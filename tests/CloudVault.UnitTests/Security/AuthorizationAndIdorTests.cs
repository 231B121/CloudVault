using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.Common.Options;
using CloudVault.Application.DTOs.Files;
using CloudVault.Application.DTOs.Folders;
using CloudVault.Domain.Entities;
using CloudVault.Domain.Exceptions;
using CloudVault.Infrastructure.Services;
using CloudVault.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CloudVault.UnitTests.Security;

public class AuthorizationAndIdorTests
{
    private readonly Mock<IFileStorageService> _storageServiceMock = new();
    private readonly Mock<IStorageQuotaService> _quotaServiceMock = new();
    private readonly Mock<IAuditLogService> _auditLogMock = new();
    private readonly Mock<ILogger<FileService>> _fileLoggerMock = new();
    private readonly Mock<ILogger<FolderService>> _folderLoggerMock = new();
    private readonly IOptions<StorageOptions> _storageOptions = Options.Create(new StorageOptions());

    [Fact]
    public async Task GetFileById_UserACannotAccessUserBFile_ThrowsNotFoundException()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        db.Files.Add(new FileItem
        {
            Id = fileId,
            UserId = userBId, // Belongs to User B
            FileName = "userB-confidential.pdf",
            S3Key = $"users/{userBId}/files/{fileId}/userB-confidential.pdf",
            ContentType = "application/pdf",
            FileSize = 100_000,
            IsDeleted = false
        });
        await db.SaveChangesAsync();

        var service = new FileService(
            db,
            _storageServiceMock.Object,
            _quotaServiceMock.Object,
            _storageOptions,
            _auditLogMock.Object,
            _fileLoggerMock.Object);

        // Act & Assert (User A attempts to access User B's file)
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetFileByIdAsync(userAId, fileId));
    }

    [Fact]
    public async Task DeleteFile_UserACannotDeleteUserBFile_ThrowsNotFoundException()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        db.Files.Add(new FileItem
        {
            Id = fileId,
            UserId = userBId,
            FileName = "userB-data.pdf",
            S3Key = "k",
            ContentType = "application/pdf",
            FileSize = 50_000,
            IsDeleted = false
        });
        await db.SaveChangesAsync();

        var service = new FileService(
            db,
            _storageServiceMock.Object,
            _quotaServiceMock.Object,
            _storageOptions,
            _auditLogMock.Object,
            _fileLoggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteFileAsync(userAId, fileId));

        // Ensure file is NOT deleted
        var fileInDb = await db.Files.FindAsync(fileId);
        fileInDb!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task RenameFile_UserACannotRenameUserBFile_ThrowsNotFoundException()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        db.Files.Add(new FileItem
        {
            Id = fileId,
            UserId = userBId,
            FileName = "userB-original.pdf",
            S3Key = "k",
            ContentType = "application/pdf",
            FileSize = 50_000,
            IsDeleted = false
        });
        await db.SaveChangesAsync();

        var service = new FileService(
            db,
            _storageServiceMock.Object,
            _quotaServiceMock.Object,
            _storageOptions,
            _auditLogMock.Object,
            _fileLoggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.RenameFileAsync(userAId, fileId, new RenameFileDto { NewFileName = "hacked.pdf" }));
    }

    [Fact]
    public async Task MoveFile_UserACannotMoveFileToUserBFolder_ThrowsNotFoundException()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var userAFileId = Guid.NewGuid();
        var userBFolderId = Guid.NewGuid();

        db.Files.Add(new FileItem
        {
            Id = userAFileId,
            UserId = userAId,
            FileName = "userA-file.txt",
            S3Key = "k",
            ContentType = "text/plain",
            FileSize = 100,
            IsDeleted = false
        });

        db.Folders.Add(new Folder
        {
            Id = userBFolderId,
            UserId = userBId, // User B's folder!
            Name = "PrivateFolderUserB",
            IsDeleted = false
        });
        await db.SaveChangesAsync();

        var service = new FileService(
            db,
            _storageServiceMock.Object,
            _quotaServiceMock.Object,
            _storageOptions,
            _auditLogMock.Object,
            _fileLoggerMock.Object);

        // Act & Assert (User A tries to move file into User B's folder)
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.MoveFileAsync(userAId, userAFileId, new MoveFileDto { TargetFolderId = userBFolderId }));
    }

    [Fact]
    public async Task GetFolderById_UserACannotAccessUserBFolder_ThrowsNotFoundException()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var folderId = Guid.NewGuid();

        db.Folders.Add(new Folder
        {
            Id = folderId,
            UserId = userBId,
            Name = "User B Secret Docs",
            IsDeleted = false
        });
        await db.SaveChangesAsync();

        var service = new FolderService(
            db,
            _storageServiceMock.Object,
            _quotaServiceMock.Object,
            _auditLogMock.Object,
            _folderLoggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetFolderByIdAsync(userAId, folderId));
    }

    [Fact]
    public async Task DeleteFolder_UserACannotDeleteUserBFolder_ThrowsNotFoundException()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var folderId = Guid.NewGuid();

        db.Folders.Add(new Folder
        {
            Id = folderId,
            UserId = userBId,
            Name = "User B Folder",
            IsDeleted = false
        });
        await db.SaveChangesAsync();

        var service = new FolderService(
            db,
            _storageServiceMock.Object,
            _quotaServiceMock.Object,
            _auditLogMock.Object,
            _folderLoggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteFolderAsync(userAId, folderId));

        var folderInDb = await db.Folders.FindAsync(folderId);
        folderInDb!.IsDeleted.Should().BeFalse();
    }
}
