using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.Common.Options;
using CloudVault.Application.DTOs.Auth;
using CloudVault.Domain.Entities;
using CloudVault.Domain.Exceptions;
using CloudVault.Infrastructure.Services;
using CloudVault.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CloudVault.UnitTests.Authentication;

public class AuthServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<IAuditLogService> _auditLogMock;
    private readonly Mock<ILogger<AuthService>> _loggerMock;
    private readonly IOptions<JwtOptions> _jwtOptions;
    private readonly IOptions<StorageOptions> _storageOptions;

    public AuthServiceTests()
    {
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _auditLogMock = new Mock<IAuditLogService>();
        _loggerMock = new Mock<ILogger<AuthService>>();

        _jwtOptions = Options.Create(new JwtOptions
        {
            Secret = "CloudVault_Super_Secret_Jwt_Security_Key_With_High_Entropy_2026_Required!",
            Issuer = "CloudVault",
            Audience = "CloudVaultUsers",
            ExpiryMinutes = 60,
            RefreshTokenExpiryDays = 7
        });

        _storageOptions = Options.Create(new StorageOptions
        {
            DefaultQuotaBytes = 1_073_741_824
        });
    }

    [Fact]
    public async Task RegisterAsync_ValidRequest_CreatesUserAndReturnsTokens()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new AuthService(
            _userManagerMock.Object,
            db,
            _jwtOptions,
            _storageOptions,
            _auditLogMock.Object,
            _loggerMock.Object);

        var request = new RegisterRequestDto
        {
            FullName = "Jane Doe",
            Email = "jane@example.com",
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        };

        _userManagerMock.Setup(m => m.FindByEmailAsync("jane@example.com"))
            .ReturnsAsync((ApplicationUser?)null);

        _userManagerMock.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), "Password123!"))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await service.RegisterAsync(request, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.User.Email.Should().Be("jane@example.com");
        result.User.FullName.Should().Be("Jane Doe");
        result.User.StorageLimitBytes.Should().Be(1_073_741_824);

        // Verify refresh token stored in DB
        db.RefreshTokens.Should().ContainSingle(r => r.Token == result.RefreshToken);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ThrowsDuplicateResourceException()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new AuthService(
            _userManagerMock.Object,
            db,
            _jwtOptions,
            _storageOptions,
            _auditLogMock.Object,
            _loggerMock.Object);

        var request = new RegisterRequestDto
        {
            FullName = "Existing User",
            Email = "exists@example.com",
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        };

        _userManagerMock.Setup(m => m.FindByEmailAsync("exists@example.com"))
            .ReturnsAsync(new ApplicationUser { Email = "exists@example.com" });

        // Act & Assert
        await Assert.ThrowsAsync<DuplicateResourceException>(() => service.RegisterAsync(request, "127.0.0.1"));
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokens()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            FullName = "Test User",
            StorageLimitBytes = 1_073_741_824
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(
            _userManagerMock.Object,
            db,
            _jwtOptions,
            _storageOptions,
            _auditLogMock.Object,
            _loggerMock.Object);

        _userManagerMock.Setup(m => m.FindByEmailAsync("user@example.com"))
            .ReturnsAsync(user);

        _userManagerMock.Setup(m => m.CheckPasswordAsync(user, "CorrectPassword123!"))
            .ReturnsAsync(true);

        var request = new LoginRequestDto
        {
            Email = "user@example.com",
            Password = "CorrectPassword123!"
        };

        // Act
        var result = await service.LoginAsync(request, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.User.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ThrowsUnauthorizedException()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            FullName = "Test User"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(
            _userManagerMock.Object,
            db,
            _jwtOptions,
            _storageOptions,
            _auditLogMock.Object,
            _loggerMock.Object);

        _userManagerMock.Setup(m => m.FindByEmailAsync("user@example.com"))
            .ReturnsAsync(user);

        _userManagerMock.Setup(m => m.CheckPasswordAsync(user, "WrongPassword"))
            .ReturnsAsync(false);

        var request = new LoginRequestDto
        {
            Email = "user@example.com",
            Password = "WrongPassword"
        };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.LoginAsync(request, "127.0.0.1"));
    }

    [Fact]
    public async Task RefreshTokenAsync_ValidToken_RotatesTokenAndReturnsNewPair()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "refresh@example.com",
            FullName = "Refresh User"
        };
        db.Users.Add(user);

        var originalRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = "valid-refresh-token-12345",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.RefreshTokens.Add(originalRefreshToken);
        await db.SaveChangesAsync();

        var service = new AuthService(
            _userManagerMock.Object,
            db,
            _jwtOptions,
            _storageOptions,
            _auditLogMock.Object,
            _loggerMock.Object);

        // Act
        var result = await service.RefreshTokenAsync("valid-refresh-token-12345", "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.RefreshToken.Should().NotBe("valid-refresh-token-12345");
        result.AccessToken.Should().NotBeNullOrEmpty();

        // Old token must be revoked
        var oldToken = db.RefreshTokens.First(r => r.Token == "valid-refresh-token-12345");
        oldToken.IsRevoked.Should().BeTrue();
        oldToken.ReplacedByToken.Should().Be(result.RefreshToken);
    }
}
