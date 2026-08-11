namespace PointOfSale.API.Configuration;

public sealed class DomainVerificationSchedulerOptions
{
    public const string SectionName = "DnsVerification:Scheduler";

    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 10;
    public int MaxVerificationWindowMinutes { get; set; } = 5;
    public int BatchSize { get; set; } = 50;
    public int MaxConcurrentVerifications { get; set; } = 10;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);
    public TimeSpan MaxVerificationWindow => TimeSpan.FromMinutes(MaxVerificationWindowMinutes);

    public bool Validate(out string error)
    {
        if (PollIntervalSeconds <= 0)
        {
            error = "DnsVerification:Scheduler:PollIntervalSeconds must be greater than zero.";
            return false;
        }

        if (MaxVerificationWindowMinutes <= 0)
        {
            error = "DnsVerification:Scheduler:MaxVerificationWindowMinutes must be greater than zero.";
            return false;
        }

        if (BatchSize <= 0)
        {
            error = "DnsVerification:Scheduler:BatchSize must be greater than zero.";
            return false;
        }

        if (MaxConcurrentVerifications <= 0)
        {
            error = "DnsVerification:Scheduler:MaxConcurrentVerifications must be greater than zero.";
            return false;
        }

        if (MaxVerificationWindow < PollInterval)
        {
            error = "DnsVerification:Scheduler:MaxVerificationWindowMinutes must be greater than or equal to PollIntervalSeconds.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
