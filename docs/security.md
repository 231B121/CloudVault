# CloudVault - Security Architecture & Threat Model

CloudVault enforces defense-in-depth across the web tier, application logic, database, and cloud storage.

---

## 1. Zero-Trust Multi-Tenant Isolation & IDOR Prevention
An **Insecure Direct Object Reference (IDOR)** occurs when an application exposes a reference to an internal object without verifying authorization.

In CloudVault:
1. **User Scoping in All Queries**: Every repository query strictly filters by `UserId == currentUserId`.
2. **Explicit Verification Before Modification**:
   - Files: When renaming, downloading, deleting, or moving files, the application verifies `f.UserId == currentUserId`. Modifying an ID in the URL for another user's file returns `404 Not Found`.
   - Folders: Destination folders in move operations must belong to the caller. A user cannot move files into another user's folders.
   - Profile Photos: Avatar keys are scoped to `users/{userId}/profile/...`.
3. **Automated IDOR Tests**: Comprehensive unit and integration test suites specifically verify that cross-tenant operations fail safely.

---

## 2. Authentication & Credential Security
- **Passwords**: Never stored in plain text. Hashed using PBKDF2 with HMAC-SHA512 via ASP.NET Core Identity.
- **JWT Authentication**:
  - Signed using HMAC-SHA256 with strong symmetric keys (>= 32 bytes).
  - Short-lived access tokens (default 60 minutes).
- **Refresh Token Rotation**:
  - Whenever a refresh token is exchanged, it is immediately revoked (`RevokedAt = UtcNow`) and replaced by a newly issued token.
  - Stolen or replayed tokens trigger rejection.

---

## 3. Storage Quota Enforcement & Race Condition Protection
- Pre-upload check: `StorageUsedBytes + NewFileSize <= StorageLimitBytes`.
- Upload rejection: Returns `400 Bad Request` with `QuotaExceededException` if storage is full.
- Atomic updates: Quota changes are synchronized in transactional entity updates.
- Deletion cleanup: Deleting a file or folder releases the exact byte count back to the user's available quota.

---

## 4. File Upload Security
- **MIME & Extension Validation**: Whitelist enforcement against `.pdf`, `.docx`, `.xlsx`, `.pptx`, `.txt`, `.jpg`, `.png`, `.gif`, `.webp`, `.zip`.
- **Magic Number (Signature) Verification**: Header bytes are inspected for every uploaded stream to prevent disguised executables (e.g., renaming `virus.exe` to `photo.jpg`).
- **Filename Sanitization**:
  - Removes directory traversal (`../`, `..\\`) and illegal filesystem characters.
  - Generates safe GUID-based S3 keys: `users/{userId}/files/{fileId}/{safeName}`.
- **Size Limits**: Enforced maximum of 100 MB per file.

---

## 5. Amazon S3 Bucket Protection
- **Private Bucket**: Public access block enabled. No objects are readable via public HTTP.
- **Pre-Signed URLs**: Direct downloads use temporary pre-signed URLs valid for 15 minutes.
- **Server-Side Encryption**: `AES256` default encryption on all PutObject requests.
- **Transport Security**: S3 Bucket Policy denies any connection not using TLS (HTTPS).

---

## 6. HTTP Security Headers
The API applies defensive HTTP headers to all responses:
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `X-XSS-Protection: 1; mode=block`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Content-Security-Policy: default-src 'self' 'unsafe-inline' https: data: blob:;`
