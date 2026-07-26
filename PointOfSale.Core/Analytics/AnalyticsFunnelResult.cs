namespace PointOfSale.Core.Analytics;

/// <summary>
/// Result of computing a funnel for a specific time window.
/// 
/// Contains the count, conversion rate, and drop-off rate for each step.
/// All metrics are computed from Redis counters only.
/// </summary>
public class AnalyticsFunnelResult
{
    /// <summary>
    /// The funnel key this result is for.
    /// </summary>
    public string FunnelKey { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the funnel.
    /// </summary>
    public string FunnelName { get; set; } = string.Empty;

    /// <summary>
    /// The time window used for this calculation (in minutes).
    /// </summary>
    public int WindowMinutes { get; set; }

    /// <summary>
    /// When this result was computed (UTC).
    /// </summary>
    public DateTime ComputedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The tenant this result is for.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Step-by-step results.
    /// </summary>
    public List<AnalyticsFunnelStepResult> Steps { get; set; } = new();

    /// <summary>
    /// Overall conversion rate (first step to last step).
    /// </summary>
    public decimal OverallConversionRate { get; set; }

    /// <summary>
    /// Overall drop-off rate (100 - overall conversion).
    /// </summary>
    public decimal OverallDropOffRate => 100 - OverallConversionRate;

    /// <summary>
    /// Count at the first step (entry point).
    /// </summary>
    public long EntryCount => Steps.FirstOrDefault()?.Count ?? 0;

    /// <summary>
    /// Count at the last step (completion).
    /// </summary>
    public long CompletionCount => Steps.LastOrDefault()?.Count ?? 0;
}

/// <summary>
/// Result for a single step in the funnel.
/// </summary>
public class AnalyticsFunnelStepResult
{
    /// <summary>
    /// Step order (1-indexed).
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Display name for this step.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The event type for this step.
    /// </summary>
    public AnalyticsEventType EventType { get; set; }

    /// <summary>
    /// Count of users/events at this step.
    /// </summary>
    public long Count { get; set; }

    /// <summary>
    /// Conversion rate from the previous step (percentage).
    /// First step has 100% conversion (entry point).
    /// 
    /// Formula: (this_step / previous_step) * 100
    /// </summary>
    public decimal ConversionFromPrevious { get; set; }

    /// <summary>
    /// Drop-off rate from the previous step (percentage).
    /// First step has 0% drop-off (entry point).
    /// 
    /// Formula: 100 - conversion_rate
    /// </summary>
    public decimal DropOffFromPrevious { get; set; }

    /// <summary>
    /// Conversion rate from the first step (percentage).
    /// 
    /// Formula: (this_step / first_step) * 100
    /// </summary>
    public decimal ConversionFromEntry { get; set; }

    /// <summary>
    /// Absolute drop-off count from the previous step.
    /// </summary>
    public long DropOffCount { get; set; }

    /// <summary>
    /// Top viewed pages (only for PageView step).
    /// </summary>
    public List<TopPageStats>? TopPages { get; set; }

    /// <summary>
    /// Top products (only for ProductView/AddToCart steps).
    /// </summary>
    public List<TopProductStats>? TopProducts { get; set; }
}
