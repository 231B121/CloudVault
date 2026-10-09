using System.Text;
using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.Common.Options;
using CloudVault.Application.DTOs.Files;
using CloudVault.Domain.Entities;
using CloudVault.Domain.Exceptions;
using CloudVault.Infrastructure.Services;
using CloudVault.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CloudVault.UnitTests.Files;

public class FileServiceTests
{
    private readonly Mock<IFileStorageService> _storageServiceMock = new();
    private readonly Mock<IStorageQuotaService> _quotaServiceMock = new();
    private readonly Mock<IAuditLogService> _auditLogMock = new();
    private readonly Mock<ILogger<FileService>> _loggerMock = new();
    private readonly IOptions<StorageOptions> _storageOptions;

    public FileServiceTests()
    {
        _storageOptions = Options.Create(new StorageOptions
        {
            AllowedExtensions = new[] { ".pdf", ".docx", ".txt", ".png", ".jpg", ".zip" },
            MaxFileSizeBytes = 10_000_000, // 10 MB
            DefaultQuotaBytes = 1_000_000_000
        });

        _storageServiceMock
            .Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string key, string _, CancellationToken _) => key);
    }

    [Fact]
    public async Task UploadFileAsync_ValidFile_SavesMetadataAndAllocatesQuota()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, StorageLimitBytes = 100_000_000 };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new FileService(
            db,
            _storageServiceMock.Object,
            _quotaServiceMock.Object,
            _storageOptions,
            _auditLogMock.Object,
            _loggerMock.Object);

        byte[] content = Encoding.UTF8.GetBytes("Sample text file content for cloud storage testing");
        using var stream = new MemoryStream(content);

        // Act
        var result = await service.UploadFileAsync(userId, stream, "test-document.txt", "text/plain", content.Length);

        // Assert
        result.Should().NotBeNull();
        result.FileName.Should().Be("test-document.txt");
        result.FileSize.Should().Be(content.Length);

        // Verify metadata persisted in DB
        var savedFile = await db.Files.FindAsync(result.Id);
        savedFile.Should().NotBeNull();
        savedFile!.UserId.Should().Be(userId);
        savedFile.S3Key.Should().Contain($"users/{userId}/files/{result.Id}/test-document.txt");
        savedFile.IsDeleted.Should().BeFalse();

        // Verify quota service was called
        _quotaServiceMock.Verify(q => q.CheckQuotaAsync(userId, content.Length, It.IsAny<CancellationToken>()), Times.Once);
        _quotaServiceMock.Verify(q => q.AllocateQuotaAsync(userId, content.Length, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadFileAsync_InvalidExtension_ThrowsValidationException()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();

        var service = new FileService(
            db,
            _storageServiceMock.Object,
            _quotaServiceMock.Object,
            _storageOptions,
            _auditLogMock.Object,
            _loggerMock.Object);

        byte[] content = new byte[] { 0x4D, 0x5A, 0x90, 0x00 }; // .exe header
        using var stream = new MemoryStream(content);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.UploadFileAsync(userId, stream, "malicious.exe", "application/x-msdownload", content.Length));

        ex.Message.Should().Contain("not permitted");
    }

    [Fact]
    public async Task UploadFileAsync_EmptyFile_ThrowsValidationException()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();

        var service = new FileService(
            db,
            _storageServiceMock.Object,
            _quotaServiceMock.Object,
            _storageOptions,
            _auditLogMock.Object,
            _loggerMock.Object);

        using var stream = new MemoryStream();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.UploadFileAsync(userId, stream, "empty.txt", "text/plain", 0));

        ex.Message.Should().Contain("Empty files cannot be uploaded");
    }

    [Fact]
    public async Task UploadFileAsync_SanitizesMaliciousPathTraversalInFileName()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new FileService(
            db,
            _storageServiceMock.Object,
            _quotaServiceMock.Object,
            _storageOptions,
            _auditLogMock.Object,
            _loggerMock.Object);

        byte[] content = Encoding.UTF8.GetBytes("test");
        using var stream = new MemoryStream(content);

        // Act (Filename with directory traversal attack)
        var result = await service.UploadFileAsync(userId, stream, "../../../etc/passwd.txt", "text/plain", content.Length);

        // Assert
        result.FileName.Should().NotContain("..");
        result.FileName.Should().NotContain("/");
        result.FileName.Should().Be("passwd.txt");
    }

    [Fact]
    public async Task DeleteFileAsync_ReleasesQuotaAndDeletesFromStorage()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        var file = new FileItem
        {
            Id = fileId,
            UserId = userId,
            FileName = "receipt.pdf",
            S3Key = $"users/{userId}/files/{fileId}/receipt.pdf",
            ContentType = "application/pdf",
            FileSize = 50_000,
            IsDeleted = false
        };
        db.Files.Add(file);
        await db.SaveChangesAsync();

        var service = new FileService(
            db,
            _storageServiceMock.Object,
            _quotaServiceMock.Object,
            _storageOptions,
            _auditLogMock.Object,
            _loggerMock.Object);

        // Act
        await service.DeleteFileAsync(userId, fileId);

        // Assert
        var deletedFile = await db.Files.FindAsync(fileId);
        deletedFile!.IsDeleted.Should().BeTrue();

        // Must accurately release quota
        _quotaServiceMock.Verify(q => q.ReleaseQuotaAsync(userId, 50_000, It.IsAny<CancellationToken>()), Times.Once);

        // Must delete from storage
        _storageServiceMock.Verify(s => s.DeleteAsync(file.S3Key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenameFileAsync_UpdatesFileNameAndPreservesExtension()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        var file = new FileItem
        {
            Id = fileId,
            UserId = userId,
            FileName = "old-report.pdf",
            S3Key = $"users/{userId}/files/{fileId}/old-report.pdf",
            ContentType = "application/pdf",
            FileSize = 100_000,
            IsDeleted = false
        };
        db.Files.Add(file);
        await db.SaveChangesAsync();

        var service = new FileService(
            db,
            _storageServiceMock.Object,
            _quotaServiceMock.Object,
            _storageOptions,
            _auditLogMock.Object,
            _loggerMock.Object);

        // Act (Renaming to "new-report" without specifying extension)
        var result = await service.RenameFileAsync(userId, fileId, new RenameFileDto { NewFileName = "new-report" });

        // Assert
        result.FileName.Should().Be("new-report.pdf");
        var updated = await db.Files.FindAsync(fileId);
        updated!.FileName.Should().Be("new-report.pdf");
    }

    [Fact]
    public async Task GetFilesAsync_WithSearchAndPagination_FiltersAccurately()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();

        for (int i = 1; i <= 25; i++)
        {
            db.Files.Add(new FileItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FileName = i <= 5 ? $"invoice_{i}.pdf" : $"other_doc_{i}.txt",
                S3Key = $"k_{i}",
                ContentType = i <= 5 ? "application/pdf" : "text/plain",
                FileSize = 1000 * i,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(i),
                IsDeleted = false
            });
        }
        await db.SaveChangesAsync();

        var service = new FileService(
            db,
            _storageServiceMock.Object,
            _quotaServiceMock.Object,
            _storageOptions,
            _auditLogMock.Object,
            _loggerMock.Object);

        // Act (Search for "invoice", page 1, pageSize 10)
        var result = await service.GetFilesAsync(userId, new FileFilterQuery
        {
            Search = "invoice",
            Page = 1,
            PageSize = 10
        });

        // Assert
        result.TotalCount.Should().Be(5);
        result.Items.Should().HaveCount(5);
        result.Items.Should().OnlyContain(f => f.FileName.Contains("invoice"));
    }
}
