namespace PointOfSale.Core.Analytics;

/// <summary>
/// An alert rule that defines when to trigger a business alert.
/// 
/// EXAMPLE RULES:
/// - "Alert when PurchaseCompleted == 0 over 30 minutes" (zero sales)
/// - "Alert when PaymentFailed > 5 over 5 minutes" (payment issue spike)
/// - "Alert when CartAbandonmentRate > 80 over 15 minutes" (high abandonment)
/// 
/// EVALUATION:
/// - Rules are evaluated every 60 seconds by AnalyticsAlertEvaluator
/// - Metrics are read from Redis counters (NO MongoDB)
/// - Alerts are triggered/resolved based on threshold comparison
/// </summary>
public class AnalyticsAlertRule
{
    /// <summary>
    /// Unique identifier for the rule.
    /// </summary>
    public Guid RuleId { get; set; }

    /// <summary>
    /// Tenant that owns this rule.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Human-readable name for the rule.
    /// Example: "Zero Sales Alert"
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of what this rule monitors.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The metric to monitor.
    /// </summary>
    public AnalyticsMetric Metric { get; set; }

    /// <summary>
    /// How to compare the metric value against the threshold.
    /// </summary>
    public AnalyticsComparisonOperator Operator { get; set; }

    /// <summary>
    /// The threshold value to trigger the alert.
    /// </summary>
    public decimal Threshold { get; set; }

    /// <summary>
    /// Time window for metric evaluation (in minutes).
    /// The metric value is computed over this sliding window.
    /// </summary>
    public int WindowMinutes { get; set; }

    /// <summary>
    /// Severity of alerts triggered by this rule.
    /// </summary>
    public AnalyticsAlertSeverity Severity { get; set; } = AnalyticsAlertSeverity.Warning;

    /// <summary>
    /// Whether this rule is currently enabled.
    /// Disabled rules are not evaluated.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Cooldown period after an alert is triggered (in minutes).
    /// Prevents alert spam by not re-triggering for this duration.
    /// </summary>
    public int CooldownMinutes { get; set; } = 15;

    /// <summary>
    /// When this rule was created (UTC).
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this rule was last updated (UTC).
    /// </summary>
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>
    /// Whether the rule is soft-deleted.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Gets the time window as a TimeSpan.
    /// </summary>
    public TimeSpan Window => TimeSpan.FromMinutes(WindowMinutes);

    /// <summary>
    /// Gets the cooldown as a TimeSpan.
    /// </summary>
    public TimeSpan Cooldown => TimeSpan.FromMinutes(CooldownMinutes);

    /// <summary>
    /// Evaluates whether the given value triggers this rule.
    /// </summary>
    public bool Evaluate(decimal actualValue)
    {
        return Operator switch
        {
            AnalyticsComparisonOperator.GreaterThan => actualValue > Threshold,
            AnalyticsComparisonOperator.LessThan => actualValue < Threshold,
            AnalyticsComparisonOperator.Equal => actualValue == Threshold,
            AnalyticsComparisonOperator.GreaterThanOrEqual => actualValue >= Threshold,
            AnalyticsComparisonOperator.LessThanOrEqual => actualValue <= Threshold,
            _ => false
        };
    }

    /// <summary>
    /// Gets a human-readable description of the condition.
    /// </summary>
    public string GetConditionDescription()
    {
        var op = Operator switch
        {
            AnalyticsComparisonOperator.GreaterThan => ">",
            AnalyticsComparisonOperator.LessThan => "<",
            AnalyticsComparisonOperator.Equal => "==",
            AnalyticsComparisonOperator.GreaterThanOrEqual => ">=",
            AnalyticsComparisonOperator.LessThanOrEqual => "<=",
            _ => "?"
        };

        return $"{Metric} {op} {Threshold} over {WindowMinutes} minutes";
    }
}
