using CloudVault.Application.Common.Helpers;
using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.Common.Models;
using CloudVault.Application.Common.Options;
using CloudVault.Application.DTOs.Files;
using CloudVault.Domain.Entities;
using CloudVault.Domain.Exceptions;
using CloudVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudVault.Infrastructure.Services;

public class FileService : IFileService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFileStorageService _storageService;
    private readonly IStorageQuotaService _quotaService;
    private readonly StorageOptions _storageOptions;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<FileService> _logger;

    public FileService(
        ApplicationDbContext dbContext,
        IFileStorageService storageService,
        IStorageQuotaService quotaService,
        IOptions<StorageOptions> storageOptions,
        IAuditLogService auditLogService,
        ILogger<FileService> logger)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _quotaService = quotaService;
        _storageOptions = storageOptions.Value;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<FileUploadResultDto> UploadFileAsync(
        Guid userId,
        Stream stream,
        string fileName,
        string contentType,
        long fileSize,
        Guid? folderId = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Sanitize file name
        string safeFileName = FileHelper.SanitizeFileName(fileName);

        // 2. Validate folder ownership if folder is supplied
        if (folderId.HasValue)
        {
            var folderExists = await _dbContext.Folders
                .AnyAsync(f => f.Id == folderId.Value && f.UserId == userId && !f.IsDeleted, cancellationToken);

            if (!folderExists)
            {
                throw new NotFoundException("Folder", folderId.Value);
            }
        }

        // 3. Validate file rules (extension, magic bytes, size limits)
        FileHelper.ValidateFile(
            safeFileName,
            fileSize,
            contentType,
            stream,
            _storageOptions.AllowedExtensions,
            _storageOptions.MaxFileSizeBytes);

        // 4. Validate storage quota
        await _quotaService.CheckQuotaAsync(userId, fileSize, cancellationToken);

        // 5. Generate secure, user-isolated S3 key
        var fileId = Guid.NewGuid();
        string s3Key = FileHelper.BuildS3Key(userId, fileId, safeFileName);

        // 6. Upload stream to Amazon S3 / Storage Service
        await _storageService.UploadAsync(stream, s3Key, contentType, cancellationToken);

        // 7. Save metadata in SQL Server
        var fileItem = new FileItem
        {
            Id = fileId,
            UserId = userId,
            FolderId = folderId,
            FileName = safeFileName,
            S3Key = s3Key,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            FileSize = fileSize,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Files.Add(fileItem);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 8. Allocate quota atomically
        await _quotaService.AllocateQuotaAsync(userId, fileSize, cancellationToken);

        // 9. Structured Audit Log
        await _auditLogService.LogAsync(userId, "FILE_UPLOADED", $"Uploaded file '{safeFileName}' ({fileSize} bytes)", null, cancellationToken);

        return new FileUploadResultDto
        {
            Id = fileItem.Id,
            FileName = fileItem.FileName,
            FileSize = fileItem.FileSize,
            ContentType = fileItem.ContentType,
            CreatedAt = fileItem.CreatedAt
        };
    }

    public async Task<IReadOnlyList<FileUploadResultDto>> UploadMultipleFilesAsync(
        Guid userId,
        IEnumerable<(Stream Stream, string FileName, string ContentType, long FileSize)> files,
        Guid? folderId = null,
        CancellationToken cancellationToken = default)
    {
        var fileList = files.ToList();
        if (fileList.Count == 0)
        {
            return Array.Empty<FileUploadResultDto>();
        }

        // Pre-validate total size against user quota
        long totalBatchSize = fileList.Sum(f => f.FileSize);
        await _quotaService.CheckQuotaAsync(userId, totalBatchSize, cancellationToken);

        var results = new List<FileUploadResultDto>();
        foreach (var file in fileList)
        {
            var result = await UploadFileAsync(
                userId,
                file.Stream,
                file.FileName,
                file.ContentType,
                file.FileSize,
                folderId,
                cancellationToken);

            results.Add(result);
        }

        return results;
    }

    public async Task<PagedResult<FileDto>> GetFilesAsync(
        Guid userId,
        FileFilterQuery filter,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Files
            .AsNoTracking()
            .Include(f => f.Folder)
            .Where(f => f.UserId == userId && !f.IsDeleted);

        // Folder filter
        if (filter.FolderId.HasValue)
        {
            query = query.Where(f => f.FolderId == filter.FolderId.Value);
        }
        else if (!filter.IncludeSubfolders && string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(f => f.FolderId == null);
        }

        // Search filter (file name)
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string searchTerm = filter.Search.Trim().ToLowerInvariant();
            query = query.Where(f => f.FileName.ToLower().Contains(searchTerm));
        }

        // File type filter
        if (!string.IsNullOrWhiteSpace(filter.FileType))
        {
            string type = filter.FileType.Trim().ToLowerInvariant();
            query = type switch
            {
                "image" => query.Where(f => f.FileName.EndsWith(".jpg") || f.FileName.EndsWith(".jpeg") || f.FileName.EndsWith(".png") || f.FileName.EndsWith(".gif") || f.FileName.EndsWith(".webp")),
                "document" => query.Where(f => f.FileName.EndsWith(".pdf") || f.FileName.EndsWith(".doc") || f.FileName.EndsWith(".docx") || f.FileName.EndsWith(".txt")),
                "spreadsheet" => query.Where(f => f.FileName.EndsWith(".xls") || f.FileName.EndsWith(".xlsx")),
                "presentation" => query.Where(f => f.FileName.EndsWith(".ppt") || f.FileName.EndsWith(".pptx")),
                "archive" => query.Where(f => f.FileName.EndsWith(".zip")),
                _ => query
            };
        }

        int totalCount = await query.CountAsync(cancellationToken);

        // Sorting
        query = (filter.SortBy?.ToLowerInvariant(), filter.SortDescending) switch
        {
            ("filename", true) => query.OrderByDescending(f => f.FileName),
            ("filename", false) => query.OrderBy(f => f.FileName),
            ("filesize", true) => query.OrderByDescending(f => f.FileSize),
            ("filesize", false) => query.OrderBy(f => f.FileSize),
            ("createdat", false) => query.OrderBy(f => f.CreatedAt),
            _ => query.OrderByDescending(f => f.CreatedAt)
        };

        // Pagination
        int page = filter.Page > 0 ? filter.Page : 1;
        int pageSize = filter.PageSize is > 0 and <= 100 ? filter.PageSize : 20;

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new FileDto
            {
                Id = f.Id,
                UserId = f.UserId,
                FolderId = f.FolderId,
                FolderName = f.Folder != null ? f.Folder.Name : null,
                FileName = f.FileName,
                Extension = Path.GetExtension(f.FileName).ToLowerInvariant(),
                ContentType = f.ContentType,
                FileSize = f.FileSize,
                FormattedSize = SizeFormatter.FormatBytes(f.FileSize),
                IsImage = FileHelper.IsImageExtension(Path.GetExtension(f.FileName)),
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<FileDto>(items, totalCount, page, pageSize);
    }

    public async Task<FileDto> GetFileByIdAsync(
        Guid userId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var file = await _dbContext.Files
            .AsNoTracking()
            .Include(f => f.Folder)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId && !f.IsDeleted, cancellationToken);

        if (file == null)
        {
            throw new NotFoundException("File", fileId);
        }

        return new FileDto
        {
            Id = file.Id,
            UserId = file.UserId,
            FolderId = file.FolderId,
            FolderName = file.Folder?.Name,
            FileName = file.FileName,
            Extension = Path.GetExtension(file.FileName).ToLowerInvariant(),
            ContentType = file.ContentType,
            FileSize = file.FileSize,
            FormattedSize = SizeFormatter.FormatBytes(file.FileSize),
            IsImage = FileHelper.IsImageExtension(Path.GetExtension(file.FileName)),
            CreatedAt = file.CreatedAt,
            UpdatedAt = file.UpdatedAt
        };
    }

    public async Task<FileDownloadDto> DownloadFileAsync(
        Guid userId,
        Guid fileId,
        bool directDownload = false,
        CancellationToken cancellationToken = default)
    {
        var file = await _dbContext.Files
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId && !f.IsDeleted, cancellationToken);

        if (file == null)
        {
            throw new NotFoundException("File", fileId);
        }

        await _auditLogService.LogAsync(userId, "FILE_DOWNLOADED", $"Downloaded file '{file.FileName}'", null, cancellationToken);

        if (directDownload)
        {
            var stream = await _storageService.DownloadAsync(file.S3Key, cancellationToken);
            return new FileDownloadDto
            {
                FileName = file.FileName,
                ContentType = file.ContentType,
                Stream = stream,
                FileSize = file.FileSize
            };
        }

        // Temporary secure pre-signed URL (valid for 15 minutes)
        string downloadUrl = await _storageService.GenerateDownloadUrlAsync(
            file.S3Key,
            file.FileName,
            TimeSpan.FromMinutes(15),
            cancellationToken);

        return new FileDownloadDto
        {
            FileName = file.FileName,
            ContentType = file.ContentType,
            DownloadUrl = downloadUrl,
            FileSize = file.FileSize
        };
    }

    public async Task DeleteFileAsync(
        Guid userId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var file = await _dbContext.Files
            .FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId && !f.IsDeleted, cancellationToken);

        if (file == null)
        {
            throw new NotFoundException("File", fileId);
        }

        file.IsDeleted = true;
        file.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Release user quota accurately
        await _quotaService.ReleaseQuotaAsync(userId, file.FileSize, cancellationToken);

        // Delete from S3/Storage
        try
        {
            await _storageService.DeleteAsync(file.S3Key, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete storage object {Key} for file {FileId}", file.S3Key, fileId);
        }

        await _auditLogService.LogAsync(userId, "FILE_DELETED", $"Deleted file '{file.FileName}' ({file.FileSize} bytes)", null, cancellationToken);
    }

    public async Task<FileDto> RenameFileAsync(
        Guid userId,
        Guid fileId,
        RenameFileDto request,
        CancellationToken cancellationToken = default)
    {
        var file = await _dbContext.Files
            .Include(f => f.Folder)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId && !f.IsDeleted, cancellationToken);

        if (file == null)
        {
            throw new NotFoundException("File", fileId);
        }

        string safeNewName = FileHelper.SanitizeFileName(request.NewFileName);

        // Preserve original extension if new name does not include extension
        string originalExt = Path.GetExtension(file.FileName);
        string newExt = Path.GetExtension(safeNewName);

        if (string.IsNullOrEmpty(newExt))
        {
            safeNewName += originalExt;
        }

        file.FileName = safeNewName;
        file.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogService.LogAsync(userId, "FILE_RENAMED", $"Renamed file {fileId} to '{safeNewName}'", null, cancellationToken);

        return new FileDto
        {
            Id = file.Id,
            UserId = file.UserId,
            FolderId = file.FolderId,
            FolderName = file.Folder?.Name,
            FileName = file.FileName,
            Extension = Path.GetExtension(file.FileName).ToLowerInvariant(),
            ContentType = file.ContentType,
            FileSize = file.FileSize,
            FormattedSize = SizeFormatter.FormatBytes(file.FileSize),
            IsImage = FileHelper.IsImageExtension(Path.GetExtension(file.FileName)),
            CreatedAt = file.CreatedAt,
            UpdatedAt = file.UpdatedAt
        };
    }

    public async Task<FileDto> MoveFileAsync(
        Guid userId,
        Guid fileId,
        MoveFileDto request,
        CancellationToken cancellationToken = default)
    {
        var file = await _dbContext.Files
            .Include(f => f.Folder)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId && !f.IsDeleted, cancellationToken);

        if (file == null)
        {
            throw new NotFoundException("File", fileId);
        }

        // If target folder is provided, ensure it belongs to the current user (IDOR prevention)
        if (request.TargetFolderId.HasValue)
        {
            var targetFolder = await _dbContext.Folders
                .FirstOrDefaultAsync(f => f.Id == request.TargetFolderId.Value && f.UserId == userId && !f.IsDeleted, cancellationToken);

            if (targetFolder == null)
            {
                throw new NotFoundException("Target Folder", request.TargetFolderId.Value);
            }
        }

        file.FolderId = request.TargetFolderId;
        file.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogService.LogAsync(userId, "FILE_MOVED", $"Moved file {fileId} to folder {request.TargetFolderId}", null, cancellationToken);

        return await GetFileByIdAsync(userId, fileId, cancellationToken);
    }
}
