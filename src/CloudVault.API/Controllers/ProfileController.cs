using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.Common.Models;
using CloudVault.Application.DTOs.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloudVault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly IProfileService _profileService;
    private readonly IFileStorageService _storageService;
    private readonly ICurrentUserService _currentUserService;

    public ProfileController(
        IProfileService profileService,
        IFileStorageService storageService,
        ICurrentUserService currentUserService)
    {
        _profileService = profileService;
        _storageService = storageService;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<UserProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var profile = await _profileService.GetProfileAsync(userId, cancellationToken);
        return Ok(ApiResponse<UserProfileDto>.Ok(profile));
    }

    [HttpPut]
    [ProducesResponseType(typeof(ApiResponse<UserProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var updated = await _profileService.UpdateProfileAsync(userId, request, cancellationToken);
        return Ok(ApiResponse<UserProfileDto>.Ok(updated, "Profile updated successfully."));
    }

    [HttpPost("photo")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<ProfilePhotoResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadPhoto(IFormFile photo, CancellationToken cancellationToken)
    {
        if (photo == null || photo.Length == 0)
        {
            return BadRequest(ApiResponse.Fail("Please select a photo to upload."));
        }

        var userId = _currentUserService.GetRequiredUserId();
        using var stream = photo.OpenReadStream();
        var result = await _profileService.UploadPhotoAsync(userId, stream, photo.FileName, photo.ContentType, cancellationToken);
        return Ok(ApiResponse<ProfilePhotoResultDto>.Ok(result, "Profile photo updated successfully."));
    }

    [HttpGet("photo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhoto(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var photoResult = await _profileService.GetPhotoStreamAsync(userId, cancellationToken);

        if (photoResult == null)
        {
            return NotFound(ApiResponse.Fail("No profile photo uploaded."));
        }

        return File(photoResult.Value.Stream, photoResult.Value.ContentType);
    }

    [HttpPut("password")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        await _profileService.ChangePasswordAsync(userId, request, cancellationToken);
        return Ok(ApiResponse.Ok("Password changed successfully."));
    }
}
