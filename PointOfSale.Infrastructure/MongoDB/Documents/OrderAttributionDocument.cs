using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB document for Order Attribution (Cold Storage).
/// 
/// COLLECTION: order_attributions
/// 
/// INDEXES:
/// 1. Unique: (TenantId, OrderId, Model) - One attribution per order per model
/// 2. Compound: (TenantId, AttributedAtUtc) - For time-range queries
/// 3. Compound: (TenantId, Source, AttributedAtUtc) - For ROI by source
/// 4. Compound: (TenantId, Campaign, AttributedAtUtc) - For campaign ROI
/// 
/// IMMUTABLE: Records cannot be updated after creation.
/// </summary>
[BsonIgnoreExtraElements]
public class OrderAttributionDocument
{
    /// <summary>
    /// MongoDB internal document ID.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>
    /// The attributed order ID.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid OrderId { get; set; }

    /// <summary>
    /// Tenant identifier for multi-tenant isolation.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// Session identifier that made the purchase.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Attribution model used (FirstTouch or LastTouch).
    /// Stored as string for readability in MongoDB queries.
    /// </summary>
    public string Model { get; set; } = "LastTouch";

    /// <summary>
    /// Attributed traffic source.
    /// </summary>
    public string Source { get; set; } = "direct";

    /// <summary>
    /// Attributed marketing medium.
    /// </summary>
    public string Medium { get; set; } = "none";

    /// <summary>
    /// Attributed campaign name.
    /// </summary>
    public string? Campaign { get; set; }

    /// <summary>
    /// Attributed content/creative.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Attributed search term.
    /// </summary>
    public string? Term { get; set; }

    /// <summary>
    /// Total order value.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal OrderValue { get; set; }

    /// <summary>
    /// Currency code for the order value.
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// When this attribution was recorded (UTC).
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime AttributedAtUtc { get; set; }

    /// <summary>
    /// Optional customer ID.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? CustomerId { get; set; }

    /// <summary>
    /// Time to conversion in seconds.
    /// </summary>
    public long? TimeToConversionSeconds { get; set; }

    /// <summary>
    /// When this document was stored (UTC).
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime StoredAtUtc { get; set; }
}
