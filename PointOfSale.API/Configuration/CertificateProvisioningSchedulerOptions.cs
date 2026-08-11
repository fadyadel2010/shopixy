namespace PointOfSale.API.Configuration;

public sealed class CertificateProvisioningSchedulerOptions
{
    public const string SectionName = "CertificateProvisioningScheduler";

    public bool Enabled { get; set; } = true;
    public int ScanIntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 50;
    public int MaxConcurrentOperations { get; set; } = 4;

    public bool Validate(out string error)
    {
        if (ScanIntervalSeconds is < 15 or > 3600)
        {
            error = "CertificateProvisioningScheduler:ScanIntervalSeconds must be between 15 and 3600.";
            return false;
        }

        if (BatchSize is < 1 or > 500 || MaxConcurrentOperations is < 1 or > 32)
        {
            error = "CertificateProvisioningScheduler batch size or concurrency is outside the supported range.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
