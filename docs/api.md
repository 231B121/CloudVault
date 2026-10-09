# CloudVault - REST API Reference

All APIs return a standardized JSON envelope:
```json
{
  "success": true,
  "message": "Success",
  "data": { ... },
  "errors": null
}
```

## 1. Authentication APIs (`/api/auth`)

| Method | Endpoint | Description | Auth Required |
|---|---|---|---|
| `POST` | `/api/auth/register` | Registers a new user account with default 1 GB storage quota | No |
| `POST` | `/api/auth/login` | Authenticates with email and password, returning JWT and Refresh Token | No |
| `POST` | `/api/auth/refresh` | Rotates expired access token using a valid refresh token | No |
| `POST` | `/api/auth/logout` | Revokes the active refresh token | Yes (Bearer) |
| `GET` | `/api/auth/me` | Returns current authenticated user information and storage status | Yes (Bearer) |

### Register Request:
```json
{
  "fullName": "Jane Doe",
  "email": "jane@example.com",
  "password": "SecurePassword123!",
  "confirmPassword": "SecurePassword123!"
}
```

---

## 2. User Profile APIs (`/api/profile`)

| Method | Endpoint | Description | Auth Required |
|---|---|---|---|
| `GET` | `/api/profile` | Returns full user profile, quota breakdown, and avatar URL | Yes |
| `PUT` | `/api/profile` | Updates user's full name | Yes |
| `POST` | `/api/profile/photo` | Uploads profile picture directly to S3 (`multipart/form-data`) | Yes |
| `GET` | `/api/profile/photo` | Streams the uploaded profile picture from storage | Yes |
| `PUT` | `/api/profile/password` | Changes user password | Yes |

---

## 3. Folders APIs (`/api/folders`)

| Method | Endpoint | Description | Auth Required |
|---|---|---|---|
| `POST` | `/api/folders` | Creates a new folder (optional `parentFolderId` for nested folders) | Yes |
| `GET` | `/api/folders` | Lists folders under root or specified `parentId` query param | Yes |
| `GET` | `/api/folders/{id}` | Retrieves folder details, breadcrumbs hierarchy, and subfolders | Yes |
| `PUT` | `/api/folders/{id}` | Renames a folder | Yes |
| `DELETE` | `/api/folders/{id}` | Recursively deletes folder, files inside, and restores user quota | Yes |

---

## 4. File APIs (`/api/files`)

| Method | Endpoint | Description | Auth Required |
|---|---|---|---|
| `POST` | `/api/files/upload` | Uploads a single file (`multipart/form-data`) to S3 | Yes |
| `POST` | `/api/files/upload-multiple` | Uploads multiple files in batch with total batch quota check | Yes |
| `GET` | `/api/files` | Queries files with search, folder filtering, file type, pagination | Yes |
| `GET` | `/api/files/{id}` | Retrieves file metadata | Yes |
| `GET` | `/api/files/{id}/download` | Generates a temporary secure S3 pre-signed URL or direct stream | Yes |
| `PUT` | `/api/files/{id}/rename` | Renames a file while preserving extension | Yes |
| `PUT` | `/api/files/{id}/move` | Moves file to target folder (or root if null) with IDOR protection | Yes |
| `DELETE` | `/api/files/{id}` | Soft-deletes file, deletes S3 object, and releases user quota | Yes |

### Query Parameters for `GET /api/files`:
- `search` (string): Search file names.
- `folderId` (Guid, optional): Filter by folder.
- `fileType` (string): Filter by category (`image`, `document`, `spreadsheet`, `presentation`, `archive`).
- `page` (int, default: 1): Page number.
- `pageSize` (int, default: 20): Items per page.
- `sortBy` (string, default: `CreatedAt`): Sort by `FileName`, `FileSize`, or `CreatedAt`.
- `sortDescending` (bool, default: `true`): Sort order.

---

## 5. Storage Quota APIs (`/api/storage`)

| Method | Endpoint | Description | Auth Required |
|---|---|---|---|
| `GET` | `/api/storage/usage` | Returns storage used, limit, available bytes, usage percentage, and formatted human-readable strings | Yes |
