namespace PointOfSale.Core.Analytics;

/// <summary>
/// Tenant-level analytics provider settings.
/// Stored encrypted in the database, cached in Redis.
/// 
/// SECURITY RULES:
/// - Secrets encrypted at rest (Data Protection API)
/// - Never logged or exposed in API responses
/// - Changes take effect immediately (cache invalidation)
/// </summary>
public class TenantAnalyticsSettings
{
    /// <summary>
    /// Tenant ID this settings belongs to.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Google Analytics 4 settings.
    /// </summary>
    public GoogleAnalyticsSettings? Google { get; set; }

    /// <summary>
    /// Meta Conversions API settings.
    /// </summary>
    public MetaAnalyticsSettings? Meta { get; set; }

    /// <summary>
    /// TikTok Events API settings.
    /// </summary>
    public TikTokAnalyticsSettings? TikTok { get; set; }

    /// <summary>
    /// When settings were last updated (UTC).
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Google Analytics 4 (Measurement Protocol) settings.
/// 
/// SECURITY: 
/// - API secret is encrypted at rest
/// - NO gtag.js code on storefront
/// - Server-side dispatch only
/// </summary>
public class GoogleAnalyticsSettings
{
    /// <summary>
    /// Whether GA4 dispatch is enabled for this tenant.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// GA4 Measurement ID (e.g., "G-XXXXXXXXXX").
    /// Public identifier, safe to store.
    /// </summary>
    public string? MeasurementId { get; set; }

    /// <summary>
    /// GA4 API Secret for Measurement Protocol.
    /// ENCRYPTED at rest - never expose in logs/responses.
    /// </summary>
    public string? ApiSecret { get; set; }

    /// <summary>
    /// Whether to send debug events (for testing).
    /// Uses debug endpoint: /debug/mp/collect
    /// </summary>
    public bool DebugMode { get; set; }
}

/// <summary>
/// Meta Conversions API (CAPI) settings.
/// 
/// SECURITY:
/// - Access token is encrypted at rest
/// - Pixel ID never exposed to frontend
/// - All PII hashed server-side (SHA256)
/// - Server-side dispatch only
/// </summary>
public class MetaAnalyticsSettings
{
    /// <summary>
    /// Whether Meta CAPI dispatch is enabled for this tenant.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Meta Pixel ID.
    /// </summary>
    public string? PixelId { get; set; }

    /// <summary>
    /// Meta Conversions API access token.
    /// ENCRYPTED at rest - never expose in logs/responses.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Whether to use test event code (for debugging).
    /// </summary>
    public string? TestEventCode { get; set; }
}

/// <summary>
/// TikTok Events API settings.
///
/// SECURITY:
/// - Access token is encrypted at rest
/// - Pixel ID never exposed to storefront code
/// - Server-side dispatch only through provider delivery infrastructure
/// </summary>
public class TikTokAnalyticsSettings
{
    public const string DefaultEndpoint = "https://business-api.tiktok.com/open_api/v1.3/event/track/";

    /// <summary>
    /// Whether TikTok Events API dispatch is enabled for this tenant.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// TikTok Pixel ID / event_source_id.
    /// </summary>
    public string? PixelId { get; set; }

    /// <summary>
    /// TikTok Events API access token.
    /// ENCRYPTED at rest - never expose in logs/responses.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Indicates that the tenant is validating TikTok delivery in test mode.
    /// </summary>
    public bool TestMode { get; set; }

    /// <summary>
    /// TikTok Events API endpoint. Defaults to Events API 2.0 v1.3 track endpoint.
    /// </summary>
    public string? Endpoint { get; set; } = DefaultEndpoint;

    /// <summary>
    /// Provider request timeout in seconds.
    /// </summary>
    public int Timeout { get; set; } = 30;
}
