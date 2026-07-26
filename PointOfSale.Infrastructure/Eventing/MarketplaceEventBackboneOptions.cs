namespace PointOfSale.Infrastructure.Eventing;

public sealed class MarketplaceEventBackboneOptions
{
    public const string SectionName = "MarketplaceEventBackbone";

    public int PublishedOutboxRetentionDays { get; set; } = 14;
    public int FailedOutboxRetentionDays { get; set; } = 30;
    public int ProcessedEventsRetentionDays { get; set; } = 30;
    public int CleanupIntervalMinutes { get; set; } = 360;

    /// <summary>
    /// Poll interval for marketplace projection outbox worker.
    /// </summary>
    public int OutboxPollIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum marketplace event outbox rows claimed per processor batch.
    /// </summary>
    public int EventOutboxBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum marketplace projection outbox rows claimed per processor batch.
    /// </summary>
    public int ProjectionOutboxBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum batches drained in one worker tick before yielding back to the host.
    /// </summary>
    public int MaxBatchesPerPoll { get; set; } = 3;

    /// <summary>
    /// Optional pause between drained batches to avoid saturating shared dependencies.
    /// </summary>
    public int InterBatchDelayMilliseconds { get; set; } = 100;

    /// <summary>
    /// Lease duration used while a worker publishes an outbox message.
    /// Must be tuned above normal publish latency to avoid duplicate publishes.
    /// </summary>
    public int ClaimLeaseSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum retry attempts before a projection outbox message is moved
    /// to terminal failed state and handed to recovery/compensation flow.
    /// </summary>
    public int ProjectionMaxRetryAttempts { get; set; } = 8;
}
