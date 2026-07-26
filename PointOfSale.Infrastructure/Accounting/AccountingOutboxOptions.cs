using System.ComponentModel.DataAnnotations;

namespace PointOfSale.Infrastructure.Accounting;

public sealed class AccountingOutboxOptions
{
    public const string SectionName = "AccountingOutbox";

    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;

    [Range(1, 100)]
    public int MaxBatchesPerRun { get; set; } = 5;

    [Range(1, 300)]
    public int PollIntervalSeconds { get; set; } = 5;

    [Range(1, 900)]
    public int IdleIntervalSeconds { get; set; } = 30;

    [Range(1, 900)]
    public int ErrorDelaySeconds { get; set; } = 10;

    [Range(0, 60000)]
    public int InterBatchDelayMilliseconds { get; set; } = 250;

    [Range(1, 20)]
    public int MaxRetryAttempts { get; set; } = 5;

    [Range(5, 900)]
    public int ProcessingLeaseSeconds { get; set; } = 60;

    [Range(1, 365)]
    public int ProcessedRetentionDays { get; set; } = 14;

    [Range(1, 365)]
    public int FailedRetentionDays { get; set; } = 30;

    [Range(1, 5000)]
    public int CleanupBatchSize { get; set; } = 250;

    [Range(1, 100)]
    public int CleanupMaxBatchesPerRun { get; set; } = 8;

    /// <summary>
    /// When true, uses EF Core ExecuteDeleteAsync for bulk deletion during cleanup.
    /// This is more efficient for large-scale cleanups but bypasses EF interceptors and cascade behaviors.
    /// When false, loads entities and uses RemoveRange to preserve interceptors and cascades.
    /// Environment variable: AccountingOutbox__UseExecuteDelete
    /// </summary>
    public bool UseExecuteDelete { get; set; } = false;
}
