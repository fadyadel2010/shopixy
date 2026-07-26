using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB read model document for shipping methods.
/// This is the denormalized representation optimized for storefront read queries.
/// 
/// ARCHITECTURE:
/// - This is NOT the source of truth (PostgreSQL is)
/// - Documents are tenant-isolated by TenantId
/// - ShippingMethodId + TenantId forms the unique business key
/// - Projected via ShippingMethodProjectionConsumer
/// </summary>
public class ShippingMethodDocument
{
    /// <summary>
    /// MongoDB internal document ID - auto-generated if not set
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? Id { get; set; }

    /// <summary>
    /// The tenant that owns this shipping method.
    /// CRITICAL: This MUST match the event's TenantId for tenant isolation.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// The PostgreSQL ShippingMethod.Id - source of truth reference
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid ShippingMethodId { get; set; }

    /// <summary>
    /// Shipping method name (English)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Shipping method name (Arabic)
    /// </summary>
    public string? NameAr { get; set; }

    /// <summary>
    /// Method description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Base shipping rate
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal BaseRate { get; set; }

    /// <summary>
    /// Additional rate per kg
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal PerKgRate { get; set; }

    /// <summary>
    /// Order total threshold for free shipping
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    [BsonIgnoreIfNull]
    public decimal? FreeShippingThreshold { get; set; }

    /// <summary>
    /// Currency code for shipping rates (e.g., USD, SAR)
    /// Defaults to tenant's default currency
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Minimum estimated delivery days
    /// </summary>
    public int EstimatedDeliveryDaysMin { get; set; }

    /// <summary>
    /// Maximum estimated delivery days
    /// </summary>
    public int EstimatedDeliveryDaysMax { get; set; }

    /// <summary>
    /// Whether this shipping method is currently available
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Tombstone flag. Deleted shipping methods stay as inactive tombstones so
    /// stale earlier events cannot resurrect storefront shipping options.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Display order in shipping method list
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Carrier/provider name (e.g., DHL, FedEx, Aramex)
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// Carrier code for carrier-agnostic shipping module (e.g., "bosta", "aramex").
    /// Links this shipping method to a carrier adapter.
    /// Null means "manual" (no carrier integration).
    /// </summary>
    public string? CarrierCode { get; set; }

    /// <summary>
    /// Tracking URL template (use {tracking_number} as placeholder)
    /// </summary>
    public string? TrackingUrlTemplate { get; set; }

    /// <summary>
    /// Countries where this shipping method is available
    /// Stored as JSON array of country codes
    /// </summary>
    public string? AvailableCountries { get; set; }

    /// <summary>
    /// When this document was last synced/updated from an event
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime SyncedAtUtc { get; set; }

    /// <summary>
    /// The EventId that last updated this document (for debugging/tracing)
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    [BsonIgnoreIfNull]
    public Guid? LastEventId { get; set; }

    /// <summary>
    /// Timestamp of the newest event applied to this projection.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastEventOccurredAtUtc { get; set; }

    /// <summary>
    /// Version of the newest event applied to this projection.
    /// </summary>
    public int LastEventVersion { get; set; }

    /// <summary>
    /// Service level for delivery speed (standard, express, same_day, next_day).
    /// Defaults to "standard" if not specified.
    /// </summary>
    public string? ServiceLevel { get; set; }
}
