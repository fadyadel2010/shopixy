using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB document for Attribution Context (Cold Storage).
/// 
/// COLLECTION: attribution_contexts
/// 
/// INDEXES:
/// 1. Unique: (TenantId, SessionId, CapturedAtUtc) - For chronological tracking
/// 2. Compound: (TenantId, CapturedAtUtc) - For time-range queries
/// 3. Compound: (TenantId, Source, CapturedAtUtc) - For source analysis
/// 
/// APPEND-ONLY: No updates - each PageView with UTM creates a new record.
/// This allows both FirstTouch and LastTouch attribution.
/// </summary>
[BsonIgnoreExtraElements]
public class AttributionContextDocument
{
    /// <summary>
    /// MongoDB internal document ID.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>
    /// Tenant identifier for multi-tenant isolation.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// POS-owned session identifier.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Traffic source (lowercase, trimmed).
    /// </summary>
    public string Source { get; set; } = "direct";

    /// <summary>
    /// Marketing medium (lowercase, trimmed).
    /// </summary>
    public string Medium { get; set; } = "none";

    /// <summary>
    /// Campaign name.
    /// </summary>
    public string? Campaign { get; set; }

    /// <summary>
    /// Ad content or creative identifier.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Search term or keyword.
    /// </summary>
    public string? Term { get; set; }

    /// <summary>
    /// Referrer URL.
    /// </summary>
    public string? Referrer { get; set; }

    /// <summary>
    /// Landing page URL.
    /// </summary>
    public string? LandingPageUrl { get; set; }

    /// <summary>
    /// When this context was captured (UTC).
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CapturedAtUtc { get; set; }

    /// <summary>
    /// Optional customer ID if authenticated.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? CustomerId { get; set; }

    /// <summary>
    /// When this document was stored (UTC).
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime StoredAtUtc { get; set; }
}
