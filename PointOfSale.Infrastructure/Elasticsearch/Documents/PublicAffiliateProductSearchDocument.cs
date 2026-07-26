using Nest;

namespace PointOfSale.Infrastructure.Elasticsearch.Documents;

/// <summary>
/// AF-11: Elasticsearch document for public affiliate product marketplace search.
/// 
/// ARCHITECTURE:
/// - Cross-tenant mapping for global marketplace search
/// - Optimized for query text relevance and category filtering
/// 
/// AF-17A: INVENTORY & PRICING TRUTH ALIGNMENT
/// - Must mirror Storefront's exact inventory and pricing logic
/// - No independent calculations allowed
/// - Fail-fast if data integrity violated
/// 
/// Fields mapped based on AF-11 design guidelines.
/// </summary>
[ElasticsearchType(RelationName = "public_affiliate_product")]
public class PublicAffiliateProductSearchDocument
{
    [Keyword]
    public Guid ProductId { get; set; }

    /// <summary>
    /// Searchable product name.
    /// </summary>
    [Text]
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Searchable description.
    /// </summary>
    [Text]
    public string Description { get; set; } = string.Empty;

    [Keyword]
    public string CategoryName { get; set; } = string.Empty;

    [Keyword]
    public string StoreName { get; set; } = string.Empty;

    /// <summary>
    /// AF-17A: Display price for simple products (no variants).
    /// MUST be null if HasVariants = true.
    /// </summary>
    [Number(NumberType.Double)]
    public double? Price { get; set; }

    /// <summary>
    /// AF-17A: Minimum variant price (for products with variants).
    /// MUST be null if HasVariants = false.
    /// </summary>
    [Number(NumberType.Double)]
    public double? MinVariantPrice { get; set; }

    /// <summary>
    /// AF-17A: Maximum variant price (for products with variants).
    /// MUST be null if HasVariants = false.
    /// </summary>
    [Number(NumberType.Double)]
    public double? MaxVariantPrice { get; set; }

    [Date(Format = "strict_date_optional_time||epoch_millis")]
    public DateTime AffiliateEnabledAtUtc { get; set; }

    [Boolean]
    public bool IsAffiliateEnabled { get; set; }

    [Boolean]
    public bool IsActive { get; set; }

    // Other required fields for retrieval that are not primarily for search relevance but needed for display
    [Keyword]
    public string ThumbnailUrl { get; set; } = string.Empty;

    [Keyword]
    public string Currency { get; set; } = string.Empty;

    [Keyword]
    public string StoreSlug { get; set; } = string.Empty;

    [Keyword]
    public string ProductSlug { get; set; } = string.Empty;

    /// <summary>
    /// AF-17A: Whether product has variants requiring selection.
    /// </summary>
    [Boolean]
    public bool HasVariants { get; set; }

    /// <summary>
    /// AF-17A: Total available stock across all variants/warehouses.
    /// CRITICAL: Products with TotalAvailableStock = 0 must NOT be indexed.
    /// </summary>
    [Number(NumberType.Integer)]
    public int TotalAvailableStock { get; set; }

    /// <summary>
    /// AF-17A: Whether product is in stock.
    /// Must match Storefront's InStock calculation exactly.
    /// </summary>
    [Boolean]
    public bool InStock { get; set; }

    /// <summary>
    /// AF-16E: Authoritative storefront base URL for this product's tenant.
    /// CRITICAL: Must be indexed to enable multi-tenant promotion link correctness.
    /// Example: "http://mena.localhost:5174" or "https://mena.example.com"
    /// </summary>
    [Keyword]
    public string StorefrontBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// AF-16E: TenantId for internal resolution and fail-fast validation.
    /// </summary>
    [Keyword]
    public Guid TenantId { get; set; }
}
