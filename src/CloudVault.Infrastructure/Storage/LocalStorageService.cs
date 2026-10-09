using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudVault.Infrastructure.Storage;

public class LocalStorageService : IFileStorageService
{
    private readonly string _basePath;
    private readonly ILogger<LocalStorageService> _logger;

    public LocalStorageService(
        IOptions<StorageOptions> storageOptions,
        ILogger<LocalStorageService> logger)
    {
        _logger = logger;
        string configPath = storageOptions.Value.LocalStoragePath;
        _basePath = Path.IsPathRooted(configPath)
            ? configPath
            : Path.Combine(AppContext.BaseDirectory, configPath);

        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }
    }

    private string GetFullPath(string s3Key)
    {
        // Normalize key to local safe path
        string normalizedKey = s3Key.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        return Path.Combine(_basePath, normalizedKey);
    }

    public async Task<string> UploadAsync(
        Stream stream,
        string s3Key,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        string fullPath = GetFullPath(s3Key);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }
        await stream.CopyToAsync(fileStream, cancellationToken);
        _logger.LogInformation("Stored file locally at {Path}", fullPath);
        return s3Key;
    }

    public Task<Stream> DownloadAsync(string s3Key, CancellationToken cancellationToken = default)
    {
        string fullPath = GetFullPath(s3Key);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File not found at {fullPath}");
        }

        Stream fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(fileStream);
    }

    public Task<string> GenerateDownloadUrlAsync(
        string s3Key,
        string fileName,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        // For local storage, returns the direct API download endpoint
        return Task.FromResult($"/api/files/download-direct?key={Uri.EscapeDataString(s3Key)}&name={Uri.EscapeDataString(fileName)}");
    }

    public Task DeleteAsync(string s3Key, CancellationToken cancellationToken = default)
    {
        string fullPath = GetFullPath(s3Key);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("Deleted local file at {Path}", fullPath);
        }
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string s3Key, CancellationToken cancellationToken = default)
    {
        string fullPath = GetFullPath(s3Key);
        return Task.FromResult(File.Exists(fullPath));
    }
}
