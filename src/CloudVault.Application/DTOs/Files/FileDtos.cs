using System.ComponentModel.DataAnnotations;

namespace CloudVault.Application.DTOs.Files;

public class FileDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? FolderId { get; set; }
    public string? FolderName { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FormattedSize { get; set; } = string.Empty;
    public bool IsImage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class FileDownloadDto
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public Stream? Stream { get; set; }
    public string? DownloadUrl { get; set; }
    public long FileSize { get; set; }
}

public class FileUploadResultDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class RenameFileDto
{
    [Required(ErrorMessage = "New file name is required.")]
    [StringLength(255, MinimumLength = 1, ErrorMessage = "File name must be between 1 and 255 characters.")]
    [RegularExpression(@"^[^\\/:*?""<>|]+$", ErrorMessage = "File name contains invalid characters.")]
    public string NewFileName { get; set; } = string.Empty;
}

public class MoveFileDto
{
    public Guid? TargetFolderId { get; set; }
}

public class FileFilterQuery
{
    public string? Search { get; set; }
    public Guid? FolderId { get; set; }
    public bool IncludeSubfolders { get; set; } = false;
    public string? FileType { get; set; } // "image", "document", "archive", "spreadsheet", etc.
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; } = "CreatedAt"; // "FileName", "FileSize", "CreatedAt"
    public bool SortDescending { get; set; } = true;
}
