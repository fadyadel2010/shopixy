namespace PointOfSale.Core.DTOs;

// =============================================================================
// ELASTICSEARCH-POWERED PRODUCT DISCOVERY DTOs
// Phase: Shopify-Grade Product Filtering & Discovery
// 
// CORE PRINCIPLES:
// - Elasticsearch supplies search candidates; SQL authority is reapplied before response shaping
// - NO MongoDB authority for product listing
// - NO frontend filtering or sorting
// - Faceted navigation with dynamic counts
// - Promotion-aware ranking (additive, never overrides text relevance)
// =============================================================================

/// <summary>
/// Query parameters for product discovery endpoint.
/// Elasticsearch applies candidate filtering, sorting, and pagination before SQL authority recomposition.
/// 
/// API Contract:
/// GET /api/storefront/products/discover
/// </summary>
public class ProductDiscoveryQuery
{
    /// <summary>
    /// Search query text (optional for browse mode).
    /// When empty, returns all products (discovery mode).
    /// </summary>
    public string? Q { get; set; }

    /// <summary>
    /// Category filter (ID or slug). Supports multiple values.
    /// Deep linking: /products?category=phones&category=tablets
    /// </summary>
    public List<string>? Category { get; set; }

    /// <summary>
    /// Brand filter. Supports multiple values (OR).
    /// Example: brand=Apple&brand=Samsung
    /// </summary>
    public List<string>? Brand { get; set; }

    /// <summary>
    /// Minimum price filter.
    /// CRITICAL: For HasVariants=true products, price may be null.
    /// </summary>
    public decimal? PriceFrom { get; set; }

    /// <summary>
    /// Maximum price filter.
    /// </summary>
    public decimal? PriceTo { get; set; }

    /// <summary>
    /// Filter by active promotion.
    /// true = only promoted products
    /// false = only non-promoted products
    /// null = all products
    /// </summary>
    public bool? HasPromotion { get; set; }

    /// <summary>
    /// Filter by stock availability.
    /// true = in stock only
    /// false = out of stock only
    /// null = all products
    /// </summary>
    public bool? InStock { get; set; }

    /// <summary>
    /// Filter by variant products.
    /// true = only products with variants
    /// false = only simple products
    /// null = all products
    /// </summary>
    public bool? HasVariants { get; set; }

    /// <summary>
    /// Sort mode. 
    /// Default: relevance (uses _score for search, createdAt for browse)
    /// </summary>
    public ProductSortMode Sort { get; set; } = ProductSortMode.Relevance;

    /// <summary>
    /// Page number (1-indexed).
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Items per page.
    /// </summary>
    public int PageSize { get; set; } = 12;

    /// <summary>
    /// Language for display: en or ar.
    /// Affects search field boosting and display text.
    /// </summary>
    public string Lang { get; set; } = "en";

    #region Validation Constants
    public const int MinPage = 1;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 12;
    #endregion

    /// <summary>
    /// Validates query parameters.
    /// Returns (ErrorCode, ErrorMessage) tuple, or (null, null) if valid.
    /// </summary>
    public (string? ErrorCode, string? ErrorMessage) Validate()
    {
        // Validate pagination
        if (Page < MinPage)
            return ("INVALID_PAGINATION", $"Page must be at least {MinPage}");

        if (PageSize < MinPageSize || PageSize > MaxPageSize)
            return ("INVALID_PAGINATION", $"PageSize must be between {MinPageSize} and {MaxPageSize}");

        // Validate price range
        if (PriceFrom.HasValue && PriceTo.HasValue && PriceFrom > PriceTo)
            return ("INVALID_PRICE_RANGE", "PriceFrom cannot be greater than PriceTo");

        if (PriceFrom.HasValue && PriceFrom < 0)
            return ("INVALID_PRICE_RANGE", "PriceFrom must be non-negative");

        // Validate language
        if (!string.IsNullOrEmpty(Lang) && Lang != "en" && Lang != "ar")
            return ("INVALID_LANGUAGE", "Language must be 'en' or 'ar'");

        return (null, null);
    }

    /// <summary>
    /// Whether this is a search query (has Q) vs a browse query (no Q).
    /// </summary>
    public bool IsSearchMode => !string.IsNullOrWhiteSpace(Q);
}

/// <summary>
/// Sort modes for product discovery.
/// All sorting is done in Elasticsearch, NEVER frontend.
/// </summary>
public enum ProductSortMode
{
    /// <summary>
    /// Default: _score for search, createdAt desc for browse.
    /// </summary>
    Relevance = 0,

    /// <summary>
    /// Price ascending (lowest first).
    /// </summary>
    PriceAsc = 1,

    /// <summary>
    /// Price descending (highest first).
    /// </summary>
    PriceDesc = 2,

    /// <summary>
    /// Newest products first (createdAt desc).
    /// </summary>
    Newest = 3,

    /// <summary>
    /// Highest discount percentage first.
    /// </summary>
    HighestDiscount = 4
}

/// <summary>
/// Product discovery response with items, pagination, and facets.
/// 
/// CRITICAL: This is the COMPLETE contract for the discovery API.
/// Frontend receives everything it needs - no recalculation allowed.
/// </summary>
public class ProductDiscoveryResult
{
    /// <summary>
    /// Product items for current page.
    /// </summary>
    public List<ProductDiscoveryItemDto> Items { get; set; } = new();

    /// <summary>
    /// Total number of matching products.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Current page number (1-indexed).
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Items per page.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>
    /// Whether next page exists.
    /// </summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    /// Whether previous page exists.
    /// </summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>
    /// Faceted navigation data.
    /// CRITICAL: Facets reflect CURRENT filter state.
    /// </summary>
    public ProductDiscoveryFacets Facets { get; set; } = new();

    /// <summary>
    /// Search query (empty for browse mode).
    /// </summary>
    public string? Query { get; set; }

    /// <summary>
    /// Search time in milliseconds (for diagnostics).
    /// </summary>
    public long SearchTimeMs { get; set; }
}

/// <summary>
/// A single product item in discovery results.
/// Optimized for card/list display.
/// </summary>
public class ProductDiscoveryItemDto
{
    /// <summary>
    /// Product unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Product name (English).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Product name (Arabic). Optional.
    /// </summary>
    public string? NameAr { get; set; }

    /// <summary>
    /// SEO-friendly URL slug.
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// Existing product-level storefront template/layout preference.
    /// Uses the same source as ProductTemplate in MongoDB projections.
    /// </summary>
    public string? StorefrontLayout { get; set; }

    /// <summary>
    /// Whether the product is curated as featured.
    /// </summary>
    public bool IsFeatured { get; set; }

    /// <summary>
    /// Product price.
    /// CRITICAL: Null for HasVariants=true products.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Backend-composed pricing presentation.
    /// Frontend may format these values, but must not calculate discounts.
    /// </summary>
    public ProductDiscoveryPricingDto Pricing { get; set; } = new();

    /// <summary>
    /// Display text for price.
    /// For variants: "Multiple options" (EN) / "متعدد الاختيارات" (AR)
    /// For simple: null (frontend formats Price)
    /// </summary>
    public string? DisplayPriceText { get; set; }

    /// <summary>
    /// Brand name for display.
    /// </summary>
    public string? Brand { get; set; }

    /// <summary>
    /// Category name for display.
    /// </summary>
    public string? CategoryName { get; set; }

    /// <summary>
    /// Category ID for filtering.
    /// </summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// Primary image URL.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Whether product has variants.
    /// </summary>
    public bool HasVariants { get; set; }

    /// <summary>
    /// Whether product is in stock (any variant or product-level).
    /// </summary>
    public bool InStock { get; set; }

    /// <summary>
    /// Whether product has an active promotion.
    /// </summary>
    public bool HasActivePromotion { get; set; }

    /// <summary>
    /// Promotion badge text (e.g., "20% OFF").
    /// </summary>
    public string? PromotionBadge { get; set; }

    /// <summary>
    /// Discount type: "percentage" or "fixed".
    /// </summary>
    public string? PromotionDiscountType { get; set; }

    /// <summary>
    /// Discount value for calculating discounted price.
    /// </summary>
    public decimal? PromotionDiscountValue { get; set; }

    /// <summary>
    /// Elasticsearch relevance score.
    /// </summary>
    public double Score { get; set; }
}

public class ProductDiscoveryPricingDto
{
    public decimal CurrentPrice { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public decimal? DiscountedPrice { get; set; }
    public string? DiscountBadge { get; set; }
}

/// <summary>
/// Faceted navigation data.
/// All counts reflect current filter state (for dynamic facets).
/// </summary>
public class ProductDiscoveryFacets
{
    /// <summary>
    /// Brand facet with counts.
    /// Key = brand name, Count = matching products.
    /// </summary>
    public List<FacetBucket> Brands { get; set; } = new();

    /// <summary>
    /// Category facet with counts.
    /// </summary>
    public List<CategoryFacetBucket> Categories { get; set; } = new();

    /// <summary>
    /// Price range facet with predefined ranges.
    /// </summary>
    public List<PriceRangeBucket> PriceRanges { get; set; } = new();

    /// <summary>
    /// Promotion facet.
    /// True = count of promoted products, False = count of non-promoted.
    /// </summary>
    public PromotionFacet Promotions { get; set; } = new();

    /// <summary>
    /// Stock availability facet.
    /// </summary>
    public StockFacet Stock { get; set; } = new();
}

/// <summary>
/// Generic facet bucket with key and count.
/// </summary>
public class FacetBucket
{
    /// <summary>
    /// Facet key (e.g., brand name).
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Number of products matching this facet value.
    /// </summary>
    public long Count { get; set; }
}

/// <summary>
/// Category facet bucket with additional metadata.
/// </summary>
public class CategoryFacetBucket
{
    /// <summary>
    /// Category ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Category name (English).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Category name (Arabic).
    /// </summary>
    public string? NameAr { get; set; }

    /// <summary>
    /// Category slug.
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// Number of products matching this category.
    /// </summary>
    public long Count { get; set; }
}

/// <summary>
/// Price range facet bucket.
/// </summary>
public class PriceRangeBucket
{
    /// <summary>
    /// Display label (e.g., "Under $50", "50 - 100").
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Minimum price (inclusive).
    /// </summary>
    public decimal? From { get; set; }

    /// <summary>
    /// Maximum price (exclusive).
    /// </summary>
    public decimal? To { get; set; }

    /// <summary>
    /// Number of products in this range.
    /// </summary>
    public long Count { get; set; }
}

/// <summary>
/// Promotion facet showing counts for promoted vs non-promoted.
/// </summary>
public class PromotionFacet
{
    /// <summary>
    /// Count of products with active promotions.
    /// </summary>
    public long PromotedCount { get; set; }

    /// <summary>
    /// Count of products without active promotions.
    /// </summary>
    public long NonPromotedCount { get; set; }
}

/// <summary>
/// Stock availability facet.
/// </summary>
public class StockFacet
{
    /// <summary>
    /// Count of in-stock products.
    /// </summary>
    public long InStockCount { get; set; }

    /// <summary>
    /// Count of out-of-stock products.
    /// </summary>
    public long OutOfStockCount { get; set; }
}
