using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.Common.Options;
using CloudVault.Application.DTOs.Auth;
using CloudVault.Domain.Entities;
using CloudVault.Domain.Exceptions;
using CloudVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CloudVault.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly JwtOptions _jwtOptions;
    private readonly StorageOptions _storageOptions;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        IOptions<JwtOptions> jwtOptions,
        IOptions<StorageOptions> storageOptions,
        IAuditLogService auditLogService,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _jwtOptions = jwtOptions.Value;
        _storageOptions = storageOptions.Value;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<AuthResponseDto> RegisterAsync(
        RegisterRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        string normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existingUser = await _userManager.FindByEmailAsync(normalizedEmail);
        if (existingUser != null)
        {
            _logger.LogWarning("Registration attempt with existing email: {Email}", normalizedEmail);
            throw new DuplicateResourceException("An account with this email address already exists.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = normalizedEmail,
            Email = normalizedEmail,
            FullName = request.FullName.Trim(),
            StorageLimitBytes = _storageOptions.DefaultQuotaBytes,
            StorageUsedBytes = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = result.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

            throw new ValidationException(errors);
        }

        _logger.LogInformation("User registered successfully. UserId: {UserId}, Email: {Email}", user.Id, user.Email);
        await _auditLogService.LogAsync(user.Id, "USER_REGISTERED", $"User registered with email {user.Email}", ipAddress, cancellationToken);

        return await GenerateAuthResponseAsync(user, ipAddress, cancellationToken);
    }

    public async Task<AuthResponseDto> LoginAsync(
        LoginRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        string normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _userManager.FindByEmailAsync(normalizedEmail);

        if (user == null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            _logger.LogWarning("Failed login attempt for email {Email}", normalizedEmail);
            await _auditLogService.LogAsync(null, "LOGIN_FAILED", $"Failed login attempt for {normalizedEmail}", ipAddress, cancellationToken);
            throw new UnauthorizedException("Invalid email or password.");
        }

        _logger.LogInformation("User logged in successfully. UserId: {UserId}", user.Id);
        await _auditLogService.LogAsync(user.Id, "USER_LOGGED_IN", "User logged in", ipAddress, cancellationToken);

        return await GenerateAuthResponseAsync(user, ipAddress, cancellationToken);
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(
        string refreshToken,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var tokenRecord = await _dbContext.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == refreshToken, cancellationToken);

        if (tokenRecord == null || !tokenRecord.IsActive)
        {
            _logger.LogWarning("Invalid or inactive refresh token used.");
            throw new UnauthorizedException("Invalid or expired refresh token.");
        }

        // Revoke the old token (token rotation pattern)
        tokenRecord.RevokedAt = DateTimeOffset.UtcNow;
        string newRefreshTokenString = GenerateRefreshTokenString();
        tokenRecord.ReplacedByToken = newRefreshTokenString;

        // Create the new refresh token record
        var newRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = tokenRecord.UserId,
            Token = newRefreshTokenString,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenExpiryDays)
        };

        _dbContext.RefreshTokens.Add(newRefreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var (accessToken, expiresAt) = GenerateJwtToken(tokenRecord.User);

        await _auditLogService.LogAsync(tokenRecord.UserId, "TOKEN_REFRESHED", "Refreshed access token", ipAddress, cancellationToken);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshTokenString,
            ExpiresAt = expiresAt,
            User = MapToUserDto(tokenRecord.User)
        };
    }

    public async Task RevokeTokenAsync(
        string refreshToken,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var tokenRecord = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == refreshToken, cancellationToken);

        if (tokenRecord == null || !tokenRecord.IsActive)
        {
            return;
        }

        tokenRecord.RevokedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(tokenRecord.UserId, "TOKEN_REVOKED", "Revoked refresh token", ipAddress, cancellationToken);
    }

    public async Task<UserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("User", userId);
        }

        return MapToUserDto(user);
    }

    private async Task<AuthResponseDto> GenerateAuthResponseAsync(
        ApplicationUser user,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var (accessToken, expiresAt) = GenerateJwtToken(user);
        string refreshTokenString = GenerateRefreshTokenString();

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshTokenString,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenExpiryDays)
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenString,
            ExpiresAt = expiresAt,
            User = MapToUserDto(user)
        };
    }

    private (string Token, DateTimeOffset ExpiresAt) GenerateJwtToken(ApplicationUser user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtOptions.Secret);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_jwtOptions.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt.UtcDateTime,
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return (tokenHandler.WriteToken(token), expiresAt);
    }

    private static string GenerateRefreshTokenString()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    private static UserDto MapToUserDto(ApplicationUser user)
    {
        return new UserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            ProfilePictureUrl = !string.IsNullOrEmpty(user.ProfilePictureS3Key) ? $"/api/profile/photo" : null,
            StorageLimitBytes = user.StorageLimitBytes,
            StorageUsedBytes = user.StorageUsedBytes,
            CreatedAt = user.CreatedAt
        };
    }
}
