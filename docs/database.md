# CloudVault - Database Architecture & Schema

CloudVault uses **Microsoft SQL Server** with **Entity Framework Core**. Actual files are **never** stored inside the database; SQL Server exclusively manages metadata, security tokens, folder structures, and user storage quotas.

---

## 1. Entity-Relationship Diagram

```
+-----------------------------------------------------+
|                       Users                         |
+-----------------------------------------------------+
| PK  Id                  uniqueidentifier            |
|     FullName            nvarchar(100)               |
|     Email               nvarchar(256) (Unique)      |
|     PasswordHash        nvarchar(max)               |
|     StorageLimitBytes   bigint (Default: 1 GB)      |
|     StorageUsedBytes    bigint (Default: 0)         |
|     ProfilePictureS3Key nvarchar(500) (Nullable)    |
|     CreatedAt           datetimeoffset              |
|     UpdatedAt           datetimeoffset              |
+-----------------------------------------------------+
        |                     |                |
        | 1:N                 | 1:N            | 1:N
        v                     v                v
+------------------+  +---------------+  +---------------+
|     Folders      |  |     Files     |  | RefreshTokens |
+------------------+  +---------------+  +---------------+
| PK  Id           |  | PK  Id        |  | PK  Id        |
| FK  UserId       |  | FK  UserId    |  | FK  UserId    |
| FK  ParentFolder |  | FK  FolderId  |  |     Token     |
|     Name         |  |     FileName  |  |     ExpiresAt |
|     IsDeleted    |  |     S3Key     |  |     RevokedAt |
|     CreatedAt    |  |     FileSize  |  +---------------+
+------------------+  |     IsDeleted |
                      +---------------+
```

---

## 2. Table Specifications

### 2.1 `Users`
- Stores authentication credentials, password hashes via ASP.NET Core Identity (`IPasswordHasher<ApplicationUser>`), and cumulative storage limits.
- `StorageLimitBytes`: Configurable per-user quota (defaults to `1_073_741_824` = 1 GB).
- `StorageUsedBytes`: Atomically synchronized byte total of all non-deleted files.

### 2.2 `Folders`
- Maintains directory hierarchies.
- `ParentFolderId`: Self-referencing foreign key to allow nested folders.
- Unique Index: `(UserId, ParentFolderId, Name, IsDeleted)` prevents duplicate folder names within the same directory.
- `IsDeleted`: Soft deletion flag.

### 2.3 `Files`
- Stores metadata for Amazon S3 objects.
- `S3Key`: Generated user-isolated key (e.g. `users/{userId}/files/{fileId}/{fileName}`).
- `FileSize`: Size in bytes used for quota checks and dashboard calculations.
- `ContentType`: Standard MIME type (e.g. `application/pdf`, `image/png`).
- Composite Indexes on `(UserId, IsDeleted, CreatedAt)` and `(UserId, FolderId, IsDeleted)` optimize file manager queries.

### 2.4 `RefreshTokens`
- Manages active JWT refresh tokens.
- `Token`: High-entropy 64-byte cryptographically secure random string.
- `ReplacedByToken`: Records token rotation lineage for anti-tamper detection.

### 2.5 `AuditLogs`
- Records security-sensitive operations (`USER_REGISTERED`, `USER_LOGGED_IN`, `FILE_UPLOADED`, `FILE_DELETED`, `PASSWORD_CHANGED`).
- Tracks client IP addresses and UTC timestamps.

---

## 3. Database Initialization & Migrations

### Applying EF Core Migrations
```bash
dotnet ef database update --project src/CloudVault.Infrastructure/CloudVault.Infrastructure.csproj --startup-project src/CloudVault.API/CloudVault.API.csproj
```

### Standalone SQL Script
A production-ready idempotent SQL DDL script is located at:
`database/init.sql`
