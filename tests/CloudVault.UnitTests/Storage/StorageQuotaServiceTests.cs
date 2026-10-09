using CloudVault.Domain.Entities;
using CloudVault.Domain.Exceptions;
using CloudVault.Infrastructure.Services;
using CloudVault.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CloudVault.UnitTests.Storage;

public class StorageQuotaServiceTests
{
    private readonly Mock<ILogger<StorageQuotaService>> _loggerMock = new();

    [Fact]
    public async Task CheckQuotaAsync_WithinLimit_CompletesWithoutException()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            StorageLimitBytes = 1_000_000,
            StorageUsedBytes = 400_000
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new StorageQuotaService(db, _loggerMock.Object);

        // Act & Assert (Attempting 500,000 bytes => 400,000 + 500,000 = 900,000 <= 1,000,000)
        var act = async () => await service.CheckQuotaAsync(userId, 500_000);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckQuotaAsync_ExceedingLimit_ThrowsQuotaExceededException()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            StorageLimitBytes = 1_000_000,
            StorageUsedBytes = 800_000
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new StorageQuotaService(db, _loggerMock.Object);

        // Act & Assert (Attempting 300,000 bytes => 800,000 + 300,000 = 1,100,000 > 1,000,000)
        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => service.CheckQuotaAsync(userId, 300_000));
        ex.StorageLimitBytes.Should().Be(1_000_000);
        ex.StorageUsedBytes.Should().Be(800_000);
        ex.AttemptedSizeBytes.Should().Be(300_000);
    }

    [Fact]
    public async Task AllocateQuotaAsync_ValidBytes_IncrementsUserUsedStorage()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            StorageLimitBytes = 1_000_000,
            StorageUsedBytes = 100_000
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new StorageQuotaService(db, _loggerMock.Object);

        // Act
        await service.AllocateQuotaAsync(userId, 250_000);

        // Assert
        var updated = await db.Users.FindAsync(userId);
        updated!.StorageUsedBytes.Should().Be(350_000);
    }

    [Fact]
    public async Task ReleaseQuotaAsync_DecrementsUserUsedStorageAccurately()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            StorageLimitBytes = 1_000_000,
            StorageUsedBytes = 600_000
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new StorageQuotaService(db, _loggerMock.Object);

        // Act (Release 200,000 bytes)
        await service.ReleaseQuotaAsync(userId, 200_000);

        // Assert
        var updated = await db.Users.FindAsync(userId);
        updated!.StorageUsedBytes.Should().Be(400_000);
    }

    [Fact]
    public async Task ReleaseQuotaAsync_MoreThanUsed_ClampsAtZero()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            StorageLimitBytes = 1_000_000,
            StorageUsedBytes = 50_000
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new StorageQuotaService(db, _loggerMock.Object);

        // Act (Release 100,000 bytes when only 50,000 used)
        await service.ReleaseQuotaAsync(userId, 100_000);

        // Assert
        var updated = await db.Users.FindAsync(userId);
        updated!.StorageUsedBytes.Should().Be(0);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsAccurateUsageMetricsAndPercentage()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            StorageLimitBytes = 1_073_741_824, // 1 GB
            StorageUsedBytes = 536_870_912     // 512 MB (50%)
        };
        db.Users.Add(user);

        db.Files.Add(new FileItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FileName = "test1.pdf",
            S3Key = "k1",
            ContentType = "application/pdf",
            FileSize = 500_000,
            IsDeleted = false
        });

        db.Folders.Add(new Folder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Docs",
            IsDeleted = false
        });

        await db.SaveChangesAsync();

        var service = new StorageQuotaService(db, _loggerMock.Object);

        // Act
        var usage = await service.GetUsageAsync(userId);

        // Assert
        usage.StorageLimitBytes.Should().Be(1_073_741_824);
        usage.StorageUsedBytes.Should().Be(536_870_912);
        usage.AvailableBytes.Should().Be(536_870_912);
        usage.UsagePercentage.Should().Be(50.0);
        usage.TotalFiles.Should().Be(1);
        usage.TotalFolders.Should().Be(1);
        usage.FormattedUsed.Should().Contain("MB");
    }
}
