using PointOfSale.Core.DTOs;
using PointOfSale.Infrastructure.Elasticsearch.Documents;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

/// <summary>
/// Service interface for product search indexing operations.
/// 
/// Responsibilities:
/// - Index/update product documents
/// - Delete product documents
/// - Ensure index exists with proper mappings
/// 
/// NOT Responsible For:
/// - Search queries (future Search API will handle this)
/// - Aggregations/analytics
/// - Bulk operations (for now)
/// </summary>
public interface IProductSearchIndexService
{
    /// <summary>
    /// Indexes or updates a product document.
    /// Uses document ID = {TenantId}_{ProductId} for idempotent upserts.
    /// </summary>
    /// <param name="document">The product document to index</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if indexing succeeded, false otherwise</returns>
    Task<bool> IndexAsync(ProductSearchDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a product document from the index.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="productId">Product ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deletion succeeded or document didn't exist, false on error</returns>
    Task<bool> DeleteAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a durable, externally versioned tombstone for a product visibility removal.
    /// </summary>
    Task<bool> TombstoneAsync(
        Guid tenantId,
        Guid productId,
        long productGeneration,
        bool isDeleted,
        Guid? eventId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the products index exists with proper mappings.
    /// Called at application startup.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if index exists or was created successfully</returns>
    Task<bool> EnsureIndexExistsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches products in Elasticsearch with optional promotion-aware ranking.
    /// 
    /// Promotion-Aware Ranking (when promotionRankingConfig provided):
    /// - Uses function_score to boost promoted products
    /// - Bigger discounts rank higher than smaller discounts
    /// - Text relevance is preserved (additive boost, not multiplicative)
    /// 
    /// AF-12: Supports Affiliate Mode filtering.
    /// </summary>
    /// <param name="tenantId">Tenant ID for isolation</param>
    /// <param name="query">Search query text</param>
    /// <param name="page">Page number (1-indexed)</param>
    /// <param name="pageSize">Items per page</param>
    /// <param name="categoryId">Optional category filter</param>
    /// <param name="minPrice">Optional minimum price filter</param>
    /// <param name="maxPrice">Optional maximum price filter</param>
    /// <param name="promotionRankingConfig">Optional promotion ranking config for boosting</param>
    /// <param name="affiliateModeOnly">If true, only returns IsAffiliateEnabled=true products</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Search result with product IDs, total count, and search time</returns>
    Task<ProductSearchResult> SearchAsync(
        Guid tenantId,
        string query,
        int page,
        int pageSize,
        Guid? categoryId = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        PointOfSale.Core.Entities.PromotionRankingConfig? promotionRankingConfig = null,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets best-selling product IDs for homepage display.
    /// 
    /// NOTE: In Phase 2.5, this implementation returns most recently created products
    /// as a placeholder until order/sales analytics data is available in Elasticsearch.
    /// Future implementation will use aggregated sales data to determine best sellers.
    /// 
    /// AF-12: Supports Affiliate Mode filtering.
    /// </summary>
    /// <param name="tenantId">Tenant ID for isolation</param>
    /// <param name="limit">Maximum number of products to return</param>
    /// <param name="affiliateModeOnly">If true, only returns IsAffiliateEnabled=true products</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Search result with product IDs</returns>
    Task<ProductSearchResult> GetBestSellersAsync(
        Guid tenantId,
        int limit,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // REINDEX OPERATIONS (For Promotion-Aware Search Pre-Experiment Checklist)
    // =========================================================================

    /// <summary>
    /// Deletes the products index to force fresh mappings on recreation.
    /// Use this before ReindexAllAsync to ensure new fields are properly mapped.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if index was deleted or didn't exist</returns>
    Task<bool> DeleteIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-indexes all products for a tenant from SQL-authoritative projection documents to Elasticsearch.
    /// Used after schema changes to ensure all products have new fields.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of products reindexed</returns>
    Task<int> ReindexAllAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of Elasticsearch product search.
/// Contains only IDs - hydration happens in MongoDB.
/// </summary>
public class ProductSearchResult
{
    /// <summary>
    /// Product IDs in relevance order.
    /// </summary>
    public List<Guid> ProductIds { get; set; } = new();

    /// <summary>
    /// Total number of matching documents.
    /// </summary>
    public long TotalCount { get; set; }

    /// <summary>
    /// Time taken for the search in milliseconds.
    /// </summary>
    public long SearchTimeMs { get; set; }

    /// <summary>
    /// Whether the search was successful.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if search failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    public static ProductSearchResult Success(List<Guid> productIds, long totalCount, long searchTimeMs) =>
        new()
        {
            ProductIds = productIds,
            TotalCount = totalCount,
            SearchTimeMs = searchTimeMs,
            IsSuccess = true
        };

    public static ProductSearchResult Failure(string errorMessage) =>
        new()
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
}
