using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB read model document for Coupons.
/// This is the denormalized representation optimized for storefront validation.
/// 
/// ARCHITECTURE (CQRS COMPLIANCE):
/// - This is NOT the source of truth (PostgreSQL is)
/// - Documents are tenant-isolated by TenantId
/// - CouponId + TenantId forms the unique business key
/// - Storefront MUST read from this document, NEVER from PostgreSQL
/// 
/// CRITICAL VALIDATION RULE:
/// - All coupon validation MUST use this document
/// - CurrentUsage is updated via CouponUsageChangedEvent
/// - Never query PostgreSQL for coupon data in Storefront
/// </summary>
public class CouponDocument
{
    /// <summary>
    /// MongoDB internal document ID - auto-generated if not set
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? Id { get; set; }

    /// <summary>
    /// The tenant that owns this coupon.
    /// CRITICAL: This MUST match the event's TenantId for tenant isolation.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// The PostgreSQL Coupon.Id - source of truth reference
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid CouponId { get; set; }

    /// <summary>
    /// Coupon code (normalized to uppercase)
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Discount type: "percentage" or "fixed"
    /// </summary>
    public string DiscountType { get; set; } = "percentage";

    /// <summary>
    /// Discount value (percentage 0-100 or fixed amount)
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal DiscountValue { get; set; }

    /// <summary>
    /// Maximum discount amount for percentage coupons (cap)
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? MaxDiscountAmount { get; set; }

    /// <summary>
    /// Minimum order amount required to use this coupon
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? MinOrderAmount { get; set; }

    /// <summary>
    /// Maximum total uses for this coupon (null = unlimited)
    /// </summary>
    public int? MaxUsage { get; set; }

    /// <summary>
    /// Current usage count (updated via CouponUsageChangedEvent)
    /// </summary>
    public int CurrentUsage { get; set; }

    /// <summary>
    /// Maximum uses per customer (null = unlimited)
    /// </summary>
    public int? MaxUsagePerCustomer { get; set; }

    /// <summary>
    /// Whether this coupon is only valid for first orders
    /// </summary>
    public bool FirstOrderOnly { get; set; }

    /// <summary>
    /// Coupon validity start date (null = immediately valid)
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// Coupon validity end date (null = never expires)
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Whether the coupon is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Description for display purposes
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional: Product IDs this coupon applies to (null = all products)
    /// </summary>
    public List<Guid>? ApplicableProductIds { get; set; }

    /// <summary>
    /// Optional: Category IDs this coupon applies to (null = all categories)
    /// </summary>
    public List<Guid>? ApplicableCategoryIds { get; set; }

    /// <summary>
    /// Optional: Product IDs this coupon must never apply to.
    /// </summary>
    public List<Guid>? ExcludedProductIds { get; set; }

    /// <summary>
    /// When the coupon was created in PostgreSQL (source of truth)
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When the coupon was deleted (soft delete timestamp)
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>
    /// When this document was last synced/updated from an event
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime SyncedAtUtc { get; set; }

    /// <summary>
    /// The EventId that last updated this document (for debugging/tracing)
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? LastEventId { get; set; }

    /// <summary>
    /// Source event timestamp used to reject stale projection writes.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastEventOccurredAtUtc { get; set; }

    /// <summary>
    /// Source event version used as a deterministic tie-breaker for equal timestamps.
    /// </summary>
    public int LastEventVersion { get; set; }
}
