namespace CloudVault.Domain.Entities;

public class Folder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? ParentFolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;

    // Navigation properties
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Folder? ParentFolder { get; set; }
    public virtual ICollection<Folder> SubFolders { get; set; } = new List<Folder>();
    public virtual ICollection<FileItem> Files { get; set; } = new List<FileItem>();
}
