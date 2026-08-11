namespace PointOfSale.API.Configuration;

public sealed class CertificateRenewalSchedulerOptions
{
    public const string SectionName = "CertificateRenewalScheduler";
    public const string ScanIntervalMinutesExpectedRange =
        "1, 2, 3, 4, 5, 6, 10, 12, 15, 20, or 30 minutes; or a whole-hour interval that divides evenly into one day: 60, 120, 180, 240, 360, 480, 720, or 1440 minutes";

    public bool Enabled { get; set; } = true;
    public int ScanIntervalMinutes { get; set; } = 60;
    public int BatchSize { get; set; } = 50;
    public int MaxConcurrentRenewals { get; set; } = 4;
    public int RenewalThresholdDays { get; set; } = 30;
    public int ExpiryWarningThresholdDays { get; set; } = 14;
    public int ExpiryUrgentThresholdDays { get; set; } = 7;
    public int MaxAttempts { get; set; } = 5;
    public int RetryBackoffSeconds { get; set; } = 300;
    public int MaxRetryDelayMinutes { get; set; } = 360;
    public int ProviderRateLimitWindowMinutes { get; set; } = 60;
    public int ProviderAttemptBudget { get; set; } = 5;
    public int StaleOperationTimeoutMinutes { get; set; } = 30;
    public int JitterPercent { get; set; } = 0;

    public TimeSpan ScanInterval => TimeSpan.FromMinutes(ScanIntervalMinutes);
    public TimeSpan RenewalThreshold => TimeSpan.FromDays(RenewalThresholdDays);
    public TimeSpan ExpiryWarningThreshold => TimeSpan.FromDays(ExpiryWarningThresholdDays);
    public TimeSpan ExpiryUrgentThreshold => TimeSpan.FromDays(ExpiryUrgentThresholdDays);
    public TimeSpan RetryBackoff => TimeSpan.FromSeconds(RetryBackoffSeconds);
    public TimeSpan MaxRetryDelay => TimeSpan.FromMinutes(MaxRetryDelayMinutes);
    public TimeSpan ProviderRateLimitWindow => TimeSpan.FromMinutes(ProviderRateLimitWindowMinutes);
    public TimeSpan StaleOperationTimeout => TimeSpan.FromMinutes(StaleOperationTimeoutMinutes);

    public bool Validate(out string error)
    {
        if (!IsSupportedScanIntervalMinutes(ScanIntervalMinutes))
        {
            error =
                "Hangfire recurring job 'certificate-renewal-monitor' has invalid scheduling configuration. " +
                $"Configuration key 'CertificateRenewalScheduler:ScanIntervalMinutes' value '{ScanIntervalMinutes}' is invalid. " +
                $"Expected {ScanIntervalMinutesExpectedRange}.";
            return false;
        }

        if (BatchSize <= 0)
        {
            error = "CertificateRenewalScheduler:BatchSize must be greater than zero.";
            return false;
        }

        if (MaxConcurrentRenewals <= 0)
        {
            error = "CertificateRenewalScheduler:MaxConcurrentRenewals must be greater than zero.";
            return false;
        }

        if (RenewalThresholdDays <= 0 || ExpiryWarningThresholdDays <= 0 || ExpiryUrgentThresholdDays <= 0)
        {
            error = "CertificateRenewalScheduler expiry thresholds must be greater than zero.";
            return false;
        }

        if (ExpiryUrgentThresholdDays > ExpiryWarningThresholdDays)
        {
            error = "CertificateRenewalScheduler:ExpiryUrgentThresholdDays must be less than or equal to ExpiryWarningThresholdDays.";
            return false;
        }

        if (MaxAttempts <= 0 || ProviderAttemptBudget <= 0)
        {
            error = "CertificateRenewalScheduler attempt limits must be greater than zero.";
            return false;
        }

        if (RetryBackoffSeconds <= 0 || MaxRetryDelayMinutes <= 0 || ProviderRateLimitWindowMinutes <= 0 || StaleOperationTimeoutMinutes <= 0)
        {
            error = "CertificateRenewalScheduler retry, rate-limit, and stale-operation windows must be greater than zero.";
            return false;
        }

        if (JitterPercent is < 0 or > 50)
        {
            error = "CertificateRenewalScheduler:JitterPercent must be between 0 and 50.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool IsSupportedScanIntervalMinutes(int scanIntervalMinutes)
    {
        if (scanIntervalMinutes <= 0)
        {
            return false;
        }

        if (scanIntervalMinutes < 60)
        {
            return 60 % scanIntervalMinutes == 0;
        }

        if (scanIntervalMinutes == 1440)
        {
            return true;
        }

        if (scanIntervalMinutes % 60 != 0)
        {
            return false;
        }

        var hours = scanIntervalMinutes / 60;
        return hours is >= 1 and < 24 && 24 % hours == 0;
    }
}
