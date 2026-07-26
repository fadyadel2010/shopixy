namespace PointOfSale.Core.Analytics;

/// <summary>
/// A triggered alert instance.
/// 
/// LIFECYCLE:
/// 1. Alert is TRIGGERED when rule condition is met
/// 2. Alert remains ACTIVE while condition persists
/// 3. Alert is RESOLVED when condition clears
/// 
/// STORAGE:
/// - Active alerts: Redis (for fast admin API access)
/// - All alerts: MongoDB (audit trail)
/// </summary>
public class AnalyticsAlert
{
    /// <summary>
    /// Unique identifier for this alert instance.
    /// </summary>
    public Guid AlertId { get; set; }

    /// <summary>
    /// Tenant that owns this alert.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The rule that triggered this alert.
    /// </summary>
    public Guid RuleId { get; set; }

    /// <summary>
    /// Name of the rule (denormalized for display).
    /// </summary>
    public string RuleName { get; set; } = string.Empty;

    /// <summary>
    /// The metric that triggered this alert.
    /// </summary>
    public AnalyticsMetric Metric { get; set; }

    /// <summary>
    /// The actual value when the alert was triggered.
    /// </summary>
    public decimal ActualValue { get; set; }

    /// <summary>
    /// The threshold that was crossed.
    /// </summary>
    public decimal Threshold { get; set; }

    /// <summary>
    /// The comparison operator used.
    /// </summary>
    public AnalyticsComparisonOperator Operator { get; set; }

    /// <summary>
    /// Severity of this alert.
    /// </summary>
    public AnalyticsAlertSeverity Severity { get; set; }

    /// <summary>
    /// Human-readable description of the condition that triggered.
    /// </summary>
    public string ConditionDescription { get; set; } = string.Empty;

    /// <summary>
    /// When the alert was triggered (UTC).
    /// </summary>
    public DateTime TriggeredAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the alert was resolved (UTC).
    /// Null if still active.
    /// </summary>
    public DateTime? ResolvedAtUtc { get; set; }

    /// <summary>
    /// Whether this alert has been resolved.
    /// </summary>
    public bool IsResolved { get; set; }

    /// <summary>
    /// The value when the alert was resolved.
    /// </summary>
    public decimal? ResolvedValue { get; set; }

    /// <summary>
    /// Duration the alert was active (if resolved).
    /// </summary>
    public TimeSpan? Duration => ResolvedAtUtc.HasValue 
        ? ResolvedAtUtc.Value - TriggeredAtUtc 
        : null;

    /// <summary>
    /// Whether this alert is currently active (not resolved).
    /// </summary>
    public bool IsActive => !IsResolved;

    /// <summary>
    /// Creates an alert from a triggered rule.
    /// </summary>
    public static AnalyticsAlert FromRule(AnalyticsAlertRule rule, decimal actualValue)
    {
        return new AnalyticsAlert
        {
            AlertId = Guid.NewGuid(),
            TenantId = rule.TenantId,
            RuleId = rule.RuleId,
            RuleName = rule.Name,
            Metric = rule.Metric,
            ActualValue = actualValue,
            Threshold = rule.Threshold,
            Operator = rule.Operator,
            Severity = rule.Severity,
            ConditionDescription = rule.GetConditionDescription(),
            TriggeredAtUtc = DateTime.UtcNow,
            IsResolved = false
        };
    }

    /// <summary>
    /// Resolves this alert.
    /// </summary>
    public void Resolve(decimal currentValue)
    {
        IsResolved = true;
        ResolvedAtUtc = DateTime.UtcNow;
        ResolvedValue = currentValue;
    }
}
