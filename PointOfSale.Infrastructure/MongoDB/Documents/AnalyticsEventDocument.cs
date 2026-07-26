using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB document for analytics events (Cold Storage).
/// 
/// STORAGE STRATEGY:
/// - Append-only (no updates)
/// - Indexed by TenantId + OccurredAtUtc for time-series queries
/// - Indexed by TenantId + EventType for aggregation
/// - TTL optional (configurable per-tenant for GDPR compliance)
/// 
/// QUERY PATTERNS:
/// - Events by tenant in time range
/// - Events by type for conversion funnels
/// - Events by actor for user journey analysis
/// </summary>
[BsonIgnoreExtraElements]
public class AnalyticsEventDocument
{
    /// <summary>
    /// MongoDB internal document ID.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>
    /// Unique event ID for idempotency.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid EventId { get; set; }

    /// <summary>
    /// Tenant ID for isolation.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// Event type as string for readability in MongoDB queries.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// When the event occurred (UTC).
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// When the event was received by the server (UTC).
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>
    /// When this document was stored (UTC).
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime StoredAtUtc { get; set; }

    #region Actor (WHO)

    /// <summary>
    /// Authenticated customer ID (null for guests).
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? CustomerId { get; set; }

    /// <summary>
    /// Anonymous session ID for guest tracking.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Whether the user was authenticated.
    /// </summary>
    public bool IsAuthenticated { get; set; }

    #endregion

    #region Context (WHERE / HOW)

    /// <summary>
    /// Page URL where event occurred.
    /// </summary>
    public string PageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Referring URL.
    /// </summary>
    public string? Referrer { get; set; }

    /// <summary>
    /// User agent string.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Client IP (may be anonymized).
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User's language preference.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Currency for monetary values.
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Device type: desktop, mobile, tablet.
    /// </summary>
    public string? DeviceType { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmContent { get; set; }
    public string? UtmTerm { get; set; }

    #endregion

    #region Payload (WHAT)

    /// <summary>
    /// Product ID for product events.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? ProductId { get; set; }

    /// <summary>
    /// Variant ID for variant-specific events.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? VariantId { get; set; }

    /// <summary>
    /// Order ID for purchase events.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? OrderId { get; set; }

    /// <summary>
    /// Category ID for category events.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// Monetary value.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? Value { get; set; }

    /// <summary>
    /// Quantity for cart events.
    /// </summary>
    public int? Quantity { get; set; }

    /// <summary>
    /// Search query for search events.
    /// </summary>
    public string? SearchQuery { get; set; }

    /// <summary>
    /// Applied filters as BSON document.
    /// </summary>
    public BsonDocument? Filters { get; set; }

    /// <summary>
    /// Product name (denormalized).
    /// </summary>
    public string? ProductName { get; set; }

    /// <summary>
    /// Product SKU.
    /// </summary>
    public string? ProductSku { get; set; }

    /// <summary>
    /// Extra payload data as BSON document.
    /// </summary>
    public BsonDocument? Extra { get; set; }

    #endregion

    /// <summary>
    /// Schema version for migration support.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;
}
