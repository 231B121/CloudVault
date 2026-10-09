using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.DTOs.Folders;
using CloudVault.Domain.Entities;
using CloudVault.Domain.Exceptions;
using CloudVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloudVault.Infrastructure.Services;

public class FolderService : IFolderService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFileStorageService _storageService;
    private readonly IStorageQuotaService _quotaService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<FolderService> _logger;

    public FolderService(
        ApplicationDbContext dbContext,
        IFileStorageService storageService,
        IStorageQuotaService quotaService,
        IAuditLogService auditLogService,
        ILogger<FolderService> logger)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _quotaService = quotaService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<FolderDto> CreateFolderAsync(
        Guid userId,
        CreateFolderDto request,
        CancellationToken cancellationToken = default)
    {
        string trimmedName = request.Name.Trim();

        // If parent folder specified, ensure it belongs to the user
        if (request.ParentFolderId.HasValue)
        {
            var parentFolder = await _dbContext.Folders
                .FirstOrDefaultAsync(f => f.Id == request.ParentFolderId.Value && f.UserId == userId && !f.IsDeleted, cancellationToken);

            if (parentFolder == null)
            {
                throw new NotFoundException("Parent Folder", request.ParentFolderId.Value);
            }
        }

        // Prevent duplicate folder names under same parent
        bool exists = await _dbContext.Folders
            .AnyAsync(f => f.UserId == userId &&
                           f.ParentFolderId == request.ParentFolderId &&
                           f.Name == trimmedName &&
                           !f.IsDeleted, cancellationToken);

        if (exists)
        {
            throw new DuplicateResourceException($"A folder named '{trimmedName}' already exists in this location.");
        }

        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ParentFolderId = request.ParentFolderId,
            Name = trimmedName,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Folders.Add(folder);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(userId, "FOLDER_CREATED", $"Created folder '{folder.Name}' ({folder.Id})", null, cancellationToken);

        return new FolderDto
        {
            Id = folder.Id,
            UserId = folder.UserId,
            ParentFolderId = folder.ParentFolderId,
            Name = folder.Name,
            SubFolderCount = 0,
            FileCount = 0,
            TotalSizeBytes = 0,
            CreatedAt = folder.CreatedAt,
            UpdatedAt = folder.UpdatedAt
        };
    }

    public async Task<IReadOnlyList<FolderDto>> GetFoldersAsync(
        Guid userId,
        Guid? parentFolderId = null,
        CancellationToken cancellationToken = default)
    {
        var folders = await _dbContext.Folders
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.ParentFolderId == parentFolderId && !f.IsDeleted)
            .OrderBy(f => f.Name)
            .Select(f => new FolderDto
            {
                Id = f.Id,
                UserId = f.UserId,
                ParentFolderId = f.ParentFolderId,
                Name = f.Name,
                SubFolderCount = f.SubFolders.Count(sf => !sf.IsDeleted),
                FileCount = f.Files.Count(file => !file.IsDeleted),
                TotalSizeBytes = f.Files.Where(file => !file.IsDeleted).Sum(file => file.FileSize),
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return folders;
    }

    public async Task<FolderDetailDto> GetFolderByIdAsync(
        Guid userId,
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var folder = await _dbContext.Folders
            .AsNoTracking()
            .Include(f => f.SubFolders)
            .Include(f => f.Files)
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId && !f.IsDeleted, cancellationToken);

        if (folder == null)
        {
            throw new NotFoundException("Folder", folderId);
        }

        // Build breadcrumbs path up to root
        var breadcrumbs = new List<FolderBreadcrumbDto>();
        Guid? currentParentId = folder.ParentFolderId;

        while (currentParentId.HasValue)
        {
            var ancestor = await _dbContext.Folders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == currentParentId.Value && f.UserId == userId && !f.IsDeleted, cancellationToken);

            if (ancestor == null) break;

            breadcrumbs.Insert(0, new FolderBreadcrumbDto
            {
                Id = ancestor.Id,
                Name = ancestor.Name
            });

            currentParentId = ancestor.ParentFolderId;
        }

        breadcrumbs.Add(new FolderBreadcrumbDto
        {
            Id = folder.Id,
            Name = folder.Name
        });

        var subFolders = folder.SubFolders
            .Where(sf => !sf.IsDeleted)
            .OrderBy(sf => sf.Name)
            .Select(sf => new FolderDto
            {
                Id = sf.Id,
                UserId = sf.UserId,
                ParentFolderId = sf.ParentFolderId,
                Name = sf.Name,
                SubFolderCount = sf.SubFolders.Count(s => !s.IsDeleted),
                FileCount = sf.Files.Count(f => !f.IsDeleted),
                TotalSizeBytes = sf.Files.Where(f => !f.IsDeleted).Sum(f => f.FileSize),
                CreatedAt = sf.CreatedAt,
                UpdatedAt = sf.UpdatedAt
            })
            .ToList();

        return new FolderDetailDto
        {
            Id = folder.Id,
            UserId = folder.UserId,
            ParentFolderId = folder.ParentFolderId,
            Name = folder.Name,
            SubFolderCount = subFolders.Count,
            FileCount = folder.Files.Count(f => !f.IsDeleted),
            TotalSizeBytes = folder.Files.Where(f => !f.IsDeleted).Sum(f => f.FileSize),
            CreatedAt = folder.CreatedAt,
            UpdatedAt = folder.UpdatedAt,
            Breadcrumbs = breadcrumbs,
            SubFolders = subFolders
        };
    }

    public async Task<FolderDto> UpdateFolderAsync(
        Guid userId,
        Guid folderId,
        UpdateFolderDto request,
        CancellationToken cancellationToken = default)
    {
        var folder = await _dbContext.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId && !f.IsDeleted, cancellationToken);

        if (folder == null)
        {
            throw new NotFoundException("Folder", folderId);
        }

        string trimmedName = request.Name.Trim();

        // Check duplicate name under the same parent
        bool nameExists = await _dbContext.Folders
            .AnyAsync(f => f.UserId == userId &&
                           f.ParentFolderId == folder.ParentFolderId &&
                           f.Id != folderId &&
                           f.Name == trimmedName &&
                           !f.IsDeleted, cancellationToken);

        if (nameExists)
        {
            throw new DuplicateResourceException($"A folder named '{trimmedName}' already exists in this location.");
        }

        folder.Name = trimmedName;
        folder.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogService.LogAsync(userId, "FOLDER_RENAMED", $"Renamed folder {folderId} to '{folder.Name}'", null, cancellationToken);

        return new FolderDto
        {
            Id = folder.Id,
            UserId = folder.UserId,
            ParentFolderId = folder.ParentFolderId,
            Name = folder.Name,
            CreatedAt = folder.CreatedAt,
            UpdatedAt = folder.UpdatedAt
        };
    }

    public async Task DeleteFolderAsync(Guid userId, Guid folderId, CancellationToken cancellationToken = default)
    {
        var folder = await _dbContext.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId && !f.IsDeleted, cancellationToken);

        if (folder == null)
        {
            throw new NotFoundException("Folder", folderId);
        }

        // Recursively find all child folder IDs
        var folderIdsToDelete = new List<Guid> { folderId };
        await CollectSubFolderIdsAsync(userId, folderId, folderIdsToDelete, cancellationToken);

        // Fetch all files contained within these folders
        var filesToDelete = await _dbContext.Files
            .Where(f => f.UserId == userId && f.FolderId.HasValue && folderIdsToDelete.Contains(f.FolderId.Value) && !f.IsDeleted)
            .ToListAsync(cancellationToken);

        long totalBytesFreed = 0;

        foreach (var file in filesToDelete)
        {
            file.IsDeleted = true;
            file.UpdatedAt = DateTimeOffset.UtcNow;
            totalBytesFreed += file.FileSize;

            try
            {
                await _storageService.DeleteAsync(file.S3Key, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete storage object {Key} for file {FileId}", file.S3Key, file.Id);
            }
        }

        // Soft delete all folders
        var foldersToDelete = await _dbContext.Folders
            .Where(f => folderIdsToDelete.Contains(f.Id))
            .ToListAsync(cancellationToken);

        foreach (var f in foldersToDelete)
        {
            f.IsDeleted = true;
            f.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Accurately decrement quota
        if (totalBytesFreed > 0)
        {
            await _quotaService.ReleaseQuotaAsync(userId, totalBytesFreed, cancellationToken);
        }

        await _auditLogService.LogAsync(userId, "FOLDER_DELETED", $"Deleted folder '{folder.Name}' ({folderId}) and {filesToDelete.Count} files", null, cancellationToken);
    }

    private async Task CollectSubFolderIdsAsync(Guid userId, Guid parentId, List<Guid> accumulator, CancellationToken cancellationToken)
    {
        var children = await _dbContext.Folders
            .Where(f => f.UserId == userId && f.ParentFolderId == parentId && !f.IsDeleted)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        foreach (var childId in children)
        {
            accumulator.Add(childId);
            await CollectSubFolderIdsAsync(userId, childId, accumulator, cancellationToken);
        }
    }
}
