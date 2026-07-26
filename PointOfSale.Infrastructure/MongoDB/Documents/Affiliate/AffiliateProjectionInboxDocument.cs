using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;

/// <summary>
/// Idempotency inbox for affiliate projection consumers.
/// 
/// PROBLEM: Long-lived aggregates (AffiliateDashboardDocument, AffiliateEarningsSummaryDocument)
/// were accumulating unbounded ProcessedEventIds arrays, causing eventual MongoDB 16MB document
/// size failures.
/// 
/// SOLUTION: Move idempotency tracking to a dedicated inbox collection with a unique constraint
/// encoded in the document Id. This allows efficient
/// idempotency checks without bloating aggregate documents.
/// 
/// PROJECTION TYPES:
/// - "Dashboard": AffiliateDashboardDocument (Currency = empty string)
/// - "Summary": AffiliateEarningsSummaryDocument (Currency = actual currency code)
/// 
/// UNIQUE KEY: The document Id is a composite string containing tenantId, projectionType,
/// aggregateId, optional currency, and eventId to prevent duplicate event processing across
/// different projection types.
/// 
/// SCOPE: Only used for long-lived aggregates (dashboard, summary).
/// Order documents retain ProcessedEventIds since they have bounded lifetime (one per order).
/// </summary>
[BsonIgnoreExtraElements]
public class AffiliateProjectionInboxDocument
{
    /// <summary>
    /// MongoDB document ID used as the inbox idempotency key.
    /// Format: tenantId_projectionType_aggregateId_[currency_]eventId
    /// </summary>
    [BsonId]
    [BsonRepresentation(global::MongoDB.Bson.BsonType.String)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Tenant ID for tenant isolation.
    /// </summary>
    [BsonElement("tenantId")]
    [BsonRepresentation(global::MongoDB.Bson.BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// Projection type discriminator to prevent key collisions between different projections.
    /// Values: "Dashboard", "Summary"
    /// </summary>
    [BsonElement("projectionType")]
    [BsonRequired]
    public string ProjectionType { get; set; } = string.Empty;

    /// <summary>
    /// Aggregate ID (AffiliateAccountId for dashboard/summary).
    /// </summary>
    [BsonElement("aggregateId")]
    [BsonRepresentation(global::MongoDB.Bson.BsonType.String)]
    public Guid AggregateId { get; set; }

    /// <summary>
    /// Currency code for summary projections (empty string for dashboard).
    /// Included in the composite Id for currency-scoped summary idempotency.
    /// </summary>
    [BsonElement("currency")]
    [BsonRequired]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Event ID that was processed.
    /// </summary>
    [BsonElement("eventId")]
    [BsonRepresentation(global::MongoDB.Bson.BsonType.String)]
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// Event type for debugging/monitoring.
    /// </summary>
    [BsonElement("eventType")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// When this event was processed.
    /// </summary>
    [BsonElement("processedAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ProcessedAtUtc { get; set; }

    /// <summary>
    /// Projection type constants.
    /// </summary>
    public static class ProjectionTypes
    {
        public const string Dashboard = "Dashboard";
        public const string Summary = "Summary";
    }
}
