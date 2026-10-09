using System.ComponentModel.DataAnnotations;

namespace CloudVault.Application.DTOs.Folders;

public class FolderDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ParentFolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SubFolderCount { get; set; }
    public int FileCount { get; set; }
    public long TotalSizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class CreateFolderDto
{
    [Required(ErrorMessage = "Folder name is required.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Folder name must be between 1 and 100 characters.")]
    [RegularExpression(@"^[^\\/:*?""<>|]+$", ErrorMessage = "Folder name contains invalid characters.")]
    public string Name { get; set; } = string.Empty;

    public Guid? ParentFolderId { get; set; }
}

public class UpdateFolderDto
{
    [Required(ErrorMessage = "Folder name is required.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Folder name must be between 1 and 100 characters.")]
    [RegularExpression(@"^[^\\/:*?""<>|]+$", ErrorMessage = "Folder name contains invalid characters.")]
    public string Name { get; set; } = string.Empty;
}

public class FolderBreadcrumbDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class FolderDetailDto : FolderDto
{
    public List<FolderBreadcrumbDto> Breadcrumbs { get; set; } = new();
    public List<FolderDto> SubFolders { get; set; } = new();
}
