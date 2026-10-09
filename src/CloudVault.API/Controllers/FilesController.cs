using CloudVault.Application.Common.Helpers;
using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.Common.Models;
using CloudVault.Application.DTOs.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloudVault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;
    private readonly IFileStorageService _storageService;
    private readonly ICurrentUserService _currentUserService;

    public FilesController(
        IFileService fileService,
        IFileStorageService storageService,
        ICurrentUserService currentUserService)
    {
        _fileService = fileService;
        _storageService = storageService;
        _currentUserService = currentUserService;
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResultDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromForm] Guid? folderId,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse.Fail("Please select a file to upload."));
        }

        var userId = _currentUserService.GetRequiredUserId();
        using var stream = file.OpenReadStream();

        var result = await _fileService.UploadFileAsync(
            userId,
            stream,
            file.FileName,
            file.ContentType,
            file.Length,
            folderId,
            cancellationToken);

        return CreatedAtAction(nameof(GetFileById), new { id = result.Id }, ApiResponse<FileUploadResultDto>.Ok(result, "File uploaded successfully."));
    }

    [HttpPost("upload-multiple")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<FileUploadResultDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadMultiple(
        List<IFormFile> files,
        [FromForm] Guid? folderId,
        CancellationToken cancellationToken)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest(ApiResponse.Fail("No files selected for upload."));
        }

        var userId = _currentUserService.GetRequiredUserId();
        var filesToUpload = new List<(Stream Stream, string FileName, string ContentType, long FileSize)>();

        try
        {
            foreach (var formFile in files)
            {
                filesToUpload.Add((formFile.OpenReadStream(), formFile.FileName, formFile.ContentType, formFile.Length));
            }

            var results = await _fileService.UploadMultipleFilesAsync(userId, filesToUpload, folderId, cancellationToken);
            return Ok(ApiResponse<IReadOnlyList<FileUploadResultDto>>.Ok(results, $"{results.Count} files uploaded successfully."));
        }
        finally
        {
            foreach (var item in filesToUpload)
            {
                item.Stream.Dispose();
            }
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<FileDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFiles([FromQuery] FileFilterQuery filter, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var pagedResult = await _fileService.GetFilesAsync(userId, filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<FileDto>>.Ok(pagedResult));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<FileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFileById(Guid id, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var file = await _fileService.GetFileByIdAsync(userId, id, cancellationToken);
        return Ok(ApiResponse<FileDto>.Ok(file));
    }

    [HttpGet("{id:guid}/download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile(
        Guid id,
        [FromQuery] bool direct = true,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var downloadInfo = await _fileService.DownloadFileAsync(userId, id, direct, cancellationToken);

        if (downloadInfo.Stream != null)
        {
            string contentType = FileHelper.GetContentTypeForExtension(downloadInfo.FileName, downloadInfo.ContentType);
            return File(downloadInfo.Stream, contentType, downloadInfo.FileName);
        }

        if (!string.IsNullOrEmpty(downloadInfo.DownloadUrl))
        {
            if (downloadInfo.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect(downloadInfo.DownloadUrl);
            }
        }

        // Fallback to direct stream
        var directInfo = await _fileService.DownloadFileAsync(userId, id, directDownload: true, cancellationToken);
        if (directInfo.Stream != null)
        {
            string contentType = FileHelper.GetContentTypeForExtension(directInfo.FileName, directInfo.ContentType);
            return File(directInfo.Stream, contentType, directInfo.FileName);
        }

        return NotFound(ApiResponse.Fail("File could not be found or retrieved."));
    }

    [HttpGet("{id:guid}/view")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ViewFile(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var downloadInfo = await _fileService.DownloadFileAsync(userId, id, directDownload: true, cancellationToken);

        if (downloadInfo.Stream != null)
        {
            string contentType = FileHelper.GetContentTypeForExtension(downloadInfo.FileName, downloadInfo.ContentType);
            Response.Headers.Append("Content-Disposition", $"inline; filename=\"{downloadInfo.FileName}\"");
            return File(downloadInfo.Stream, contentType);
        }

        return NotFound(ApiResponse.Fail("File could not be found or retrieved."));
    }

    [HttpGet("download-direct")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadDirect(
        [FromQuery] string key,
        [FromQuery] string name,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();

        // Enforce user isolation in key check
        string expectedPrefix = $"users/{userId}/";
        if (!key.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var stream = await _storageService.DownloadAsync(key, cancellationToken);
        string contentType = FileHelper.GetContentTypeForExtension(name);
        return File(stream, contentType, name);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFile(Guid id, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        await _fileService.DeleteFileAsync(userId, id, cancellationToken);
        return Ok(ApiResponse.Ok("File deleted successfully."));
    }

    [HttpPut("{id:guid}/rename")]
    [ProducesResponseType(typeof(ApiResponse<FileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RenameFile(Guid id, [FromBody] RenameFileDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var updated = await _fileService.RenameFileAsync(userId, id, request, cancellationToken);
        return Ok(ApiResponse<FileDto>.Ok(updated, "File renamed successfully."));
    }

    [HttpPut("{id:guid}/move")]
    [ProducesResponseType(typeof(ApiResponse<FileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MoveFile(Guid id, [FromBody] MoveFileDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var updated = await _fileService.MoveFileAsync(userId, id, request, cancellationToken);
        return Ok(ApiResponse<FileDto>.Ok(updated, "File moved successfully."));
    }
}
