namespace PointOfSale.Core.Analytics;

/// <summary>
/// Represents WHERE and HOW the analytics event occurred.
/// 
/// CAPTURE RULES:
/// - PageUrl: From frontend (required)
/// - Referrer: From frontend (optional)
/// - UserAgent: From HTTP headers (server-side)
/// - IpAddress: From HTTP context (server-side, anonymized if required)
/// - Language: From Accept-Language header or frontend
/// - Currency: From tenant configuration (server-side)
/// - DeviceType: Derived from UserAgent (server-side)
/// </summary>
public class AnalyticsContext
{
    /// <summary>
    /// The full URL of the page where the event occurred.
    /// Required for all events.
    /// </summary>
    public string PageUrl { get; set; } = string.Empty;

    /// <summary>
    /// The referring URL (where the user came from).
    /// Optional - may be null for direct traffic.
    /// </summary>
    public string? Referrer { get; set; }

    /// <summary>
    /// User agent string from the browser.
    /// CAPTURED SERVER-SIDE from HTTP headers.
    /// </summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>
    /// Client IP address.
    /// CAPTURED SERVER-SIDE from HTTP context.
    /// 
    /// PRIVACY: May be anonymized (last octet zeroed) based on tenant settings.
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// User's preferred language (e.g., "en", "ar").
    /// From Accept-Language header or frontend state.
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Currency used for monetary values in this event.
    /// RESOLVED SERVER-SIDE from tenant configuration.
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Device type derived from UserAgent.
    /// Values: "desktop", "mobile", "tablet", "unknown"
    /// DERIVED SERVER-SIDE from UserAgent parsing.
    /// </summary>
    public string DeviceType { get; set; } = "unknown";

    /// <summary>
    /// Screen width in pixels (optional).
    /// Provided by frontend for responsive analytics.
    /// </summary>
    public int? ScreenWidth { get; set; }

    /// <summary>
    /// Screen height in pixels (optional).
    /// Provided by frontend for responsive analytics.
    /// </summary>
    public int? ScreenHeight { get; set; }

    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmContent { get; set; }
    public string? UtmTerm { get; set; }
}
