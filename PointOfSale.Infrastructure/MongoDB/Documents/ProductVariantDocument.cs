using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// Embedded document for variant attribute.
/// </summary>
public class VariantAttributeDocument
{
    /// <summary>
    /// Attribute name (e.g., "Color", "Size").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Attribute value (e.g., "Red", "XL").
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Display order for attribute selection UI.
    /// </summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// Tombstone for variant deletes. Deleting a variant removes it from the public
/// Variants array, so the product document must keep delete ordering metadata
/// separately to prevent delayed create/update events from resurrecting it.
/// </summary>
[BsonIgnoreExtraElements]
public class ProductVariantDeletionTombstoneDocument
{
    [BsonRepresentation(BsonType.String)]
    public Guid VariantId { get; set; }

    [BsonRepresentation(BsonType.String)]
    [BsonIgnoreIfNull]
    public Guid? LastEventId { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastEventOccurredAtUtc { get; set; }

    public int LastEventVersion { get; set; }
}

/// <summary>
/// Embedded document for product variant.
/// 
/// PHASE 11.3 — STOREFRONT VARIANT SELECTION
/// 
/// This document is embedded in ProductDocument.Variants[]
/// and contains all data needed for storefront variant selection:
/// - Price and OnlinePrice for dynamic pricing
/// - TotalStock for availability
/// - Attributes for variant selection UI
/// - ImageUrl for variant-specific images
/// 
/// CRITICAL:
/// - Every product MUST have at least one variant (IsDefault = true)
/// - Cart and Checkout use VariantId (not ProductId) for pricing
/// </summary>
[BsonIgnoreExtraElements]
public class ProductVariantDocument
{
    /// <summary>
    /// The variant ID from PostgreSQL.
    /// This is the ID used in Cart and Checkout.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid VariantId { get; set; }

    /// <summary>
    /// Variant-specific SKU.
    /// </summary>
    public string? SKU { get; set; }

    /// <summary>
    /// Variant-specific barcode.
    /// </summary>
    public string? Barcode { get; set; }

    /// <summary>
    /// POS sale price for this variant.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Price { get; set; }

    /// <summary>
    /// Online/Storefront price for e-commerce.
    /// THIS IS THE SOURCE OF TRUTH FOR STOREFRONT PRICING.
    /// If null or 0, falls back to Price.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? OnlinePrice { get; set; }

    /// <summary>
    /// Computed effective price for storefront: OnlinePrice if set, otherwise Price.
    /// </summary>
    [BsonIgnore]
    public decimal EffectivePrice => (OnlinePrice.HasValue && OnlinePrice.Value > 0) ? OnlinePrice.Value : Price;

    /// <summary>
    /// Cost/Purchase price for this variant.
    /// PHASE 11.4.1: Used for profit calculation on order items.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal CostPrice { get; set; }

    /// <summary>
    /// Product media image reference.
    /// Preferred over legacy ImageUrl when present.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? MediaId { get; set; }

    /// <summary>
    /// Variant-specific image URL.
    /// Falls back to product images if not set.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Whether this is the default variant.
    /// Default variant is auto-selected when no selection made.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Whether this variant is active and sellable.
    /// Inactive variants are hidden from storefront.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Total stock quantity across all warehouses.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalStock { get; set; }

    /// <summary>
    /// Computed: Whether this variant is in stock.
    /// </summary>
    [BsonIgnore]
    public bool InStock => TotalStock > 0;

    /// <summary>
    /// Display order for variant selection.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Variant attributes (Color, Size, etc.).
    /// Used for storefront variant selection UI.
    /// </summary>
    public List<VariantAttributeDocument> Attributes { get; set; } = new();

    /// <summary>
    /// When this variant document was last synced.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime SyncedAtUtc { get; set; }

    /// <summary>
    /// Source-of-truth stock timestamp used to reject stale stock projections.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? StockUpdatedAtUtc { get; set; }

    /// <summary>
    /// Event id of the newest authoritative stock event applied to this variant.
    /// Kept separate from variant metadata ordering so metadata updates cannot
    /// mask a missed stock event in downstream projections.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    [BsonIgnoreIfNull]
    public Guid? LastStockEventId { get; set; }

    /// <summary>
    /// Source-of-truth stock timestamp used to reject stale stock projections.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastStockUpdatedAtUtc { get; set; }

    /// <summary>
    /// Source stock event version used as a deterministic tie-breaker when stock
    /// timestamps match.
    /// </summary>
    public int LastStockEventVersion { get; set; }

    /// <summary>
    /// Source event timestamp used to reject stale variant metadata projections.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastEventOccurredAtUtc { get; set; }

    /// <summary>
    /// Event id of the newest variant lifecycle mutation applied to this document.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    [BsonIgnoreIfNull]
    public Guid? LastEventId { get; set; }

    /// <summary>
    /// Source event version used as a deterministic tie-breaker for equal timestamps.
    /// S40: Removed [BsonIgnoreIfDefault] to ensure version is always persisted (matching PromotionDocument/CouponDocument).
    /// </summary>
    public int LastEventVersion { get; set; }

    /// <summary>
    /// Variant weight in kg for shipping calculations.
    /// If null, falls back to Product.Weight.
    /// Used for weight-based shipping quotes when variant has different weight than product.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    [BsonIgnoreIfDefault]
    public decimal? Weight { get; set; }
}
