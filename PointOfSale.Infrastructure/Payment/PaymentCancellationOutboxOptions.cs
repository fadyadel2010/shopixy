using System.ComponentModel.DataAnnotations;

namespace PointOfSale.Infrastructure.Payment;

public sealed class PaymentCancellationOutboxOptions
{
    public const string SectionName = "PaymentCancellationOutbox";

    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;

    [Range(1, 25)]
    public int MaxBatchesPerPoll { get; set; } = 3;

    [Range(1, 300)]
    public int PollIntervalSeconds { get; set; } = 10;

    [Range(1, 300)]
    public int IdleIntervalSeconds { get; set; } = 30;

    [Range(1, 300)]
    public int ErrorDelaySeconds { get; set; } = 60;

    [Range(0, 60000)]
    public int InterBatchDelayMilliseconds { get; set; } = 100;

    [Range(5, 3600)]
    public int ProcessingLeaseSeconds { get; set; } = 120;

    [Range(3, 50)]
    public int MaxRetryAttempts { get; set; } = 10;

    [Range(1, 1440)]
    public int MaxRetryDelayMinutes { get; set; } = 512;
}
