using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.Common.Models;
using CloudVault.Application.DTOs.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloudVault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StorageController : ControllerBase
{
    private readonly IStorageQuotaService _storageQuotaService;
    private readonly ICurrentUserService _currentUserService;

    public StorageController(
        IStorageQuotaService storageQuotaService,
        ICurrentUserService currentUserService)
    {
        _storageQuotaService = storageQuotaService;
        _currentUserService = currentUserService;
    }

    [HttpGet("usage")]
    [ProducesResponseType(typeof(ApiResponse<StorageUsageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsage(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var usage = await _storageQuotaService.GetUsageAsync(userId, cancellationToken);
        return Ok(ApiResponse<StorageUsageDto>.Ok(usage));
    }
}
