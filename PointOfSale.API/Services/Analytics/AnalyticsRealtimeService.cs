using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Infrastructure.Analytics;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.Analytics;

/// <summary>
/// Implementation of IAnalyticsRealtimeService.
/// 
/// Reads behavioral telemetry from Redis and purchase truth from PostgreSQL.
/// Redis counters are never authoritative for completed purchases or conversion.
/// </summary>
public class AnalyticsRealtimeService : IAnalyticsRealtimeService
{
    private readonly IAnalyticsHotStoreService _hotStore;
    private readonly IStorefrontAnalyticsAuthorityReader _authorityReader;
    private readonly ILogger<AnalyticsRealtimeService> _logger;

    public AnalyticsRealtimeService(
        IAnalyticsHotStoreService hotStore,
        IStorefrontAnalyticsAuthorityReader authorityReader,
        ILogger<AnalyticsRealtimeService> logger)
    {
        _hotStore = hotStore;
        _authorityReader = authorityReader;
        _logger = logger;
    }

    public async Task<RealtimeAnalyticsDashboard> GetDashboardAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var dashboard = new RealtimeAnalyticsDashboard();
        var now = DateTime.UtcNow;

        try
        {
            // Get event counts for the current hour
            var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);

            // Get counts for each event type
            foreach (AnalyticsEventType eventType in Enum.GetValues<AnalyticsEventType>())
            {
                if (eventType == AnalyticsEventType.PurchaseCompleted)
                {
                    continue;
                }

                var count = await GetCounterValueAsync(tenantId, eventType.ToString().ToLowerInvariant(), hourStart, cancellationToken);
                dashboard.EventCountsByType[eventType.ToString()] = count;
            }

            var commerce = await _authorityReader.GetCommerceSnapshotAsync(
                tenantId,
                hourStart,
                now,
                cancellationToken);
            dashboard.EventCountsByType[nameof(AnalyticsEventType.PurchaseCompleted)] = commerce.PurchaseCount;

            // Populate specific metrics
            dashboard.PageViewsThisHour = dashboard.EventCountsByType.GetValueOrDefault("PageView", 0);
            dashboard.AddToCartsThisHour = dashboard.EventCountsByType.GetValueOrDefault("AddToCart", 0);
            dashboard.CheckoutsStartedThisHour = dashboard.EventCountsByType.GetValueOrDefault("BeginCheckout", 0);
            dashboard.PurchasesThisHour = dashboard.EventCountsByType.GetValueOrDefault("PurchaseCompleted", 0);

            // Calculate conversion rate
            if (dashboard.CheckoutsStartedThisHour > 0)
            {
                dashboard.ConversionRate = Math.Round(
                    (decimal)dashboard.PurchasesThisHour / dashboard.CheckoutsStartedThisHour * 100, 2);
            }

            // Get active users (approximate from session counters)
            dashboard.ActiveUsersLast5Min = await GetActiveSessionCountAsync(tenantId, cancellationToken);

            // Get recent events
            var recentEvents = await _hotStore.GetRecentEventsAsync(tenantId, null, 20, cancellationToken);
            dashboard.RecentEvents = recentEvents.Select(e => new RecentEventSummary
            {
                EventId = e.EventId,
                EventType = e.Type.ToString(),
                OccurredAtUtc = e.OccurredAtUtc,
                PageUrl = e.Context?.PageUrl,
                ProductName = e.Payload?.ProductName,
                Value = e.Payload?.Value,
                IsAuthenticated = e.Actor?.IsAuthenticated ?? false
            }).ToList();

            _logger.LogDebug(
                "[Analytics Realtime] Dashboard generated | TenantId: {TenantId} | PageViews: {PageViews} | Purchases: {Purchases}",
                tenantId,
                dashboard.PageViewsThisHour,
                dashboard.PurchasesThisHour);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics Realtime] Dashboard generation failed | TenantId: {TenantId}",
                tenantId);
        }

        return dashboard;
    }

    public async Task<ConversionFunnel> GetConversionFunnelAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var funnel = new ConversionFunnel();
        var now = DateTime.UtcNow;
        var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);

        try
        {
            funnel.PageViews = await GetCounterValueAsync(tenantId, "pageview", hourStart, cancellationToken);
            funnel.ProductViews = await GetCounterValueAsync(tenantId, "productview", hourStart, cancellationToken);
            funnel.AddToCarts = await GetCounterValueAsync(tenantId, "addtocart", hourStart, cancellationToken);
            funnel.BeginCheckouts = await GetCounterValueAsync(tenantId, "begincheckout", hourStart, cancellationToken);
            var commerce = await _authorityReader.GetCommerceSnapshotAsync(
                tenantId,
                hourStart,
                now,
                cancellationToken);
            funnel.Purchases = commerce.PurchaseCount;

            _logger.LogDebug(
                "[Analytics Realtime] Funnel generated | TenantId: {TenantId} | Conversion: {Conversion}%",
                tenantId,
                funnel.OverallConversionRate);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics Realtime] Funnel generation failed | TenantId: {TenantId}",
                tenantId);
        }

        return funnel;
    }

    public async Task<List<AnalyticsEvent>> GetRecentEventsAsync(
        Guid tenantId,
        AnalyticsEventType? eventType = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _hotStore.GetRecentEventsAsync(tenantId, eventType, limit, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics Realtime] Get recent events failed | TenantId: {TenantId}",
                tenantId);
            return new List<AnalyticsEvent>();
        }
    }

    private async Task<long> GetCounterValueAsync(
        Guid tenantId,
        string counterName,
        DateTime timeWindow,
        CancellationToken cancellationToken)
    {
        // Counter values are stored with the time window as part of the key
        // We need to read the counter, not increment it
        // For now, we'll use the increment with 0 to get the current value
        // A better approach would be to add a GetCounterAsync method to the hot store
        
        try
        {
            // Use the existing increment method with 0 to read current value
            return await _hotStore.IncrementCounterAsync(tenantId, counterName, timeWindow, 0, cancellationToken);
        }
        catch
        {
            return 0;
        }
    }

    private async Task<int> GetActiveSessionCountAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        // This is an approximate count based on recent events
        // A more accurate implementation would track unique sessions in Redis sets
        try
        {
            var recentEvents = await _hotStore.GetRecentEventsAsync(tenantId, null, 100, cancellationToken);
            var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);
            
            var activeSessions = recentEvents
                .Where(e => e.OccurredAtUtc >= fiveMinutesAgo)
                .Select(e => e.Actor?.SessionId)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .Count();

            return activeSessions;
        }
        catch
        {
            return 0;
        }
    }
}
