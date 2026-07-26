using Microsoft.Extensions.Logging;

namespace PointOfSale.Infrastructure.Observability;

/// <summary>
/// PRODUCTION HARDENING (Phase 12) — EVENT LAG MONITORING
/// 
/// This module provides event observability:
/// - Tracks publish-to-consume latency
/// - Detects slow consumers before they cause queue buildup
/// - Logs structured metrics for aggregation
/// 
/// USAGE: Call TrackEventProcessed at the end of each consumer's Consume method.
/// </summary>
public interface IEventLagTracker
{
    /// <summary>
    /// Records the processing time and lag for an event.
    /// Call this at the END of your consumer's Consume method.
    /// </summary>
    void TrackEventProcessed(
        string eventType,
        DateTime? publishedAtUtc,
        TimeSpan processingDuration,
        string? correlationId = null);
    
    /// <summary>
    /// Records a failed event processing attempt.
    /// Call this when a consumer throws an exception.
    /// </summary>
    void TrackEventFailed(
        string eventType,
        DateTime? publishedAtUtc,
        string errorMessage,
        string? correlationId = null);
}

/// <summary>
/// Production implementation of event lag tracking.
/// Uses structured logging for easy aggregation in monitoring systems.
/// </summary>
public class EventLagTracker : IEventLagTracker
{
    private readonly ILogger<EventLagTracker> _logger;
    
    // Alert thresholds
    private const int LagWarningThresholdSeconds = 5;
    private const int LagCriticalThresholdSeconds = 30;
    private const int ProcessingWarningThresholdMs = 500;
    private const int ProcessingCriticalThresholdMs = 2000;

    public EventLagTracker(ILogger<EventLagTracker> logger)
    {
        _logger = logger;
    }

    public void TrackEventProcessed(
        string eventType,
        DateTime? publishedAtUtc,
        TimeSpan processingDuration,
        string? correlationId = null)
    {
        var lagMs = publishedAtUtc.HasValue 
            ? (DateTime.UtcNow - publishedAtUtc.Value).TotalMilliseconds 
            : 0;
        
        var processingMs = processingDuration.TotalMilliseconds;
        
        // Determine severity and log appropriately
        if (lagMs > LagCriticalThresholdSeconds * 1000)
        {
            _logger.LogError(
                "EVENT LAG CRITICAL | EventType: {EventType} | LagMs: {LagMs} | ProcessingMs: {ProcessingMs} | CorrelationId: {CorrelationId}",
                eventType, lagMs, processingMs, correlationId ?? "N/A");
        }
        else if (lagMs > LagWarningThresholdSeconds * 1000)
        {
            _logger.LogWarning(
                "EVENT LAG WARNING | EventType: {EventType} | LagMs: {LagMs} | ProcessingMs: {ProcessingMs} | CorrelationId: {CorrelationId}",
                eventType, lagMs, processingMs, correlationId ?? "N/A");
        }
        else if (processingMs > ProcessingCriticalThresholdMs)
        {
            _logger.LogError(
                "EVENT PROCESSING SLOW | EventType: {EventType} | LagMs: {LagMs} | ProcessingMs: {ProcessingMs} | CorrelationId: {CorrelationId}",
                eventType, lagMs, processingMs, correlationId ?? "N/A");
        }
        else if (processingMs > ProcessingWarningThresholdMs)
        {
            _logger.LogWarning(
                "EVENT PROCESSING ELEVATED | EventType: {EventType} | LagMs: {LagMs} | ProcessingMs: {ProcessingMs} | CorrelationId: {CorrelationId}",
                eventType, lagMs, processingMs, correlationId ?? "N/A");
        }
        else
        {
            // Normal processing - log at Debug level
            _logger.LogDebug(
                "EVENT PROCESSED | EventType: {EventType} | LagMs: {LagMs} | ProcessingMs: {ProcessingMs} | CorrelationId: {CorrelationId}",
                eventType, lagMs, processingMs, correlationId ?? "N/A");
        }
    }

    public void TrackEventFailed(
        string eventType,
        DateTime? publishedAtUtc,
        string errorMessage,
        string? correlationId = null)
    {
        var lagMs = publishedAtUtc.HasValue 
            ? (DateTime.UtcNow - publishedAtUtc.Value).TotalMilliseconds 
            : 0;
        
        _logger.LogError(
            "EVENT PROCESSING FAILED | EventType: {EventType} | LagMs: {LagMs} | Error: {Error} | CorrelationId: {CorrelationId}",
            eventType, lagMs, errorMessage, correlationId ?? "N/A");
    }
}

/// <summary>
/// Metrics constants for consistent metric naming across the application.
/// Use these with your metrics provider (Prometheus, Application Insights, etc.)
/// </summary>
public static class ObservabilityMetrics
{
    // Stock reservation metrics
    public const string StockReservationAttempts = "stock_reservation_attempts_total";
    public const string StockReservationSuccesses = "stock_reservation_successes_total";
    public const string StockReservationFailures = "stock_reservation_failures_total";
    public const string StockReservationDurationMs = "stock_reservation_duration_ms";
    
    // Event processing metrics
    public const string EventProcessingDurationMs = "event_processing_duration_ms";
    public const string EventLagMs = "event_lag_ms";
    public const string EventProcessingFailures = "event_processing_failures_total";
    
    // Checkout metrics
    public const string CheckoutDurationMs = "checkout_duration_ms";
    public const string CheckoutAttempts = "checkout_attempts_total";
    public const string CheckoutSuccesses = "checkout_successes_total";
    public const string CheckoutFailures = "checkout_failures_total";
    
    // Cache metrics
    public const string CacheHits = "cache_hits_total";
    public const string CacheMisses = "cache_misses_total";
    public const string CacheLatencyMs = "cache_latency_ms";
    
    // MongoDB metrics
    public const string MongoQueryDurationMs = "mongodb_query_duration_ms";
    public const string MongoSlowQueries = "mongodb_slow_queries_total";
    
    // Labels
    public const string LabelTenantId = "tenant_id";
    public const string LabelEventType = "event_type";
    public const string LabelOperationType = "operation_type";
    public const string LabelCacheKeyType = "cache_key_type";
    public const string LabelFailureReason = "failure_reason";
}
