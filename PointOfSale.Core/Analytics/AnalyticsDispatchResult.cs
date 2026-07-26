namespace PointOfSale.Core.Analytics;

/// <summary>
/// Result of dispatching an analytics event to a specific provider.
/// 
/// ARCHITECTURAL RULE:
/// - Dispatch failures NEVER affect event ingestion
/// - Results are for observability only
/// - Latency is tracked for performance monitoring
/// </summary>
public class AnalyticsDispatchResult
{
    /// <summary>
    /// The provider that received the event.
    /// </summary>
    public AnalyticsProvider Provider { get; set; }

    /// <summary>
    /// Whether the dispatch was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if dispatch failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Time taken to dispatch (milliseconds).
    /// </summary>
    public long LatencyMs { get; set; }

    /// <summary>
    /// When the dispatch was attempted (UTC).
    /// </summary>
    public DateTime DispatchedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// HTTP status code (for external providers).
    /// </summary>
    public int? HttpStatusCode { get; set; }

    /// <summary>
    /// Creates a successful dispatch result.
    /// </summary>
    public static AnalyticsDispatchResult Ok(AnalyticsProvider provider, long latencyMs, int? httpStatusCode = null)
    {
        return new AnalyticsDispatchResult
        {
            Provider = provider,
            Success = true,
            LatencyMs = latencyMs,
            HttpStatusCode = httpStatusCode
        };
    }

    /// <summary>
    /// Creates a failed dispatch result.
    /// </summary>
    public static AnalyticsDispatchResult Fail(AnalyticsProvider provider, string errorMessage, long latencyMs = 0, int? httpStatusCode = null)
    {
        return new AnalyticsDispatchResult
        {
            Provider = provider,
            Success = false,
            ErrorMessage = errorMessage,
            LatencyMs = latencyMs,
            HttpStatusCode = httpStatusCode
        };
    }

    /// <summary>
    /// Creates a skipped dispatch result (provider disabled).
    /// </summary>
    public static AnalyticsDispatchResult Skipped(AnalyticsProvider provider, string reason)
    {
        return new AnalyticsDispatchResult
        {
            Provider = provider,
            Success = true,
            ErrorMessage = reason,
            LatencyMs = 0
        };
    }
}
