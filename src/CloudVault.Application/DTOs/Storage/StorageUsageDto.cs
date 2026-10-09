namespace CloudVault.Application.DTOs.Storage;

public class StorageUsageDto
{
    public long StorageLimitBytes { get; set; }
    public long StorageUsedBytes { get; set; }
    public long AvailableBytes => Math.Max(0, StorageLimitBytes - StorageUsedBytes);
    public double UsagePercentage => StorageLimitBytes > 0 
        ? Math.Round(((double)StorageUsedBytes / StorageLimitBytes) * 100, 2) 
        : 0;

    public string FormattedUsed { get; set; } = string.Empty;
    public string FormattedLimit { get; set; } = string.Empty;
    public string FormattedAvailable { get; set; } = string.Empty;
    public string DisplaySummary => $"{FormattedUsed} / {FormattedLimit} ({UsagePercentage}%)";

    public int TotalFiles { get; set; }
    public int TotalFolders { get; set; }
}
