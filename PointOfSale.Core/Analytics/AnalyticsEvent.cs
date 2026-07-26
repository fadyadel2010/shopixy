namespace PointOfSale.Core.Analytics;

/// <summary>
/// The canonical analytics event - single source of truth for all storefront analytics.
/// 
/// ARCHITECTURAL PRINCIPLES:
/// - POS owns this schema - NOT third-party analytics providers
/// - Every field has a clear purpose
/// - Idempotent by EventId
/// - Tenant-isolated by TenantId
/// - Can be dispatched to ANY analytics provider without refactoring
/// 
/// STORAGE STRATEGY:
/// - Hot Storage (Redis): TTL-based, for realtime dashboards
/// - Cold Storage (MongoDB): Append-only, for historical analytics
/// 
/// FUTURE USE CASES:
/// - Realtime dashboards
/// - Historical analytics
/// - Conversion tracking
/// - ML/BI pipelines
/// - Attribution modeling
/// </summary>
public class AnalyticsEvent
{
    /// <summary>
    /// Unique identifier for this event instance.
    /// Used for idempotency - same EventId = same event (deduplicated).
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// The tenant that owns this event.
    /// CRITICAL: Required for multi-tenant isolation.
    /// Events without TenantId are REJECTED.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The type of analytics event.
    /// Determines which payload fields are expected.
    /// </summary>
    public AnalyticsEventType Type { get; set; }

    /// <summary>
    /// When the event occurred (UTC).
    /// Set by the frontend, validated by backend (not in future, not too old).
    /// </summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// WHO performed the event.
    /// Customer ID (if authenticated) or Session ID (if guest).
    /// </summary>
    public AnalyticsActor Actor { get; set; } = new();

    /// <summary>
    /// WHERE and HOW the event occurred.
    /// Page URL, device info, referrer, etc.
    /// </summary>
    public AnalyticsContext Context { get; set; } = new();

    /// <summary>
    /// WHAT exactly happened.
    /// Product IDs, quantities, values, etc.
    /// </summary>
    public AnalyticsPayload Payload { get; set; } = new();

    /// <summary>
    /// When this event was received by the server (UTC).
    /// Set by the backend for latency tracking.
    /// </summary>
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>
    /// Schema version for forward/backward compatibility.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Creates a new analytics event with a new EventId and current timestamps.
    /// </summary>
    public static AnalyticsEvent Create(Guid tenantId, AnalyticsEventType type)
    {
        var now = DateTime.UtcNow;
        return new AnalyticsEvent
        {
            EventId = Guid.NewGuid(),
            TenantId = tenantId,
            Type = type,
            OccurredAtUtc = now,
            ReceivedAtUtc = now
        };
    }

    /// <summary>
    /// Validates the event has minimum required data.
    /// </summary>
    public bool IsValid()
    {
        if (EventId == Guid.Empty) return false;
        if (TenantId == Guid.Empty) return false;
        if (!Enum.IsDefined(typeof(AnalyticsEventType), Type)) return false;
        if (OccurredAtUtc == default) return false;
        if (Actor == null || !Actor.IsValid()) return false;
        if (Context == null || string.IsNullOrWhiteSpace(Context.PageUrl)) return false;
        
        return true;
    }
}
