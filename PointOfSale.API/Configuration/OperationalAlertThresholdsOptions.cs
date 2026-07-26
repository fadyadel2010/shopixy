using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.Configuration;

public sealed class OperationalAlertThresholdsOptions
{
    public const string SectionName = "OperationalAlertThresholds";

    [Range(1d, 100d)]
    public double HighFailureRatePercent { get; set; } = 5d;

    [Range(1, 10000)]
    public int WebhookFailureSpikeCount { get; set; } = 20;

    [Range(1, 10000)]
    public int PaymentFailureSpikeCount { get; set; } = 10;

    [Range(1, 10000)]
    public int RateLimitSpikeCount { get; set; } = 50;

    [Range(1, 60)]
    public int EvaluationWindowMinutes { get; set; } = 5;
}
