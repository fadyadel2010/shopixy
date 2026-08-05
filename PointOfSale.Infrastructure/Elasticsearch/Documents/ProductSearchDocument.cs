using Nest;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.ReadModels.Evolution;

namespace PointOfSale.Infrastructure.Elasticsearch.Documents;

/// <summary>
/// Elasticsearch document for product search and filtering.
/// 
/// ARCHITECTURE: Shopify-Grade Multilingual Search with Index-Time Enrichment
/// ===========================================================================
/// 
/// Core Principle:
/// - Search engines do NOT invent language knowledge
/// - They only match what you index
/// - Therefore, we enrich searchable text at INDEX TIME
/// 
/// How Cross-Language Search Works:
/// 1. SearchEnrichment expands brand keywords at index time
/// 2. "iPhone 16 Pro" becomes "iPhone 16 Pro iphone ايفون apple ابل pro برو"
/// 3. Now both "iphone" and "ايفون" queries match this product
/// 
/// Key Fields:
/// - Name: Original product name (for display)
/// - SearchText: THE POWER FIELD - enriched with brand keywords
/// 
/// Analyzers Handle:
/// - arabic_normalization (Alef variants, taa marbuta, tatweel removal)
/// - ASCII folding for Latin diacritics (café → cafe)
/// - Autocomplete (edge n-gram from 1 char)
/// - Fuzziness (typos within same language)
/// 
/// NO AI, NO runtime translation, NO external services.
/// </summary>
[ElasticsearchType(RelationName = "product")]
public class ProductSearchDocument : IReadModelEvolutionDocument
{
    public const int CurrentReadModelSchemaVersion = ReadModelEvolutionRegistry.StorefrontProductSearchSchemaVersion;
    public const int CurrentProjectionVersion = ReadModelEvolutionRegistry.StorefrontProductSearchProjectionVersion;
    public const int CurrentIndexVersion = ReadModelEvolutionRegistry.StorefrontProductSearchIndexVersion;

    /// <summary>
    /// Stable read model identity used by the evolution framework.
    /// </summary>
    [Keyword]
    public string ReadModelName { get; set; } = ReadModelEvolutionRegistry.StorefrontProduct;

    /// <summary>
    /// Shape version of this Elasticsearch document. Query strictness is gated on convergence.
    /// </summary>
    [Number(NumberType.Integer)]
    public int ReadModelSchemaVersion { get; set; } = CurrentReadModelSchemaVersion;

    /// <summary>
    /// Version of the projection logic that wrote this search document.
    /// </summary>
    [Number(NumberType.Integer)]
    public int ProjectionVersion { get; set; } = CurrentProjectionVersion;

    /// <summary>
    /// Set by convergence certification once all documents for this read model are safely upgraded.
    /// </summary>
    [Date(Format = "strict_date_optional_time||epoch_millis")]
    public DateTime? ProjectionConvergedAtUtc { get; set; }

    /// <summary>
    /// Tenant that owns this product.
    /// CRITICAL: All queries MUST filter by TenantId.
    /// </summary>
    [Keyword]
    public Guid TenantId { get; set; }

    /// <summary>
    /// Product ID from PostgreSQL (source of truth).
    /// </summary>
    [Keyword]
    public Guid ProductId { get; set; }

    /// <summary>
    /// Entity type for unified search.
    /// </summary>
    [Keyword]
    public string EntityType { get; set; } = "product";

    /// <summary>
    /// Product name - THE PRIMARY DISPLAY AND SEARCH FIELD.
    /// 
    /// Can be in any language (English, Arabic, or mixed).
    /// Mapped with multi-fields for different analyzers:
    /// - name (multilingual_analyzer): Standard tokenization + arabic_normalization + ASCII folding
    /// - name.autocomplete: Edge n-gram for prefix matching
    /// - name.arabic: Arabic-specific analyzer
    /// - name.keyword: Exact match sorting
    /// </summary>
    [Text(Analyzer = "multilingual_analyzer")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional Arabic name for DISPLAY purposes.
    /// If provided, it will also be included in SearchText.
    /// This is NOT required - admin can choose to enter Arabic in the main Name field.
    /// </summary>
    [Text(Analyzer = "arabic")]
    public string? NameAr { get; set; }

    /// <summary>
    /// Stock Keeping Unit - unique product identifier.
    /// Keyword only for exact match filtering.
    /// </summary>
    [Keyword]
    public string? SKU { get; set; }

    /// <summary>
    /// Barcode for scanning.
    /// Keyword only for exact match filtering.
    /// </summary>
    [Keyword]
    public string? Barcode { get; set; }

    /// <summary>
    /// Category ID reference.
    /// Keyword for filtering by category.
    /// </summary>
    [Keyword]
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// Denormalized category name for display and search.
    /// Included in SearchText for category-based discovery.
    /// </summary>
    [Text(Analyzer = "multilingual_analyzer")]
    public string? CategoryName { get; set; }

    /// <summary>
    /// Product sale price.
    /// Double for range queries and sorting.
    /// </summary>
    [Number(NumberType.Double)]
    public double Price { get; set; }

    /// <summary>
    /// Whether the product is active in the system.
    /// </summary>
    [Boolean]
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether the product is visible on e-commerce storefront.
    /// </summary>
    [Boolean]
    public bool IsOnlineVisible { get; set; }

    /// <summary>
    /// Whether the product is eligible for storefront search and discovery.
    /// </summary>
    [Boolean]
    public bool IsSearchable { get; set; }

    /// <summary>
    /// SEO-friendly URL slug.
    /// </summary>
    [Keyword]
    public string? Slug { get; set; }

    /// <summary>
    /// Product-specific storefront listing layout preference.
    /// Backed by the existing ProductTemplate value in MongoDB.
    /// </summary>
    [Keyword]
    public string? StorefrontLayout { get; set; }

    /// <summary>
    /// Whether the product is featured on curated storefront surfaces.
    /// </summary>
    [Boolean]
    public bool IsFeatured { get; set; }

    /// <summary>
    /// When the product was created in PostgreSQL.
    /// </summary>
    [Date(Format = "strict_date_optional_time||epoch_millis")]
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When this document was last indexed.
    /// </summary>
    [Date(Format = "strict_date_optional_time||epoch_millis")]
    public DateTime IndexedAtUtc { get; set; }

    /// <summary>
    /// The EventId that triggered this indexing (for debugging/tracing).
    /// </summary>
    [Keyword]
    public Guid? LastEventId { get; set; }

    /// <summary>
    /// Product description for search.
    /// Included in SearchText for content-based discovery.
    /// </summary>
    [Text(Analyzer = "multilingual_analyzer")]
    public string? Description { get; set; }

    /// <summary>
    /// THE POWER FIELD - Aggregated searchable content.
    /// 
    /// Built at index time from:
    /// - Name (primary)
    /// - NameAr (if provided)
    /// - Description
    /// - CategoryName
    /// - SKU
    /// 
    /// This field uses multilingual_analyzer which includes:
    /// - arabic_normalization (Alef variants, taa marbuta, tatweel removal)
    /// - ASCII folding for Latin diacritics
    /// - Lowercase folding
    /// 
    /// CRITICAL: This is the main field searched with highest boost.
    /// </summary>
    [Text(Analyzer = "multilingual_analyzer")]
    public string SearchText { get; set; } = string.Empty;

    // =========================================================================
    // PROMOTION-AWARE SEARCH RANKING FIELDS
    // These fields enable function_score boosting in Elasticsearch
    // =========================================================================

    /// <summary>
    /// Whether this product has an active promotion.
    /// Used as primary boost trigger in function_score.
    /// </summary>
    [Boolean]
    public bool HasActivePromotion { get; set; }

    /// <summary>
    /// Discount value for tiered boosting.
    /// </summary>
    [Number(NumberType.Double)]
    public double PromotionDiscountValue { get; set; }

    /// <summary>
    /// Discount type: "percentage" or "fixed".
    /// </summary>
    [Keyword]
    public string? PromotionDiscountType { get; set; }

    /// <summary>
    /// Active promotion ID for traceability.
    /// </summary>
    [Keyword]
    public Guid? ActivePromotionId { get; set; }

    /// <summary>
    /// When the active promotion started.
    /// Used for Gaussian time decay in function_score.
    /// </summary>
    [Date(Format = "strict_date_optional_time||epoch_millis")]
    public DateTime? PromotionStartDate { get; set; }

    /// <summary>
    /// When the active promotion ends.
    /// </summary>
    [Date(Format = "strict_date_optional_time||epoch_millis")]
    public DateTime? PromotionEndDate { get; set; }

    /// <summary>
    /// Manual boost override from Promotion.SearchBoostOverride.
    /// </summary>
    [Number(NumberType.Double)]
    public double SearchBoostOverride { get; set; }

    // =========================================================================
    // PRODUCT DISCOVERY & FILTERING FIELDS
    // Phase: Elasticsearch-Powered Product Discovery
    // =========================================================================

    /// <summary>
    /// Brand name for filtering.
    /// CRITICAL: Uses keyword type for exact match filtering and aggregations.
    /// </summary>
    [Keyword]
    public string? Brand { get; set; }

    /// <summary>
    /// Whether this product is in stock.
    /// Computed: true if stockQuantity > 0 OR any variant has stock.
    /// </summary>
    [Boolean]
    public bool IsInStock { get; set; }

    /// <summary>
    /// Total stock quantity across all variants/warehouses.
    /// Used for sorting and display, not filtering.
    /// </summary>
    [Number(NumberType.Integer)]
    public int StockQuantity { get; set; }

    // =========================================================================
    // HEADER SEARCH (AUTOCOMPLETE) FIELDS
    // =========================================================================

    /// <summary>
    /// Whether this product has variants.
    /// When true, header search displays "Multiple options" instead of price.
    /// </summary>
    [Boolean]
    public bool HasVariants { get; set; }

    /// <summary>
    /// Canonical lower-case variant attribute pairs (for example color=red).
    /// Catalog Runtime owns filtering; this field is a derivative candidate index.
    /// </summary>
    [Keyword]
    public List<string> VariantAttributes { get; set; } = new();

    /// <summary>
    /// Primary image URL for search result display.
    /// </summary>
    [Keyword]
    public string? ImageUrl { get; set; }

    // =========================================================================
    // AFFILIATE MARKETING FIELDS (AF-5)
    // SECURITY: Only IsAffiliateEnabled is indexed. Commission rate is NEVER
    // stored in Elasticsearch or exposed in search results.
    // =========================================================================

    /// <summary>
    /// Whether affiliate marketing is enabled for this product.
    /// Used as a hard filter in affiliate discovery queries.
    /// </summary>
    [Boolean]
    public bool IsAffiliateEnabled { get; set; }

    /// <summary>
    /// When affiliate was enabled (for sorting: newest affiliate products first).
    /// </summary>
    [Date(Format = "strict_date_optional_time||epoch_millis")]
    public DateTime? AffiliateEnabledAtUtc { get; set; }

    /// <summary>
    /// Generates the document ID for Elasticsearch.
    /// Uses composite key: {TenantId}_{ProductId} for tenant isolation and idempotency.
    /// </summary>
    public string GetDocumentId() => $"{TenantId}_{ProductId}";

    /// <summary>
    /// Builds the SearchText power field from all searchable content.
    /// Called at index time to aggregate all text for unified search.
    /// 
    /// CRITICAL: Uses SearchEnrichment to expand brand keywords:
    /// - "iPhone 16 Pro" becomes "iPhone 16 Pro iphone ايفون apple ابل pro برو"
    /// 
    /// This enables cross-language search without runtime translation.
    /// </summary>
    public void BuildSearchText()
    {
        SearchText = SearchEnrichment.BuildSearchText(
            name: Name,
            nameAr: NameAr,
            description: Description,
            categoryName: CategoryName,
            sku: SKU,
            barcode: Barcode,
            brand: Brand
        );
    }
}
