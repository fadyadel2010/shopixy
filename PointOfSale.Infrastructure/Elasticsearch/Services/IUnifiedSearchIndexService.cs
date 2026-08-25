using PointOfSale.Infrastructure.Elasticsearch.Documents;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

/// <summary>
/// Service interface for unified search index operations.
/// Handles indexing for Products, Categories, and CMS Pages.
/// 
/// CRITICAL: This service is for ADMIN/POS use only.
/// Storefront NEVER writes to Elasticsearch.
/// </summary>
public interface IUnifiedSearchIndexService
{
    #region Product Operations
    
    /// <summary>
    /// Indexes or updates a product document (upsert).
    /// </summary>
    Task<bool> IndexProductAsync(ProductSearchDocument document, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a product from the search index.
    /// </summary>
    Task<bool> DeleteProductAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retains a versioned product tombstone so delayed events cannot resurrect hidden state.
    /// </summary>
    Task<bool> TombstoneProductAsync(
        Guid tenantId,
        Guid productId,
        long productGeneration,
        bool isDeleted,
        Guid? eventId = null,
        CancellationToken cancellationToken = default);
    
    #endregion
    
    #region Category Operations
    
    /// <summary>
    /// Indexes or updates a category document (upsert).
    /// </summary>
    Task<bool> IndexCategoryAsync(CategorySearchDocument document, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a category from the search index.
    /// </summary>
    Task<bool> DeleteCategoryAsync(Guid tenantId, Guid categoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes tenant-scoped category documents that are missing from the current SQL authority set.
    /// </summary>
    Task<long> DeleteStaleCategoriesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> authoritativeCategoryIds,
        CancellationToken cancellationToken = default);
    
    #endregion
    
    #region CMS Page Operations
    
    /// <summary>
    /// Indexes or updates a CMS page document (upsert).
    /// </summary>
    Task<bool> IndexCmsPageAsync(CmsPageSearchDocument document, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a CMS page from the search index.
    /// </summary>
    Task<bool> DeleteCmsPageAsync(Guid tenantId, string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes tenant-scoped CMS page documents that are missing from the current SQL authority set.
    /// </summary>
    Task<long> DeleteStaleCmsPagesAsync(
        Guid tenantId,
        IReadOnlyCollection<string> authoritativeSlugs,
        CancellationToken cancellationToken = default);
    
    #endregion
    
    #region Unified Search
    
    /// <summary>
    /// Performs unified search across all entity types.
    /// </summary>
    /// <param name="tenantId">Tenant ID (REQUIRED)</param>
    /// <param name="query">Search query text</param>
    /// <param name="entityTypes">Optional filter by entity types (product, category, cms)</param>
    /// <param name="page">Page number (1-indexed)</param>
    /// <param name="pageSize">Items per page</param>
    /// <param name="language">Language code for search: en (English), ar (Arabic). Defaults to en.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Unified search result</returns>
    Task<UnifiedSearchResult> SearchAsync(
        Guid tenantId,
        string query,
        string[]? entityTypes = null,
        int page = 1,
        int pageSize = 20,
        string language = "en",
        CancellationToken cancellationToken = default);
    
    #endregion
    
    #region Header Search (Autocomplete)
    
    /// <summary>
    /// Performs SMART header search (autocomplete) optimized for:
    /// - Single character queries (edge n-gram)
    /// - Typo tolerance (fuzziness)
    /// - Arabic input support
    /// - Promotion-aware ranking (light boost)
    /// - Variant-aware price display
    /// 
    /// CRITICAL: This is separate from unified search. Header search:
    /// - Returns max 5-8 results
    /// - Uses autocomplete analyzer with edge n-grams
    /// - Has lighter promotion boosting
    /// - Never filters by entity type (products only)
    /// </summary>
    /// <param name="tenantId">Tenant ID (REQUIRED)</param>
    /// <param name="query">Search query text (min 1 character)</param>
    /// <param name="limit">Maximum results (default 5, max 8)</param>
    /// <param name="language">Language code: en or ar</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Header search result with product suggestions</returns>
    Task<HeaderSearchResult> HeaderSuggestAsync(
        Guid tenantId,
        string query,
        int limit = 5,
        string language = "en",
        CancellationToken cancellationToken = default);
    
    #endregion
    
    #region Index Management
    
    /// <summary>
    /// Ensures all required indices exist with proper mappings.
    /// </summary>
    Task<bool> EnsureIndicesExistAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Bulk indexes multiple product documents.
    /// </summary>
    Task<BulkIndexResult> BulkIndexProductsAsync(
        IEnumerable<ProductSearchDocument> documents, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Bulk indexes multiple category documents.
    /// </summary>
    Task<BulkIndexResult> BulkIndexCategoriesAsync(
        IEnumerable<CategorySearchDocument> documents, 
        CancellationToken cancellationToken = default);
    
    #endregion
}

/// <summary>
/// Result of unified search across all entity types.
/// </summary>
public class UnifiedSearchResult
{
    public List<UnifiedSearchItem> Items { get; set; } = new();
    public long TotalCount { get; set; }
    public long SearchTimeMs { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string Query { get; set; } = string.Empty;
    public int Page { get; set; }
    public int PageSize { get; set; }

    public static UnifiedSearchResult Success(
        List<UnifiedSearchItem> items, 
        long totalCount, 
        long searchTimeMs,
        string query,
        int page,
        int pageSize) => new()
    {
        Items = items,
        TotalCount = totalCount,
        SearchTimeMs = searchTimeMs,
        IsSuccess = true,
        Query = query,
        Page = page,
        PageSize = pageSize
    };

    public static UnifiedSearchResult Failure(string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// A single item in unified search results.
/// </summary>
public class UnifiedSearchItem
{
    public Guid TenantId { get; set; }
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public double? Price { get; set; }
    public string? CategoryName { get; set; }
    public double Score { get; set; }
}

/// <summary>
/// Result of bulk indexing operation.
/// </summary>
public class BulkIndexResult
{
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public long ElapsedMs { get; set; }
    public bool IsSuccess => FailureCount == 0;
}

// =========================================================================
// HEADER SEARCH (AUTOCOMPLETE) RESULT TYPES
// Phase: Smart Promotion-Aware Header Search
// =========================================================================

/// <summary>
/// Result of header search (autocomplete) query.
/// Optimized for quick suggestions, not full search results.
/// </summary>
public class HeaderSearchResult
{
    public List<HeaderSearchItem> Items { get; set; } = new();
    public string Query { get; set; } = string.Empty;
    public long SearchTimeMs { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }

    public static HeaderSearchResult Success(
        List<HeaderSearchItem> items, 
        string query, 
        long searchTimeMs) => new()
    {
        Items = items,
        Query = query,
        SearchTimeMs = searchTimeMs,
        IsSuccess = true
    };

    public static HeaderSearchResult Failure(string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// A single item in header search (autocomplete) results.
/// 
/// CRITICAL DESIGN DECISIONS:
/// - HasVariants: When true, displayPriceText shows "Multiple options" instead of price
/// - PromotionBadge: Pre-computed badge text (e.g., "20% OFF")
/// - Price is raw value; formatting is done by the API layer based on language
/// </summary>
public class HeaderSearchItem
{
    /// <summary>Product ID</summary>
    public Guid ProductId { get; set; }
    
    /// <summary>Product name (English)</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Product name (Arabic)</summary>
    public string? NameAr { get; set; }
    
    /// <summary>URL-friendly slug</summary>
    public string? Slug { get; set; }
    
    /// <summary>Whether product has variants (affects price display)</summary>
    public bool HasVariants { get; set; }
    
    /// <summary>
    /// Raw price value.
    /// When HasVariants=true, this may be 0 and should not be displayed.
    /// </summary>
    public double Price { get; set; }
    
    /// <summary>Whether product has an active promotion</summary>
    public bool HasActivePromotion { get; set; }
    
    /// <summary>
    /// Pre-computed promotion badge text (e.g., "20% OFF", "خصم 20%").
    /// Empty if no promotion.
    /// </summary>
    public string? PromotionBadge { get; set; }
    
    /// <summary>Discount percentage value (for display logic)</summary>
    public double PromotionDiscountValue { get; set; }
    
    /// <summary>Primary image URL</summary>
    public string? ImageUrl { get; set; }
    
    /// <summary>Category name for context</summary>
    public string? CategoryName { get; set; }
    
    /// <summary>Elasticsearch relevance score</summary>
    public double Score { get; set; }
}
