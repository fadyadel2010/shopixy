using PointOfSale.Core.Analytics;

namespace PointOfSale.API.Services.Analytics;

/// <summary>
/// Service for retrieving realtime analytics data for the Admin Dashboard.
/// 
/// DATA SOURCES:
/// - Redis: Realtime counters, active sessions, recent events
/// - NO database queries - all data from hot storage
/// 
/// SECURITY:
/// - Tenant-isolated (all queries scoped to TenantId)
/// - Read-only (no writes to any store)
/// - Cached data only (no heavy aggregations)
/// </summary>
public interface IAnalyticsRealtimeService
{
    /// <summary>
    /// Gets realtime analytics dashboard data for a tenant.
    /// </summary>
    Task<RealtimeAnalyticsDashboard> GetDashboardAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets conversion funnel data for a tenant.
    /// </summary>
    Task<ConversionFunnel> GetConversionFunnelAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent events for a specific event type.
    /// </summary>
    Task<List<AnalyticsEvent>> GetRecentEventsAsync(
        Guid tenantId,
        AnalyticsEventType? eventType = null,
        int limit = 50,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Realtime analytics dashboard data.
/// </summary>
public class RealtimeAnalyticsDashboard
{
    /// <summary>
    /// When this data was retrieved (UTC).
    /// </summary>
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of active users in the last 5 minutes.
    /// Based on unique session activity.
    /// </summary>
    public int ActiveUsersLast5Min { get; set; }

    /// <summary>
    /// Page views in the current hour.
    /// </summary>
    public long PageViewsThisHour { get; set; }

    /// <summary>
    /// Add to cart events in the current hour.
    /// </summary>
    public long AddToCartsThisHour { get; set; }

    /// <summary>
    /// Checkout started events in the current hour.
    /// </summary>
    public long CheckoutsStartedThisHour { get; set; }

    /// <summary>
    /// Completed purchases in the current hour.
    /// </summary>
    public long PurchasesThisHour { get; set; }

    /// <summary>
    /// Conversion rate (purchases / checkouts started).
    /// </summary>
    public decimal ConversionRate { get; set; }

    /// <summary>
    /// Event counts by type for the current hour.
    /// </summary>
    public Dictionary<string, long> EventCountsByType { get; set; } = new();

    /// <summary>
    /// Recent events (last 20).
    /// </summary>
    public List<RecentEventSummary> RecentEvents { get; set; } = new();
}

/// <summary>
/// Summary of a recent analytics event for dashboard display.
/// </summary>
public class RecentEventSummary
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string? PageUrl { get; set; }
    public string? ProductName { get; set; }
    public decimal? Value { get; set; }
    public bool IsAuthenticated { get; set; }
}

/// <summary>
/// Conversion funnel data.
/// </summary>
public class ConversionFunnel
{
    /// <summary>
    /// Total page views.
    /// </summary>
    public long PageViews { get; set; }

    /// <summary>
    /// Product views (from page views).
    /// </summary>
    public long ProductViews { get; set; }

    /// <summary>
    /// Add to cart (from product views).
    /// </summary>
    public long AddToCarts { get; set; }

    /// <summary>
    /// Begin checkout (from add to cart).
    /// </summary>
    public long BeginCheckouts { get; set; }

    /// <summary>
    /// Completed purchases (from begin checkout).
    /// </summary>
    public long Purchases { get; set; }

    /// <summary>
    /// Page view to product view rate.
    /// </summary>
    public decimal PageToProductRate => PageViews > 0 ? (decimal)ProductViews / PageViews * 100 : 0;

    /// <summary>
    /// Product view to add to cart rate.
    /// </summary>
    public decimal ProductToCartRate => ProductViews > 0 ? (decimal)AddToCarts / ProductViews * 100 : 0;

    /// <summary>
    /// Add to cart to checkout rate.
    /// </summary>
    public decimal CartToCheckoutRate => AddToCarts > 0 ? (decimal)BeginCheckouts / AddToCarts * 100 : 0;

    /// <summary>
    /// Checkout to purchase rate.
    /// </summary>
    public decimal CheckoutToPurchaseRate => BeginCheckouts > 0 ? (decimal)Purchases / BeginCheckouts * 100 : 0;

    /// <summary>
    /// Overall conversion rate (page view to purchase).
    /// </summary>
    public decimal OverallConversionRate => PageViews > 0 ? (decimal)Purchases / PageViews * 100 : 0;
}
