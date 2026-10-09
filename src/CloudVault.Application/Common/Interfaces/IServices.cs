using CloudVault.Application.Common.Models;
using CloudVault.Application.DTOs.Auth;
using CloudVault.Application.DTOs.Files;
using CloudVault.Application.DTOs.Folders;
using CloudVault.Application.DTOs.Profile;
using CloudVault.Application.DTOs.Storage;

namespace CloudVault.Application.Common.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<AuthResponseDto> LoginAsync(LoginRequestDto request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);
    Task RevokeTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);
    Task<UserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

public interface IProfileService
{
    Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileDto request, CancellationToken cancellationToken = default);
    Task<ProfilePhotoResultDto> UploadPhotoAsync(Guid userId, Stream photoStream, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task<(Stream Stream, string ContentType)?> GetPhotoStreamAsync(Guid userId, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(Guid userId, ChangePasswordDto request, CancellationToken cancellationToken = default);
}

public interface IFolderService
{
    Task<FolderDto> CreateFolderAsync(Guid userId, CreateFolderDto request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FolderDto>> GetFoldersAsync(Guid userId, Guid? parentFolderId = null, CancellationToken cancellationToken = default);
    Task<FolderDetailDto> GetFolderByIdAsync(Guid userId, Guid folderId, CancellationToken cancellationToken = default);
    Task<FolderDto> UpdateFolderAsync(Guid userId, Guid folderId, UpdateFolderDto request, CancellationToken cancellationToken = default);
    Task DeleteFolderAsync(Guid userId, Guid folderId, CancellationToken cancellationToken = default);
}

public interface IFileService
{
    Task<FileUploadResultDto> UploadFileAsync(Guid userId, Stream stream, string fileName, string contentType, long fileSize, Guid? folderId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileUploadResultDto>> UploadMultipleFilesAsync(Guid userId, IEnumerable<(Stream Stream, string FileName, string ContentType, long FileSize)> files, Guid? folderId = null, CancellationToken cancellationToken = default);
    Task<PagedResult<FileDto>> GetFilesAsync(Guid userId, FileFilterQuery filter, CancellationToken cancellationToken = default);
    Task<FileDto> GetFileByIdAsync(Guid userId, Guid fileId, CancellationToken cancellationToken = default);
    Task<FileDownloadDto> DownloadFileAsync(Guid userId, Guid fileId, bool directDownload = false, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(Guid userId, Guid fileId, CancellationToken cancellationToken = default);
    Task<FileDto> RenameFileAsync(Guid userId, Guid fileId, RenameFileDto request, CancellationToken cancellationToken = default);
    Task<FileDto> MoveFileAsync(Guid userId, Guid fileId, MoveFileDto request, CancellationToken cancellationToken = default);
}

public interface IStorageQuotaService
{
    Task CheckQuotaAsync(Guid userId, long additionalBytes, CancellationToken cancellationToken = default);
    Task AllocateQuotaAsync(Guid userId, long bytes, CancellationToken cancellationToken = default);
    Task ReleaseQuotaAsync(Guid userId, long bytes, CancellationToken cancellationToken = default);
    Task<StorageUsageDto> GetUsageAsync(Guid userId, CancellationToken cancellationToken = default);
}

public interface IAuditLogService
{
    Task LogAsync(Guid? userId, string action, string? details = null, string? ipAddress = null, CancellationToken cancellationToken = default);
}
