using System.Security.Claims;
using CloudVault.Application.Common.Interfaces;
using CloudVault.Domain.Exceptions;

namespace CloudVault.API.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier);
            if (claim != null && Guid.TryParse(claim.Value, out var guid))
            {
                return guid;
            }
            return null;
        }
    }

    public string? Email => _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value;

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    public Guid GetRequiredUserId()
    {
        return UserId ?? throw new UnauthorizedException("User is not authenticated or token is invalid.");
    }
}
