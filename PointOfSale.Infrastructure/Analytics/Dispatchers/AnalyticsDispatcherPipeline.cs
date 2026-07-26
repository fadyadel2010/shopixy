using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;

namespace PointOfSale.Infrastructure.Analytics.Dispatchers;

/// <summary>
/// Pipeline that orchestrates dispatching analytics events to all enabled providers.
/// 
/// ARCHITECTURAL RULES:
/// - Fire-and-forget dispatch (never block ingestion)
/// - Failures per provider are logged but don't affect others
/// - Providers are dispatched in priority order
/// - Never fail the main pipeline
/// 
/// FLOW:
/// AnalyticsEventConsumer
///    ↓
/// AnalyticsDispatcherPipeline.DispatchToAllAsync()
///    ↓
/// ┌─────────────────────────────────────────────────┐
/// │ For each enabled IAnalyticsDispatcher (sorted)  │
/// │ ├── AdminRealtimeDispatcher (priority 0)        │
/// │ ├── GoogleAnalyticsDispatcher (priority 10)     │
/// │ └── MetaConversionsApiDispatcher (priority 20)  │
/// └─────────────────────────────────────────────────┘
/// </summary>
public class AnalyticsDispatcherPipeline
{
    private readonly IEnumerable<IAnalyticsDispatcher> _dispatchers;
    private readonly IAnalyticsSettingsService _settingsService;
    private readonly ILogger<AnalyticsDispatcherPipeline> _logger;

    public AnalyticsDispatcherPipeline(
        IEnumerable<IAnalyticsDispatcher> dispatchers,
        IAnalyticsSettingsService settingsService,
        ILogger<AnalyticsDispatcherPipeline> logger)
    {
        _dispatchers = dispatchers.OrderBy(d => d.Priority);
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Dispatches an analytics event to all enabled providers.
    /// 
    /// RULES:
    /// - Never throws - all failures are caught and logged
    /// - Returns list of results for observability
    /// - Dispatch failures don't affect ingestion
    /// </summary>
    public async Task<List<AnalyticsDispatchResult>> DispatchToAllAsync(
        AnalyticsEvent analyticsEvent,
        CancellationToken cancellationToken = default)
    {
        var results = new List<AnalyticsDispatchResult>();

        try
        {
            // Get tenant settings (cached)
            var settings = await _settingsService.GetSettingsAsync(analyticsEvent.TenantId, cancellationToken);

            foreach (var dispatcher in _dispatchers)
            {
                try
                {
                    var result = await dispatcher.DispatchAsync(analyticsEvent, settings, cancellationToken);
                    results.Add(result);

                    if (!result.Success && result.ErrorMessage != null && 
                        !result.ErrorMessage.StartsWith("not enabled", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning(
                            "[Analytics Pipeline] Dispatch failed | Provider: {Provider} | TenantId: {TenantId} | EventId: {EventId} | Error: {Error}",
                            dispatcher.Provider,
                            analyticsEvent.TenantId,
                            analyticsEvent.EventId,
                            result.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[Analytics Pipeline] Dispatcher threw exception | Provider: {Provider} | TenantId: {TenantId} | EventId: {EventId}",
                        dispatcher.Provider,
                        analyticsEvent.TenantId,
                        analyticsEvent.EventId);

                    results.Add(AnalyticsDispatchResult.Fail(dispatcher.Provider, ex.Message));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Analytics Pipeline] Pipeline failed | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId,
                analyticsEvent.EventId);
        }

        // Log summary
        var successful = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);

        _logger.LogDebug(
            "[Analytics Pipeline] Complete | TenantId: {TenantId} | EventId: {EventId} | Success: {Success} | Failed: {Failed}",
            analyticsEvent.TenantId,
            analyticsEvent.EventId,
            successful,
            failed);

        return results;
    }

    /// <summary>
    /// Dispatches an analytics event to a specific provider.
    /// Used for testing or targeted dispatch.
    /// </summary>
    public async Task<AnalyticsDispatchResult> DispatchToProviderAsync(
        AnalyticsEvent analyticsEvent,
        AnalyticsProvider provider,
        CancellationToken cancellationToken = default)
    {
        var dispatcher = _dispatchers.FirstOrDefault(d => d.Provider == provider);
        if (dispatcher == null)
        {
            return AnalyticsDispatchResult.Fail(provider, $"No dispatcher registered for {provider}");
        }

        try
        {
            var settings = await _settingsService.GetSettingsAsync(analyticsEvent.TenantId, cancellationToken);
            return await dispatcher.DispatchAsync(analyticsEvent, settings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Analytics Pipeline] Targeted dispatch failed | Provider: {Provider} | TenantId: {TenantId}",
                provider,
                analyticsEvent.TenantId);

            return AnalyticsDispatchResult.Fail(provider, ex.Message);
        }
    }
}
