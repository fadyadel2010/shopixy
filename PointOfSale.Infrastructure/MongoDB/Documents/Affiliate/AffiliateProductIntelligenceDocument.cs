using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;

/// <summary>
/// AF-22: Product Intelligence read model for Affiliate Discover & Landing pages.
///
/// Enriches product discovery with:
/// - Full product details
/// - Inventory availability
/// - Variant-level stock
/// - Store context
/// - Commission intelligence
///
/// Updated via:
/// - ProductCreatedEvent
/// - ProductUpdatedEvent
/// - ProductImagesUpdatedEvent
/// - InventoryAdjustedEvent
/// - VariantInventoryAdjustedEvent
/// - AffiliateProductPolicyUpdatedEvent
/// - StorefrontSettingsChangedEvent
///
/// IDEMPOTENCY: ProcessedEventIds tracks which EventIds have been applied.
/// INDEXES: (ProductId, TenantId) unique, (TenantId), (IsOutOfStock), (CommissionRate), (ProductName TEXT)
/// </summary>
[BsonIgnoreExtraElements]
public class AffiliateProductIntelligenceDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    // IDENTITY
    // ═══════════════════════════════════════════════════════════════════

    [BsonElement("productId")]
    [BsonRepresentation(BsonType.String)]
    public Guid ProductId { get; set; }

    [BsonElement("tenantId")]
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    // STORE CONTEXT
    // ═══════════════════════════════════════════════════════════════════

    [BsonElement("storeSlug")]
    public string StoreSlug { get; set; } = "";

    [BsonElement("storeName")]
    public string StoreName { get; set; } = "";

    [BsonElement("storeLogo")]
    public string StoreLogo { get; set; } = "";

    [BsonElement("storefrontUrl")]
    public string StorefrontUrl { get; set; } = "";

    // ═══════════════════════════════════════════════════════════════════
    // PRODUCT DETAILS
    // ═══════════════════════════════════════════════════════════════════

    [BsonElement("productName")]
    public string ProductName { get; set; } = "";

    [BsonElement("productSlug")]
    public string ProductSlug { get; set; } = "";

    [BsonElement("description")]
    public string Description { get; set; } = "";

    [BsonElement("images")]
    public List<string> Images { get; set; } = new();

    [BsonElement("basePrice")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal BasePrice { get; set; }

    [BsonElement("currency")]
    public string Currency { get; set; } = "";

    // ═══════════════════════════════════════════════════════════════════
    // COMMISSION INTELLIGENCE
    // ═══════════════════════════════════════════════════════════════════

    [BsonElement("commissionRate")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal CommissionRate { get; set; }

    [BsonElement("estimatedCommission")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal EstimatedCommission { get; set; }

    [BsonElement("templateType")]
    public string TemplateType { get; set; } = PointOfSale.Core.Constants.LandingTemplates.ClassicV1;

    // ═══════════════════════════════════════════════════════════════════
    // AF-30: MARKETPLACE CATEGORY
    // ═══════════════════════════════════════════════════════════════════

    [BsonElement("affiliateCategoryId")]
    [BsonRepresentation(BsonType.String)]
    public Guid? AffiliateCategoryId { get; set; }

    [BsonElement("affiliateCategoryName")]
    public string? AffiliateCategoryName { get; set; }

    [BsonElement("affiliateCategorySlug")]
    public string? AffiliateCategorySlug { get; set; }

    [BsonElement("affiliateParentCategoryName")]
    public string? AffiliateParentCategoryName { get; set; }

    [BsonElement("affiliateParentCategorySlug")]
    public string? AffiliateParentCategorySlug { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    // INVENTORY AVAILABILITY
    // ═══════════════════════════════════════════════════════════════════

    [BsonElement("totalAvailableStock")]
    public int? TotalAvailableStock { get; set; }

    [BsonElement("isOutOfStock")]
    public bool IsOutOfStock { get; set; }

    [BsonElement("lowStock")]
    public bool LowStock { get; set; }

    [BsonElement("variants")]
    public List<VariantStockSubDocument> Variants { get; set; } = new();

    [BsonElement("lastProductEventVersion")]
    public int LastProductEventVersion { get; set; }

    [BsonElement("lastProductEventOccurredAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastProductEventOccurredAtUtc { get; set; }

    [BsonElement("lastStockEventVersion")]
    public int LastStockEventVersion { get; set; }

    [BsonElement("lastStockUpdatedAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastStockUpdatedAtUtc { get; set; }

    [BsonElement("lastPolicyEventVersion")]
    public int LastPolicyEventVersion { get; set; }

    [BsonElement("lastPolicyEventOccurredAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastPolicyEventOccurredAtUtc { get; set; }

    [BsonElement("lastStoreProfileEventVersion")]
    public int LastStoreProfileEventVersion { get; set; }

    [BsonElement("lastStoreProfileEventOccurredAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastStoreProfileEventOccurredAtUtc { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    // META
    // ═══════════════════════════════════════════════════════════════════

    [BsonElement("lastUpdatedUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastUpdatedUtc { get; set; }

    [BsonElement("createdAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? CreatedAtUtc { get; set; }

    /// <summary>
    /// Idempotency guard: tracks which EventIds have been applied to this document.
    /// </summary>
    [BsonElement("processedEventIds")]
    public List<string> ProcessedEventIds { get; set; } = new();

    // ═══════════════════════════════════════════════════════════════════
    // AF-34: RANKING SIGNALS
    // Updated by event consumers (clicks, attributions, commissions).
    // Rolling metrics recalculated by daily background job.
    // RankingScore is precomputed — NEVER per-request.
    // ═══════════════════════════════════════════════════════════════════

    [BsonElement("totalClicks")]
    public int TotalClicks { get; set; }

    [BsonElement("totalConversions")]
    public int TotalConversions { get; set; }

    [BsonElement("totalEarnings")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalEarnings { get; set; }

    [BsonElement("rolling7DayClicks")]
    public int Rolling7DayClicks { get; set; }

    [BsonElement("rolling7DayConversions")]
    public int Rolling7DayConversions { get; set; }

    [BsonElement("rolling7DayEarnings")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Rolling7DayEarnings { get; set; }

    [BsonElement("lastClickUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastClickUtc { get; set; }

    [BsonElement("lastConversionUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastConversionUtc { get; set; }

    /// <summary>
    /// AF-34: Precomputed ranking score.
    /// Formula: weighted sum of normalized commission, rolling earnings/conversions/clicks, and freshness.
    /// Recalculated on click/conversion events and by daily background job.
    /// Index: { RankingScore: -1 }, { CategorySlug: 1, RankingScore: -1 }
    /// </summary>
    [BsonElement("rankingScore")]
    public double RankingScore { get; set; }
}

/// <summary>
/// AF-22: Variant stock subdocument for product intelligence.
/// </summary>
public class VariantStockSubDocument
{
    [BsonElement("variantId")]
    [BsonRepresentation(BsonType.String)]
    public Guid VariantId { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = "";

    [BsonElement("price")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Price { get; set; }

    [BsonElement("stock")]
    public int Stock { get; set; }
}
