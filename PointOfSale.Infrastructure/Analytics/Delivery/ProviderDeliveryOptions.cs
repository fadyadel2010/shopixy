using System.ComponentModel.DataAnnotations;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public sealed class ProviderDeliveryOptions
{
    public const string SectionName = "ProviderDelivery";

    [Range(1, 500)]
    public int BatchSize { get; set; } = 100;

    [Range(1, 25)]
    public int MaxBatchesPerPoll { get; set; } = 3;

    [Range(1, 300)]
    public int PollIntervalSeconds { get; set; } = 5;

    [Range(1, 300)]
    public int IdleIntervalSeconds { get; set; } = 15;

    [Range(1, 300)]
    public int ErrorDelaySeconds { get; set; } = 15;

    [Range(0, 60000)]
    public int InterBatchDelayMilliseconds { get; set; } = 100;

    [Range(5, 3600)]
    public int ProcessingLeaseSeconds { get; set; } = 90;

    [Range(1, 32)]
    public int MaxParallelism { get; set; } = 4;

    [Range(3, 50)]
    public int MaxAttempts { get; set; } = 8;

    [Range(1, 1440)]
    public int MaxRetryDelayMinutes { get; set; } = 60;

    [Range(1, 1000)]
    public int BaseRetryDelaySeconds { get; set; } = 30;

    [Range(1, 100)]
    public int JitterPercent { get; set; } = 20;

    [Range(1, 1000)]
    public int CircuitFailureThreshold { get; set; } = 10;

    [Range(1, 1440)]
    public int CircuitOpenMinutes { get; set; } = 5;

    [Range(1, 10080)]
    public int CircuitCounterWindowMinutes { get; set; } = 15;

    [Range(1, 100)]
    public int CircuitHalfOpenMaxProbes { get; set; } = 1;

    [Range(1, 3600)]
    public int CircuitRecoveryWindowSeconds { get; set; } = 60;

    [Range(1, 120)]
    public int HttpTimeoutSeconds { get; set; } = 5;
}
