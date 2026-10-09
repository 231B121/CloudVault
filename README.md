# CloudVault ☁️
> Production-grade personal cloud file storage web application built with modern **ASP.NET Core (.NET 10)**, **Amazon S3**, **Microsoft SQL Server**, and a modern responsive **HTML5/JS** frontend.

[![.NET Build](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)
[![AWS S3](https://img.shields.io/badge/Storage-Amazon%20S3-orange.svg)](https://aws.amazon.com/s3/)
[![Database](https://img.shields.io/badge/Database-SQL%20Server-red.svg)](https://www.microsoft.com/sql-server)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

---

## 📖 Table of Contents
1. [Project Overview](#project-overview)
2. [Features](#features)
3. [Technology Stack](#technology-stack)
4. [Architecture & Folder Structure](#architecture--folder-structure)
5. [Database Schema](#database-schema)
6. [API List](#api-list)
7. [Local Development Setup](#local-development-setup)
8. [Docker Compose Setup](#docker-compose-setup)
9. [AWS Production Deployment](#aws-production-deployment)
10. [Security & IDOR Protections](#security--idor-protections)
11. [Automated Testing](#automated-testing)
12. [Troubleshooting](#troubleshooting)

---

## 🌟 Project Overview
**CloudVault** provides secure, user-isolated cloud storage where users can store files, photos, and folders permanently on **Amazon S3**, with all metadata and relationships stored in **Microsoft SQL Server**.

Every user receives a configurable storage quota (default: 1 GB). Uploads are verified before reaching S3, preventing quota overages and malicious executable uploads through magic number signature verification.

---

## ✨ Features
- **User Authentication**: Secure registration, login, logout, password hashing via ASP.NET Core Identity, JWT Bearer tokens, and rotating refresh tokens.
- **Profile Management**: View account stats, update name, change password, and upload user avatars directly to S3.
- **Hierarchical Folders**: Create, rename, browse nested folders, move files between folders, and delete folders with automatic cascading quota cleanup.
- **File Management**: Single & multi-file upload, drag-and-drop file picker with real-time upload progress, file rename, move, and soft delete.
- **Secure File Download**: Secure temporary Amazon S3 pre-signed URLs (15-minute expiration) or direct authenticated streaming.
- **Storage Quota Enforcement**: Atomic quota checks (`StorageUsedBytes + FileSize <= StorageLimitBytes`), live storage gauges, and instant quota restoration upon file deletion.
- **Search & Pagination**: Instant file name search, folder filtering, category filters (Documents, Images, Spreadsheets, Presentations, Archives), and configurable pagination.
- **Zero-Trust Multi-Tenant Isolation**: Rigorous row-level ownership checks preventing Insecure Direct Object References (IDOR).
- **Interactive UI**: Responsive desktop, tablet, and mobile interface with Bootstrap 5, custom SaaS CSS styles, and Lucide icons.

---

## 🛠️ Technology Stack
- **Backend Framework**: ASP.NET Core Web API (.NET 10)
- **Language**: C#
- **Database**: Microsoft SQL Server with Entity Framework Core 10
- **Object Storage**: Amazon Web Services S3 SDK (`AWSSDK.S3`)
- **Authentication**: ASP.NET Core Identity + JWT Bearer + Refresh Token Rotation
- **Documentation**: Swagger / OpenAPI with JWT Authorization support
- **Testing**: xUnit, Moq, FluentAssertions, `WebApplicationFactory<Program>`
- **Frontend**: HTML5, CSS3, JavaScript (Fetch API), Bootstrap 5

---

## 📂 Architecture & Folder Structure

```
CloudVault/
│
├── CloudVault.sln
│
├── src/
│   ├── CloudVault.Domain/               # Entities, Enums, Domain Exceptions
│   │   ├── Entities/                    # ApplicationUser, Folder, FileItem, RefreshToken, AuditLog
│   │   └── Exceptions/                  # DomainExceptions (NotFound, QuotaExceeded, Validation)
│   │
│   ├── CloudVault.Application/          # Interfaces, DTOs, Business Rules
│   │   ├── Common/Helpers/              # FileHelper (Magic bytes validation, S3 key builder)
│   │   ├── Common/Interfaces/           # IFileStorageService, IFileService, IAuthService
│   │   ├── Common/Options/              # JwtOptions, AwsOptions, StorageOptions
│   │   └── DTOs/                        # AuthDtos, FileDtos, FolderDtos, ProfileDtos, StorageDtos
│   │
│   ├── CloudVault.Infrastructure/       # Persistence, AWS S3 SDK, Services
│   │   ├── Persistence/                 # ApplicationDbContext, Migrations, ApplicationDbContextFactory
│   │   ├── Services/                    # AuthService, FileService, FolderService, StorageQuotaService
│   │   └── Storage/                     # S3FileStorageService, LocalStorageService
│   │
│   ├── CloudVault.API/                  # REST Controllers, Middlewares, Configuration
│   │   ├── Controllers/                 # Auth, Profile, Folders, Files, Storage
│   │   ├── Middleware/                  # ExceptionHandlingMiddleware, SecurityHeadersMiddleware
│   │   └── wwwroot/                     # Production Web UI (Landing, Login, Dashboard, File Manager)
│   │
│   └── CloudVault.Web/                  # Standalone Web Client Project
│
├── tests/
│   ├── CloudVault.UnitTests/            # 24 Unit Tests (Auth, Quota, File Validation, IDOR)
│   └── CloudVault.IntegrationTests/     # End-to-end API Integration Tests (Auth, Files, Quotas)
│
├── database/
│   └── init.sql                         # Standalone SQL Server migration script
│
├── deployment/
│   ├── Dockerfile                       # Multi-stage production container build
│   ├── docker-compose.yml               # Local SQL Server + LocalStack S3 + API
│   └── aws/                             # CloudFormation template, S3 bucket policy, IAM policy
│
├── docs/                                # Detailed technical documentation
│   ├── architecture.md
│   ├── api.md
│   ├── aws-deployment.md
│   ├── database.md
│   └── security.md
│
├── .env.example
└── README.md
```

---

## 🗄️ Database Schema

| Table | Description | Primary Key | Key Columns / Indexes |
|---|---|---|---|
| `Users` | User profiles, passwords, quotas | `Id` (GUID) | `Email` (Unique), `StorageLimitBytes`, `StorageUsedBytes` |
| `Folders` | Directory hierarchy | `Id` (GUID) | `UserId`, `ParentFolderId`, `Name`, `IsDeleted` |
| `Files` | File metadata & S3 object keys | `Id` (GUID) | `UserId`, `FolderId`, `FileName`, `S3Key`, `FileSize`, `IsDeleted` |
| `RefreshTokens` | Rotating authentication tokens | `Id` (GUID) | `UserId`, `Token` (Unique), `ExpiresAt`, `RevokedAt` |
| `AuditLogs` | Security event audit trail | `Id` (GUID) | `UserId`, `Action`, `IpAddress`, `CreatedAt` |

---

## 🚀 Local Development Setup

### 1. Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Docker](https://www.docker.com/) (optional, for local SQL Server)

### 2. Run with 1 Single Command
To run immediately using the built-in local development configuration (In-Memory Database + Local File Storage):
```bash
dotnet run --project src/CloudVault.API/CloudVault.API.csproj
```
Visit the application in your browser:
- **Web App**: `http://localhost:5000` (or `https://localhost:5001`)
- **Interactive Swagger Docs**: `http://localhost:5000/swagger`

---

## 🐳 Docker Compose Setup
To run with real **Microsoft SQL Server 2022** and **LocalStack AWS S3 emulation**:
```bash
cd deployment
docker compose up -d
```
This spins up:
- **SQL Server 2022**: `localhost:1433`
- **LocalStack (AWS S3)**: `localhost:4566`
- **CloudVault Web & API**: `http://localhost:5000`

---

## ☁️ AWS Production Deployment

### 1. Create S3 Storage Bucket
```bash
aws s3api create-bucket --bucket your-cloudvault-bucket --region us-east-1
aws s3api put-public-access-block --bucket your-cloudvault-bucket --public-access-block-configuration "BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true"
```

### 2. Automated CloudFormation Stack
Deploy all infrastructure (VPC, ECS Fargate, RDS SQL Server, S3, ALB, and CloudWatch) in one command:
```bash
aws cloudformation deploy \
    --template-file deployment/aws/cloudformation-template.yaml \
    --stack-name cloudvault-production \
    --parameter-overrides \
        EnvironmentName=prod \
        DBMasterUsername=cloudvault_admin \
        DBMasterPassword="YourStrongPassword123!" \
    --capabilities CAPABILITY_IAM CAPABILITY_NAMED_IAM
```

See [docs/aws-deployment.md](docs/aws-deployment.md) for full deployment details.

---

## 🔒 Security & IDOR Protections
- **Zero-Trust Multi-Tenancy**: All file and folder operations strictly enforce `UserId == currentUserId`. Modifying an ID in the URL to point to another user's file returns `404 Not Found`.
- **Content Inspection (Magic Bytes)**: Validates file headers to reject disguised executables.
- **S3 Isolation**: Files are stored under safe, user-scoped keys: `users/{userId}/files/{fileId}/{fileName}`.
- **Temporary Pre-Signed URLs**: Direct S3 downloads are restricted to 15-minute expiring URLs.

---

## 🧪 Automated Testing
Run the complete automated test suite:
```bash
dotnet test CloudVault.sln
```

### Test Coverage Highlights:
- **Authentication**: Registration, duplicate rejection, login, invalid credentials, refresh token rotation, unauthorized access.
- **Storage Quota**: Pre-upload quota checks, rejection when exceeding quota, exact byte release upon deletion.
- **File Management**: Extension whitelist, magic byte detection, file rename, search, and pagination.
- **Security & IDOR**: Verifies that User A cannot read, download, rename, move, or delete User B's files and folders.

---

## 📄 License
This project is licensed under the MIT License.
