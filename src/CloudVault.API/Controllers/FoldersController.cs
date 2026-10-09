using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.Common.Models;
using CloudVault.Application.DTOs.Folders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloudVault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FoldersController : ControllerBase
{
    private readonly IFolderService _folderService;
    private readonly ICurrentUserService _currentUserService;

    public FoldersController(IFolderService folderService, ICurrentUserService currentUserService)
    {
        _folderService = folderService;
        _currentUserService = currentUserService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<FolderDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var folder = await _folderService.CreateFolderAsync(userId, request, cancellationToken);
        return CreatedAtAction(nameof(GetFolderById), new { id = folder.Id }, ApiResponse<FolderDto>.Ok(folder, "Folder created successfully."));
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<FolderDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFolders([FromQuery] Guid? parentId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var folders = await _folderService.GetFoldersAsync(userId, parentId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<FolderDto>>.Ok(folders));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<FolderDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFolderById(Guid id, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var folder = await _folderService.GetFolderByIdAsync(userId, id, cancellationToken);
        return Ok(ApiResponse<FolderDetailDto>.Ok(folder));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<FolderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateFolder(Guid id, [FromBody] UpdateFolderDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var updated = await _folderService.UpdateFolderAsync(userId, id, request, cancellationToken);
        return Ok(ApiResponse<FolderDto>.Ok(updated, "Folder renamed successfully."));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFolder(Guid id, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        await _folderService.DeleteFolderAsync(userId, id, cancellationToken);
        return Ok(ApiResponse.Ok("Folder deleted successfully."));
    }
}
