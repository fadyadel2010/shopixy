namespace PointOfSale.Infrastructure.Eventing;

public sealed class StockSyncOutboxOptions
{
    public const string SectionName = "StockSyncOutbox";

    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 100;
    public int MaxBatchesPerPoll { get; set; } = 3;
    public int InterBatchDelayMilliseconds { get; set; } = 100;
    public int ClaimLeaseSeconds { get; set; } = 300;
    public int MaxRetryAttempts { get; set; } = 8;
}
