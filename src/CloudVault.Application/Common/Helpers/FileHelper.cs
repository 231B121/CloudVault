using System.Text.RegularExpressions;
using CloudVault.Domain.Exceptions;

namespace CloudVault.Application.Common.Helpers;

public static class SizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";

        int digitGroups = (int)(Math.Log10(bytes) / Math.Log10(1024));
        if (digitGroups >= Units.Length) digitGroups = Units.Length - 1;

        double size = bytes / Math.Pow(1024, digitGroups);
        return $"{size:0.##} {Units[digitGroups]}";
    }
}

public static class FileHelper
{
    private static readonly Dictionary<string, List<byte[]>> FileSignatures = new()
    {
        { ".jpeg", new List<byte[]> { new byte[] { 0xFF, 0xD8, 0xFF } } },
        { ".jpg", new List<byte[]> { new byte[] { 0xFF, 0xD8, 0xFF } } },
        { ".png", new List<byte[]> { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } } },
        { ".gif", new List<byte[]> { new byte[] { 0x47, 0x49, 0x46, 0x38 } } }, // GIF8
        { ".webp", new List<byte[]> { new byte[] { 0x52, 0x49, 0x46, 0x46 } } }, // RIFF
        { ".pdf", new List<byte[]> { new byte[] { 0x25, 0x50, 0x44, 0x46 } } }, // %PDF
        { ".zip", new List<byte[]> { new byte[] { 0x50, 0x4B, 0x03, 0x04 }, new byte[] { 0x50, 0x4B, 0x05, 0x06 } } }, // PK..
        { ".docx", new List<byte[]> { new byte[] { 0x50, 0x4B, 0x03, 0x04 } } }, // DOCX (Zip container)
        { ".xlsx", new List<byte[]> { new byte[] { 0x50, 0x4B, 0x03, 0x04 } } }, // XLSX (Zip container)
        { ".pptx", new List<byte[]> { new byte[] { 0x50, 0x4B, 0x03, 0x04 } } }, // PPTX (Zip container)
        { ".doc", new List<byte[]> { new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 } } }, // OLE CF
        { ".xls", new List<byte[]> { new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 } } },
        { ".ppt", new List<byte[]> { new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 } } },
    };

    public static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ValidationException("FileName", "File name cannot be empty.");
        }

        // Strip path traversal attempts and get pure file name
        string baseName = Path.GetFileName(fileName);

        // Remove illegal characters
        string sanitized = Regex.Replace(baseName, @"[^\w\-. ]", "_");
        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "unnamed_file";
        }

        return sanitized;
    }

    public static string BuildS3Key(Guid userId, Guid fileId, string fileName)
    {
        string safeFileName = SanitizeFileName(fileName);
        return $"users/{userId}/files/{fileId}/{safeFileName}";
    }

    public static string BuildProfilePhotoS3Key(Guid userId, string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        return $"users/{userId}/profile/{Guid.NewGuid()}{extension}";
    }

    public static bool IsImageExtension(string extension)
    {
        var ext = extension.ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp";
    }

    public static string GetContentTypeForExtension(string fileName, string? defaultContentType = null)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".txt" => "text/plain; charset=utf-8",
            ".json" => "application/json",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".zip" => "application/zip",
            _ => string.IsNullOrWhiteSpace(defaultContentType) || defaultContentType == "application/octet-stream"
                ? "application/octet-stream"
                : defaultContentType
        };
    }

    public static void ValidateFile(
        string fileName,
        long fileSize,
        string contentType,
        Stream stream,
        string[] allowedExtensions,
        long maxFileSizeBytes)
    {
        if (fileSize <= 0)
        {
            throw new ValidationException("File", "Empty files cannot be uploaded.");
        }

        if (fileSize > maxFileSizeBytes)
        {
            throw new ValidationException("File", $"File size exceeds the maximum allowed limit of {SizeFormatter.FormatBytes(maxFileSizeBytes)}.");
        }

        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException("File", $"File extension '{extension}' is not permitted. Allowed: {string.Join(", ", allowedExtensions)}");
        }

        // Verify header magic bytes if signature is defined
        if (FileSignatures.TryGetValue(extension, out var signatures))
        {
            byte[] header = new byte[8];
            long originalPosition = 0;
            if (stream.CanSeek)
            {
                originalPosition = stream.Position;
                stream.Position = 0;
            }

            int bytesRead = stream.Read(header, 0, header.Length);

            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }

            if (bytesRead > 0)
            {
                bool matches = signatures.Any(sig =>
                    bytesRead >= sig.Length && header.Take(sig.Length).SequenceEqual(sig));

                if (!matches)
                {
                    throw new ValidationException("File", $"File content does not match the '{extension}' format specification.");
                }
            }
        }
    }
}
