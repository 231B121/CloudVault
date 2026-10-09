using Amazon.S3;
using Amazon.S3.Model;
using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudVault.Infrastructure.Storage;

public class S3FileStorageService : IFileStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<S3FileStorageService> _logger;

    public S3FileStorageService(
        IAmazonS3 s3Client,
        IOptions<StorageOptions> storageOptions,
        ILogger<S3FileStorageService> logger)
    {
        _s3Client = s3Client;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        Stream stream,
        string s3Key,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var putRequest = new PutObjectRequest
            {
                BucketName = _storageOptions.S3BucketName,
                Key = s3Key,
                InputStream = stream,
                ContentType = contentType,
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
                AutoCloseStream = false
            };

            var response = await _s3Client.PutObjectAsync(putRequest, cancellationToken);
            _logger.LogInformation("Successfully uploaded object to S3. Key: {S3Key}, Bucket: {Bucket}", s3Key, _storageOptions.S3BucketName);
            return s3Key;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "AWS S3 error while uploading key {S3Key}: {Message}", s3Key, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error uploading key {S3Key}: {Message}", s3Key, ex.Message);
            throw;
        }
    }

    public async Task<Stream> DownloadAsync(string s3Key, CancellationToken cancellationToken = default)
    {
        try
        {
            var getRequest = new GetObjectRequest
            {
                BucketName = _storageOptions.S3BucketName,
                Key = s3Key
            };

            var response = await _s3Client.GetObjectAsync(getRequest, cancellationToken);
            var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "AWS S3 error while downloading key {S3Key}: {Message}", s3Key, ex.Message);
            throw;
        }
    }

    public Task<string> GenerateDownloadUrlAsync(
        string s3Key,
        string fileName,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var preSignedUrlRequest = new GetPreSignedUrlRequest
            {
                BucketName = _storageOptions.S3BucketName,
                Key = s3Key,
                Expires = DateTime.UtcNow.Add(expiration),
                Verb = HttpVerb.GET,
                ResponseHeaderOverrides = new ResponseHeaderOverrides
                {
                    ContentDisposition = $"attachment; filename=\"{fileName}\""
                }
            };

            string url = _s3Client.GetPreSignedURL(preSignedUrlRequest);
            return Task.FromResult(url);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "AWS S3 error while generating pre-signed URL for {S3Key}: {Message}", s3Key, ex.Message);
            throw;
        }
    }

    public async Task DeleteAsync(string s3Key, CancellationToken cancellationToken = default)
    {
        try
        {
            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = _storageOptions.S3BucketName,
                Key = s3Key
            };

            await _s3Client.DeleteObjectAsync(deleteRequest, cancellationToken);
            _logger.LogInformation("Successfully deleted object from S3. Key: {S3Key}", s3Key);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "AWS S3 error while deleting key {S3Key}: {Message}", s3Key, ex.Message);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string s3Key, CancellationToken cancellationToken = default)
    {
        try
        {
            var metaRequest = new GetObjectMetadataRequest
            {
                BucketName = _storageOptions.S3BucketName,
                Key = s3Key
            };

            await _s3Client.GetObjectMetadataAsync(metaRequest, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking existence for key {S3Key}: {Message}", s3Key, ex.Message);
            return false;
        }
    }
}
