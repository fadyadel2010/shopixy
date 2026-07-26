using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Infrastructure.Analytics.Dispatchers;

/// <summary>
/// Dispatcher for the POS Admin Realtime Dashboard.
/// 
/// This dispatcher is INTERNAL - it feeds the admin dashboard, not an external service.
/// It uses Redis hot store which is already being written to by AnalyticsEventConsumer.
/// This dispatcher's main job is to track that events are available for realtime viewing.
/// 
/// ARCHITECTURE:
/// - Reads from/updates Redis realtime metrics
/// - Always enabled (no external configuration needed)
/// - Highest priority (runs first)
/// - Never fails (Redis failures are best-effort)
/// </summary>
public class AdminRealtimeDispatcher : IAnalyticsDispatcher
{
    private readonly IAnalyticsHotStoreService _hotStore;
    private readonly ILogger<AdminRealtimeDispatcher> _logger;

    public AnalyticsProvider Provider => AnalyticsProvider.AdminRealtime;
    public int Priority => 0; // Highest priority - run first

    public AdminRealtimeDispatcher(
        IAnalyticsHotStoreService hotStore,
        ILogger<AdminRealtimeDispatcher> logger)
    {
        _hotStore = hotStore;
        _logger = logger;
    }

    public bool IsEnabled(TenantAnalyticsSettings? settings)
    {
        // Admin realtime is always enabled - it's internal
        return true;
    }

    public async Task<AnalyticsDispatchResult> DispatchAsync(
        AnalyticsEvent analyticsEvent,
        TenantAnalyticsSettings? settings,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Increment realtime dashboard counters
            // These are specific to the dashboard view, separate from storage counters
            var now = DateTime.UtcNow;

            // 5-minute window counter for "Active Now" metrics
            var fiveMinWindow = now.ToString("yyyyMMddHHmm");
            fiveMinWindow = fiveMinWindow[..^1] + "0"; // Round to 5-minute buckets

            await _hotStore.IncrementCounterAsync(
                analyticsEvent.TenantId,
                $"realtime:{analyticsEvent.Type.ToString().ToLowerInvariant()}",
                now,
                1,
                cancellationToken);

            // Track active sessions (unique sessions in last 5 minutes)
            if (!string.IsNullOrEmpty(analyticsEvent.Actor?.SessionId))
            {
                await IncrementActiveSessionsAsync(
                    analyticsEvent.TenantId,
                    analyticsEvent.Actor.SessionId,
                    cancellationToken);
            }

            stopwatch.Stop();

            _logger.LogDebug(
                "[Analytics Dispatch] AdminRealtime | TenantId: {TenantId} | EventId: {EventId} | Type: {Type} | Latency: {Latency}ms",
                analyticsEvent.TenantId,
                analyticsEvent.EventId,
                analyticsEvent.Type,
                stopwatch.ElapsedMilliseconds);

            return AnalyticsDispatchResult.Ok(Provider, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogWarning(ex,
                "[Analytics Dispatch] AdminRealtime FAILED (non-critical) | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId,
                analyticsEvent.EventId);

            return AnalyticsDispatchResult.Fail(Provider, ex.Message, stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task IncrementActiveSessionsAsync(
        Guid tenantId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Track session activity in a separate counter
            // This allows dashboard to count unique active sessions
            await _hotStore.IncrementCounterAsync(
                tenantId,
                $"session:active:{sessionId[..Math.Min(8, sessionId.Length)]}", // Truncate for privacy
                DateTime.UtcNow,
                1,
                cancellationToken);
        }
        catch
        {
            // Silently ignore - this is best-effort
        }
    }
}
