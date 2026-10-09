using Microsoft.AspNetCore.Identity;

namespace CloudVault.Domain.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
    public long StorageLimitBytes { get; set; } = 1_073_741_824; // Default 1 GB
    public long StorageUsedBytes { get; set; } = 0;
    public string? ProfilePictureS3Key { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigations
    public virtual ICollection<Folder> Folders { get; set; } = new List<Folder>();
    public virtual ICollection<FileItem> Files { get; set; } = new List<FileItem>();
    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
