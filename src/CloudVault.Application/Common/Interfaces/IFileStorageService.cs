namespace CloudVault.Application.Common.Interfaces;

public interface IFileStorageService
{
    Task<string> UploadAsync(Stream stream, string s3Key, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(string s3Key, CancellationToken cancellationToken = default);
    Task<string> GenerateDownloadUrlAsync(string s3Key, string fileName, TimeSpan expiration, CancellationToken cancellationToken = default);
    Task DeleteAsync(string s3Key, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string s3Key, CancellationToken cancellationToken = default);
}
