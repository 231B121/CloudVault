namespace CloudVault.Application.Common.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = "CloudVault_Super_Secret_Key_Minimum_32_Characters_Long_Required_For_HmacSha256!";
    public string Issuer { get; set; } = "CloudVault";
    public string Audience { get; set; } = "CloudVaultUsers";
    public int ExpiryMinutes { get; set; } = 60;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}

public class AwsOptions
{
    public const string SectionName = "AWS";

    public string Region { get; set; } = "us-east-1";
    public string? ServiceUrl { get; set; }
    public bool ForcePathStyle { get; set; } = false;
    public bool UseIamRole { get; set; } = true;
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
}

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; set; } = "S3"; // "S3" or "Local"
    public string S3BucketName { get; set; } = "cloudvault-storage";
    public string LocalStoragePath { get; set; } = "app_data/storage";
    public long DefaultQuotaBytes { get; set; } = 1_073_741_824; // 1 GB
    public long MaxFileSizeBytes { get; set; } = 104_857_600; // 100 MB per file
    public string[] AllowedExtensions { get; set; } = new[]
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx",
        ".ppt", ".pptx", ".txt", ".jpg", ".jpeg",
        ".png", ".gif", ".webp", ".zip"
    };
}
