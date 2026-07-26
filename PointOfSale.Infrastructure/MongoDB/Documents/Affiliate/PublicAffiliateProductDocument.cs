using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;

/// <summary>
/// AF-11: Public affiliate product marketplace — MongoDB read model.
///
/// ARCHITECTURE:
/// - Cross-tenant document: allows public, unauthenticated browsing
/// - TenantId is stored for internal resolution ONLY (store mapping) — never exposed
/// - Updated via AffiliateProductPolicyProjectionConsumer
///
/// AF-17A: INVENTORY & PRICING TRUTH ALIGNMENT
/// - Must mirror Storefront's exact inventory and pricing logic
/// - No independent calculations allowed
/// - Fail-fast if data integrity violated
///
/// KILL LIST:
/// - ❌ TenantId never in API responses
/// - ❌ No commission data
/// </summary>
public class PublicAffiliateProductDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid ProductId { get; set; }

    [BsonElement("productSlug")]
    public string ProductSlug { get; set; } = string.Empty;

    [BsonElement("productName")]
    public string ProductName { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("thumbnailUrl")]
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// AF-17A: Display price for simple products (no variants).
    /// MUST be null if HasVariants = true.
    /// For variant products, use MinVariantPrice/MaxVariantPrice instead.
    /// </summary>
    [BsonElement("price")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? Price { get; set; }

    /// <summary>
    /// AF-17A: Minimum variant price (for products with variants).
    /// MUST be null if HasVariants = false.
    /// </summary>
    [BsonElement("minVariantPrice")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? MinVariantPrice { get; set; }

    /// <summary>
    /// AF-17A: Maximum variant price (for products with variants).
    /// MUST be null if HasVariants = false.
    /// </summary>
    [BsonElement("maxVariantPrice")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? MaxVariantPrice { get; set; }

    [BsonElement("currency")]
    public string Currency { get; set; } = string.Empty;

    [BsonElement("storeName")]
    public string StoreName { get; set; } = string.Empty;

    [BsonElement("storeSlug")]
    public string StoreSlug { get; set; } = string.Empty;

    [BsonElement("categoryName")]
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// AF-17A: Whether product has variants requiring selection.
    /// Mirrors ProductDocument.HasVariants.
    /// </summary>
    [BsonElement("hasVariants")]
    public bool HasVariants { get; set; }

    /// <summary>
    /// AF-17A: Total available stock across all variants/warehouses.
    /// Computed using Storefront's exact logic:
    /// - Variant products: Sum of all active variant TotalStock
    /// - Simple products: Product StockQuantity
    /// CRITICAL: Products with TotalAvailableStock = 0 must NOT be indexed.
    /// </summary>
    [BsonElement("totalAvailableStock")]
    public int TotalAvailableStock { get; set; }

    /// <summary>
    /// AF-17A: Whether product is in stock.
    /// Computed using Storefront's exact logic:
    /// - Variant products: ANY active variant has TotalStock > 0
    /// - Simple products: Product InStock property
    /// CRITICAL: Must match Storefront's InStock calculation exactly.
    /// </summary>
    [BsonElement("inStock")]
    public bool InStock { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; }

    [BsonElement("isAffiliateEnabled")]
    public bool IsAffiliateEnabled { get; set; }

    [BsonElement("affiliateEnabledAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime AffiliateEnabledAtUtc { get; set; }

    [BsonElement("tenantId")]
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// AF-16E: Authoritative storefront base URL for this product's tenant.
    /// CRITICAL: Must be stored to enable multi-tenant promotion link correctness.
    /// Example: "http://mena.localhost:5174" or "https://mena.example.com"
    /// </summary>
    [BsonElement("storefrontBaseUrl")]
    public string StorefrontBaseUrl { get; set; } = string.Empty;

    [BsonElement("lastStoreProfileEventVersion")]
    public int LastStoreProfileEventVersion { get; set; }

    [BsonElement("lastStoreProfileEventOccurredAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastStoreProfileEventOccurredAtUtc { get; set; }

    [BsonElement("lastStoreProfileEventId")]
    [BsonIgnoreIfNull]
    public string? LastStoreProfileEventId { get; set; }
}
