using CloudVault.Application.Common.Helpers;
using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.DTOs.Profile;
using CloudVault.Domain.Entities;
using CloudVault.Domain.Exceptions;
using CloudVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloudVault.Infrastructure.Services;

public class ProfileService : IProfileService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IFileStorageService _storageService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ProfileService> _logger;

    public ProfileService(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IFileStorageService storageService,
        IAuditLogService auditLogService,
        ILogger<ProfileService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _storageService = storageService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("User", userId);
        }

        double usagePercentage = user.StorageLimitBytes > 0
            ? Math.Round(((double)user.StorageUsedBytes / user.StorageLimitBytes) * 100, 2)
            : 0;

        return new UserProfileDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            ProfilePictureUrl = !string.IsNullOrEmpty(user.ProfilePictureS3Key) ? "/api/profile/photo" : null,
            StorageLimitBytes = user.StorageLimitBytes,
            StorageUsedBytes = user.StorageUsedBytes,
            UsagePercentage = usagePercentage,
            CreatedAt = user.CreatedAt
        };
    }

    public async Task<UserProfileDto> UpdateProfileAsync(
        Guid userId,
        UpdateProfileDto request,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("User", userId);
        }

        user.FullName = request.FullName.Trim();
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogService.LogAsync(userId, "PROFILE_UPDATED", "Updated user profile details", null, cancellationToken);

        return await GetProfileAsync(userId, cancellationToken);
    }

    public async Task<ProfilePhotoResultDto> UploadPhotoAsync(
        Guid userId,
        Stream photoStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("User", userId);
        }

        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!FileHelper.IsImageExtension(extension))
        {
            throw new ValidationException("Photo", "Only image files (.jpg, .jpeg, .png, .gif, .webp) are allowed for profile pictures.");
        }

        // Limit profile picture to 5MB
        if (photoStream.Length > 5 * 1024 * 1024)
        {
            throw new ValidationException("Photo", "Profile photo size cannot exceed 5 MB.");
        }

        // Clean old photo if present
        if (!string.IsNullOrEmpty(user.ProfilePictureS3Key))
        {
            try
            {
                await _storageService.DeleteAsync(user.ProfilePictureS3Key, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old profile photo {Key}", user.ProfilePictureS3Key);
            }
        }

        string s3Key = FileHelper.BuildProfilePhotoS3Key(userId, fileName);
        await _storageService.UploadAsync(photoStream, s3Key, contentType, cancellationToken);

        user.ProfilePictureS3Key = s3Key;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogService.LogAsync(userId, "PROFILE_PHOTO_UPDATED", $"Updated profile photo key: {s3Key}", null, cancellationToken);

        return new ProfilePhotoResultDto
        {
            ProfilePictureUrl = "/api/profile/photo"
        };
    }

    public async Task<(Stream Stream, string ContentType)?> GetPhotoStreamAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null || string.IsNullOrEmpty(user.ProfilePictureS3Key))
        {
            return null;
        }

        try
        {
            var stream = await _storageService.DownloadAsync(user.ProfilePictureS3Key, cancellationToken);
            string ext = Path.GetExtension(user.ProfilePictureS3Key).ToLowerInvariant();
            string contentType = ext switch
            {
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };

            return (stream, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download profile photo for user {UserId}", userId);
            return null;
        }
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        ChangePasswordDto request,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            throw new NotFoundException("User", userId);
        }

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = result.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

            throw new ValidationException(errors);
        }

        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(userId, "PASSWORD_CHANGED", "User successfully changed password", null, cancellationToken);
    }
}
