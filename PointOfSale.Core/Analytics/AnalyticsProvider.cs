namespace PointOfSale.Core.Analytics;

/// <summary>
/// Analytics providers that can receive dispatched events.
/// 
/// ARCHITECTURAL RULE:
/// - Providers NEVER see the storefront directly
/// - They only receive what POS decides to forward
/// - Adding a new provider = new enum value + new dispatcher implementation
/// </summary>
public enum AnalyticsProvider
{
    /// <summary>
    /// Google Analytics 4 (Measurement Protocol)
    /// Server-side only - NO gtag.js on frontend
    /// </summary>
    GoogleAnalytics = 1,

    /// <summary>
    /// Meta Conversions API (Server-side)
    /// Server-side only - NO Meta Pixel on frontend
    /// </summary>
    Meta = 2,

    /// <summary>
    /// POS Admin Realtime Dashboard
    /// Internal use - reads from Redis hot store
    /// </summary>
    AdminRealtime = 3,

    /// <summary>
    /// TikTok Events API (server-side)
    /// Server-side only - delivery is handled through the shared provider infrastructure.
    /// </summary>
    TikTok = 4
}
