using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using CloudVault.Domain.Entities;

namespace CloudVault.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FileItem> Files => Set<FileItem>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Customize Identity Table Names
        builder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("Users");
            b.Property(u => u.FullName).HasMaxLength(100).IsRequired();
            b.Property(u => u.StorageLimitBytes).HasDefaultValue(1_073_741_824L); // 1 GB
            b.Property(u => u.StorageUsedBytes).HasDefaultValue(0L);
            b.Property(u => u.ProfilePictureS3Key).HasMaxLength(500);

            b.HasIndex(u => u.Email).IsUnique();
        });

        builder.Entity<IdentityRole<Guid>>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");

        // Folder Configuration
        builder.Entity<Folder>(b =>
        {
            b.ToTable("Folders");
            b.HasKey(f => f.Id);
            b.Property(f => f.Name).HasMaxLength(100).IsRequired();

            b.HasOne(f => f.User)
                .WithMany(u => u.Folders)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(f => f.ParentFolder)
                .WithMany(f => f.SubFolders)
                .HasForeignKey(f => f.ParentFolderId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(f => new { f.UserId, f.ParentFolderId, f.Name, f.IsDeleted });
            b.HasIndex(f => f.UserId);
            b.HasIndex(f => f.ParentFolderId);
            b.HasIndex(f => f.IsDeleted);
        });

        // FileItem Configuration
        builder.Entity<FileItem>(b =>
        {
            b.ToTable("Files");
            b.HasKey(f => f.Id);
            b.Property(f => f.FileName).HasMaxLength(255).IsRequired();
            b.Property(f => f.S3Key).HasMaxLength(1000).IsRequired();
            b.Property(f => f.ContentType).HasMaxLength(150).IsRequired();

            b.HasOne(f => f.User)
                .WithMany(u => u.Files)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(f => f.Folder)
                .WithMany(f => f.Files)
                .HasForeignKey(f => f.FolderId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasIndex(f => f.UserId);
            b.HasIndex(f => f.FolderId);
            b.HasIndex(f => f.IsDeleted);
            b.HasIndex(f => f.CreatedAt);
            b.HasIndex(f => f.S3Key);
        });

        // RefreshToken Configuration
        builder.Entity<RefreshToken>(b =>
        {
            b.ToTable("RefreshTokens");
            b.HasKey(r => r.Id);
            b.Property(r => r.Token).HasMaxLength(256).IsRequired();
            b.HasIndex(r => r.Token).IsUnique();

            b.HasOne(r => r.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(r => r.UserId);
        });

        // AuditLog Configuration
        builder.Entity<AuditLog>(b =>
        {
            b.ToTable("AuditLogs");
            b.HasKey(a => a.Id);
            b.Property(a => a.Action).HasMaxLength(100).IsRequired();
            b.Property(a => a.IpAddress).HasMaxLength(50);

            b.HasOne(a => a.User)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasIndex(a => a.UserId);
            b.HasIndex(a => a.CreatedAt);
        });
    }
}
