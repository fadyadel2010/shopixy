using PointOfSale.Core.Analytics;

namespace PointOfSale.Infrastructure.Analytics.Dispatchers;

/// <summary>
/// Interface for analytics event dispatchers.
/// 
/// ARCHITECTURAL RULES:
/// - Dispatchers are STATELESS
/// - Dispatchers NEVER mutate events
/// - Dispatchers NEVER block ingestion
/// - Dispatch failures are logged but don't throw
/// - Each dispatcher is responsible for one provider
/// 
/// PIPELINE FLOW:
/// AnalyticsEventConsumer → AnalyticsDispatcherPipeline → IAnalyticsDispatcher[]
/// </summary>
public interface IAnalyticsDispatcher
{
    /// <summary>
    /// The analytics provider this dispatcher sends to.
    /// </summary>
    AnalyticsProvider Provider { get; }

    /// <summary>
    /// Priority for dispatch order (lower = first).
    /// AdminRealtime should be first (lowest priority number), external providers second.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Dispatches an analytics event to this provider.
    /// 
    /// RULES:
    /// - MUST NOT throw exceptions
    /// - MUST NOT block for more than 5 seconds
    /// - MUST check if enabled for tenant before sending
    /// - MUST return DispatchResult with success/failure status
    /// </summary>
    /// <param name="analyticsEvent">The event to dispatch</param>
    /// <param name="settings">Tenant-specific settings (may be null if not configured)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dispatch result with success/failure status and latency</returns>
    Task<AnalyticsDispatchResult> DispatchAsync(
        AnalyticsEvent analyticsEvent,
        TenantAnalyticsSettings? settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if this dispatcher is enabled for the given tenant.
    /// </summary>
    /// <param name="settings">Tenant-specific settings</param>
    /// <returns>True if enabled and properly configured</returns>
    bool IsEnabled(TenantAnalyticsSettings? settings);
}
