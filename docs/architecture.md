# CloudVault - Software Architecture

## 1. Overview
CloudVault is built on the principles of **Clean Architecture** and **Domain-Driven Design (DDD)**, adhering strictly to **SOLID**, **DRY**, and enterprise security standards.

The application strictly separates domain business rules, application orchestration, infrastructure implementations (EF Core, SQL Server, Amazon S3 SDK), and presentation layers (ASP.NET Core Web API, responsive HTML5/JS web application).

```
+-----------------------------------------------------------------------+
|                            CloudVault.Web                             |
|       (Static SPA: HTML5 / CSS3 / JavaScript / Bootstrap 5)           |
+-----------------------------------------------------------------------+
                                   | HTTP / REST (JWT Bearer)
                                   v
+-----------------------------------------------------------------------+
|                            CloudVault.API                             |
|  - Controllers (Auth, Profile, Folders, Files, Storage)               |
|  - Middlewares (Global Exception Handling, Security Headers, CORS)    |
|  - Swagger / OpenAPI with JWT Authorization                           |
+-----------------------------------------------------------------------+
                                   |
                                   v
+-----------------------------------------------------------------------+
|                        CloudVault.Application                         |
|  - Interfaces (IFileStorageService, IFileService, IAuthService, etc.) |
|  - DTOs & Models (PagedResult, ApiResponse, FileFilterQuery)          |
|  - Business Validation & File Signature / Magic Bytes Engine          |
+-----------------------------------------------------------------------+
                |                                       |
                v                                       v
+-------------------------------+       +-------------------------------+
|      CloudVault.Domain        |       |   CloudVault.Infrastructure   |
| - ApplicationUser, Folder,    |       | - EF Core & SQL Server        |
|   FileItem, RefreshToken      |       | - AWS S3 SDK Client           |
| - Domain Exceptions           |       | - LocalStorage Fallback       |
+-------------------------------+       +-------------------------------+
```

## 2. Layer Responsibilities

### 2.1 Domain Layer (`CloudVault.Domain`)
Contains core business entities, relationships, and domain-level exceptions without external system dependencies.
- **`ApplicationUser`**: Extends `IdentityUser<Guid>`. Maintains user profile, cumulative `StorageLimitBytes`, and real-time `StorageUsedBytes`.
- **`Folder`**: Supports hierarchical nested folder organization, owned by a specific user.
- **`FileItem`**: Stores file metadata only. S3 key, file name, MIME type, size, timestamps, and soft-delete flag.
- **`RefreshToken`**: Stores cryptographically generated tokens, expiration timestamps, and rotation audit trail.
- **`AuditLog`**: Tracks security events, logins, file uploads, renames, and deletions.

### 2.2 Application Layer (`CloudVault.Application`)
Encapsulates all application business logic and use cases:
- Strongly typed option classes (`JwtOptions`, `AwsOptions`, `StorageOptions`).
- DTO contracts separating internal database schemas from external APIs.
- Service interfaces (`IFileStorageService`, `IStorageQuotaService`, `IFileService`, `IFolderService`).
- Validation & Security helpers:
  - `FileHelper`: Inspects file signatures (magic bytes) to ensure file content matches the declared extension.
  - S3 Key generator creating user-isolated, traversal-free keys.
  - `SizeFormatter`: Human-readable size converter.

### 2.3 Infrastructure Layer (`CloudVault.Infrastructure`)
Contains all external communication and persistence details:
- **`ApplicationDbContext`**: EF Core context with entity configurations, foreign keys, cascades, and composite indexes.
- **`S3FileStorageService`**: Production AWS S3 client using `AmazonS3Client` (`AWSSDK.S3`), with AES256 server-side encryption and secure pre-signed URLs.
- **`LocalStorageService`**: Drop-in implementation of `IFileStorageService` for offline development or testing.
- **`StorageQuotaService`**: Atomic check and increment/decrement of user storage usage.
- **`AuthService`**: ASP.NET Core Identity password hashing and JWT issuance with token rotation.

### 2.4 API Layer (`CloudVault.API`)
The HTTP presentation boundary:
- RESTful controllers with HTTP status codes and standard responses.
- `ExceptionHandlingMiddleware`: Translates domain exceptions (`NotFoundException`, `QuotaExceededException`, `ValidationException`) into structured JSON errors without revealing stack traces.
- `SecurityHeadersMiddleware`: Enforces `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, and Content Security Policy.
- Swagger UI with JWT Bearer authentication.
