using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using PointOfSale.Infrastructure.ReadModels.Evolution;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB read model document for Promotions (Offers).
/// This is the denormalized representation for storefront display.
/// 
/// ARCHITECTURE (CQRS COMPLIANCE):
/// - This is NOT the source of truth (PostgreSQL is)
/// - Documents are tenant-isolated by TenantId
/// - PromotionId + TenantId forms the unique business key
/// - Storefront reads from this document for product badges
/// 
/// USE CASES:
/// - Product card "On Sale" badges
/// - Discounted price display
/// - Category page banners
/// - Countdown timers for ending promotions
/// </summary>
[BsonIgnoreExtraElements]
public class PromotionDocument : IReadModelEvolutionDocument
{
    public const int CurrentReadModelSchemaVersion = ReadModelEvolutionRegistry.StorefrontPromotionMongoSchemaVersion;
    public const int CurrentProjectionVersion = ReadModelEvolutionRegistry.StorefrontPromotionMongoProjectionVersion;

    /// <summary>
    /// MongoDB internal document ID - auto-generated if not set
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? Id { get; set; }

    public string ReadModelName { get; set; } = ReadModelEvolutionRegistry.StorefrontPromotion;

    public int ReadModelSchemaVersion { get; set; } = CurrentReadModelSchemaVersion;

    public int ProjectionVersion { get; set; } = CurrentProjectionVersion;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ProjectionConvergedAtUtc { get; set; }

    /// <summary>
    /// The tenant that owns this promotion.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// The PostgreSQL Promotion.Id - source of truth reference
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid PromotionId { get; set; }

    /// <summary>
    /// Promotion name for display
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description for admin/display purposes
    /// </summary>
    public string? Description { get; set; }

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
    /// Maximum discount amount for percentage promotions (cap)
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? MaxDiscountAmount { get; set; }

    /// <summary>
    /// Scope: "product", "category", or "cart"
    /// </summary>
    public string Scope { get; set; } = "product";

    /// <summary>
    /// The entity this promotion applies to (ProductId or CategoryId)
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? AppliesToId { get; set; }

    /// <summary>
    /// Promotion validity start date
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Promotion validity end date
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime EndDate { get; set; }

    /// <summary>
    /// Priority for conflict resolution
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Whether the promotion is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether this promotion is exclusive (cannot combine with coupons)
    /// </summary>
    public bool IsExclusive { get; set; }

    /// <summary>
    /// Minimum order amount
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? MinOrderAmount { get; set; }

    /// <summary>
    /// Maximum usage count
    /// </summary>
    public int? MaxUsage { get; set; }

    /// <summary>
    /// Current usage count
    /// </summary>
    public int CurrentUsage { get; set; }

    /// <summary>
    /// Badge text for storefront display (e.g., "20% OFF")
    /// </summary>
    public string Badge { get; set; } = string.Empty;

    /// <summary>
    /// When the promotion was created in PostgreSQL
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When this document was last synced from an event
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime SyncedAtUtc { get; set; }

    /// <summary>
    /// The EventId that last updated this document
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

    #region Computed Properties

    /// <summary>
    /// Check if promotion is currently valid based on dates
    /// </summary>
    public bool IsCurrentlyValid =>
        IsActive && DateTime.UtcNow >= StartDate && DateTime.UtcNow <= EndDate;

    /// <summary>
    /// Days until promotion ends (for countdown)
    /// </summary>
    public int? DaysRemaining =>
        IsCurrentlyValid ? (int)Math.Ceiling((EndDate - DateTime.UtcNow).TotalDays) : null;

    #endregion
}
