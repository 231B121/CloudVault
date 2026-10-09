using CloudVault.Application.Common.Helpers;
using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.DTOs.Storage;
using CloudVault.Domain.Exceptions;
using CloudVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloudVault.Infrastructure.Services;

public class StorageQuotaService : IStorageQuotaService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<StorageQuotaService> _logger;

    public StorageQuotaService(ApplicationDbContext dbContext, ILogger<StorageQuotaService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task CheckQuotaAsync(Guid userId, long additionalBytes, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("User", userId);
        }

        if (user.StorageUsedBytes + additionalBytes > user.StorageLimitBytes)
        {
            _logger.LogWarning("Storage quota exceeded for user {UserId}. Used: {Used}, Limit: {Limit}, Attempted: {Attempted}",
                userId, user.StorageUsedBytes, user.StorageLimitBytes, additionalBytes);

            throw new QuotaExceededException(user.StorageLimitBytes, user.StorageUsedBytes, additionalBytes);
        }
    }

    public async Task AllocateQuotaAsync(Guid userId, long bytes, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("User", userId);
        }

        if (user.StorageUsedBytes + bytes > user.StorageLimitBytes)
        {
            throw new QuotaExceededException(user.StorageLimitBytes, user.StorageUsedBytes, bytes);
        }

        user.StorageUsedBytes += bytes;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Allocated {Bytes} bytes for user {UserId}. New total: {Total}", bytes, userId, user.StorageUsedBytes);
    }

    public async Task ReleaseQuotaAsync(Guid userId, long bytes, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            return;
        }

        user.StorageUsedBytes = Math.Max(0, user.StorageUsedBytes - bytes);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Released {Bytes} bytes for user {UserId}. New total: {Total}", bytes, userId, user.StorageUsedBytes);
    }

    public async Task<StorageUsageDto> GetUsageAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("User", userId);
        }

        int totalFiles = await _dbContext.Files
            .CountAsync(f => f.UserId == userId && !f.IsDeleted, cancellationToken);

        int totalFolders = await _dbContext.Folders
            .CountAsync(f => f.UserId == userId && !f.IsDeleted, cancellationToken);

        return new StorageUsageDto
        {
            StorageLimitBytes = user.StorageLimitBytes,
            StorageUsedBytes = user.StorageUsedBytes,
            FormattedUsed = SizeFormatter.FormatBytes(user.StorageUsedBytes),
            FormattedLimit = SizeFormatter.FormatBytes(user.StorageLimitBytes),
            FormattedAvailable = SizeFormatter.FormatBytes(Math.Max(0, user.StorageLimitBytes - user.StorageUsedBytes)),
            TotalFiles = totalFiles,
            TotalFolders = totalFolders
        };
    }
}
