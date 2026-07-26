using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin controller for Elasticsearch reindex operations.
/// 
/// ARCHITECTURE: Shopify-Grade Multilingual Search
/// ================================================
/// 
/// These endpoints are used to:
/// 1. Delete and recreate the ES index with fresh mappings
/// 2. Reindex all products from current SQL authority into ES
/// 3. Clear Redis caches for promotion/search data
/// 
/// Key Principle: Search intelligence lives in Elasticsearch analyzers.
/// NO translation dictionaries. NO data duplication.
/// </summary>
[ApiController]
[Route("api/admin/search")]
[Authorize(Roles = "Owner,Admin")]
public class SearchReindexController : ControllerBase
{
    private readonly IProductSearchIndexService _indexService;
    private readonly IMongoCollection<ProductDocument> _productCollection;
    private readonly ApplicationDbContext _dbContext;
    private readonly IStoreConfigCacheService _storeConfigCache;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<SearchReindexController> _logger;

    private const string ProductsCollectionName = "products";

    public SearchReindexController(
        IProductSearchIndexService indexService,
        IMongoDatabase database,
        ApplicationDbContext dbContext,
        IStoreConfigCacheService storeConfigCache,
        ITenantContext tenantContext,
        ILogger<SearchReindexController> logger)
    {
        _indexService = indexService;
        _productCollection = database.GetCollection<ProductDocument>(ProductsCollectionName);
        _dbContext = dbContext;
        _storeConfigCache = storeConfigCache;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Delete the products index to force fresh mappings.
    /// After calling this, restart the API to recreate the index with new mappings.
    /// </summary>
    [HttpDelete("index")]
    public async Task<IActionResult> DeleteIndex(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        _logger.LogWarning(
            "Blocked tenant admin Elasticsearch index deletion request | TenantId: {TenantId} | User: {User}",
            _tenantContext.TenantId,
            User.Identity?.Name ?? "Unknown");

        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            success = false,
            code = "GLOBAL_SEARCH_CONTROL_FORBIDDEN",
            message = "Global index deletion is not available from tenant admin scope. Use tenant-scoped reindex or a reviewed platform recovery runbook."
        });
    }

    /// <summary>
    /// Recreate the index with fresh mappings (combines delete + ensure exists).
    /// </summary>
    [HttpPost("recreate-index")]
    public async Task<IActionResult> RecreateIndex(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        _logger.LogWarning(
            "Blocked tenant admin Elasticsearch index recreation request | TenantId: {TenantId} | User: {User}",
            _tenantContext.TenantId,
            User.Identity?.Name ?? "Unknown");

        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            success = false,
            code = "GLOBAL_SEARCH_CONTROL_FORBIDDEN",
            message = "Global index recreation is not available from tenant admin scope. Use tenant-scoped reindex or a reviewed platform recovery runbook."
        });
    }

    // =========================================================================
    // SECURITY AUDIT: Anonymous /reindex-dev endpoint PERMANENTLY REMOVED
    // 
    // Removed on: 2026-01-19
    // Reason: Critical security vulnerability - allowed unauthenticated users
    //         to trigger full Elasticsearch reindex operations for any tenant.
    // 
    // USE INSTEAD:
    //   POST /api/admin/search/reindex
    //   Requires: [Authorize(Roles = "Owner,Admin")]
    //   Headers:  Authorization: Bearer <token>, X-Tenant-Id: <tenant-id>
    // 
    // This comment serves as permanent documentation that the dev endpoint
    // was intentionally removed for production security compliance.
    // =========================================================================

    /// <summary>
    /// Reindex all tenant products from current SQL authority into Elasticsearch.
    /// MongoDB documents are used only as projection candidates before SQL recomposition.
    /// </summary>
    [HttpPost("reindex")]
    public async Task<IActionResult> ReindexProducts(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        _logger.LogWarning(
            "Admin requested full product reindex | TenantId: {TenantId} | User: {User}",
            tenantId, User.Identity?.Name ?? "Unknown");

        try
        {
            // Ensure index exists with correct mappings
            await _indexService.EnsureIndexExistsAsync(cancellationToken);

            var indexedCount = 0;
            var errorCount = 0;
            var promotedCount = 0;
            var productIds = await _dbContext.Products
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(product => product.TenantId == tenantId)
                .OrderBy(product => product.CreatedAt)
                .ThenBy(product => product.Id)
                .Select(product => product.Id)
                .ToListAsync(cancellationToken);

            foreach (var productId in productIds)
            {
                try
                {
                    var sqlProjection = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                        _dbContext,
                        new ProductDocument
                        {
                            TenantId = tenantId,
                            ProductId = productId
                        },
                        tenantId,
                        cancellationToken);

                    if (sqlProjection == null)
                    {
                        _logger.LogWarning(
                            "Deleting product from search during SQL-first reindex because SQL authority no longer permits visibility | ProductId: {ProductId} | TenantId: {TenantId}",
                            productId,
                            tenantId);

                        if (!await _indexService.DeleteAsync(tenantId, productId, cancellationToken))
                        {
                            errorCount++;
                        }

                        continue;
                    }

                    if (!sqlProjection.IsActive || !sqlProjection.IsOnlineVisible)
                    {
                        if (!await _indexService.DeleteAsync(tenantId, sqlProjection.ProductId, cancellationToken))
                        {
                            errorCount++;
                        }

                        continue;
                    }

                    var document = MapToSearchDocument(sqlProjection);
                    var success = await _indexService.IndexAsync(document, cancellationToken);

                    if (success)
                    {
                        indexedCount++;
                        if (document.HasActivePromotion)
                        {
                            promotedCount++;
                        }
                    }
                    else
                    {
                        errorCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to reindex product from SQL authority | ProductId: {ProductId}",
                        productId);
                    errorCount++;
                }
            }

            _logger.LogInformation(
                "Reindex completed | TenantId: {TenantId} | Indexed: {Indexed} | WithPromotion: {Promoted} | Errors: {Errors}",
                tenantId, indexedCount, promotedCount, errorCount);

            return Ok(new
            {
                success = true,
                tenantId,
                indexedProducts = indexedCount,
                productsWithPromotion = promotedCount,
                errors = errorCount,
                message = errorCount == 0 
                    ? "All products reindexed successfully" 
                    : $"Reindex completed with {errorCount} errors"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reindex failed | TenantId: {TenantId}", tenantId);
            return StatusCode(500, new
            {
                success = false,
                message = "An unexpected error occurred"
            });
        }
    }

    /// <summary>
    /// Clear all promotion and search caches from Redis.
    /// </summary>
    [HttpPost("clear-cache")]
    public async Task<IActionResult> ClearCache(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        _logger.LogWarning(
            "Admin requested cache clear | TenantId: {TenantId} | User: {User}",
            tenantId, User.Identity?.Name ?? "Unknown");

        try
        {
            // Clear store config cache (includes PromotionRankingConfig)
            await _storeConfigCache.InvalidateStoreConfigAsync(tenantId, cancellationToken);

            _logger.LogInformation(
                "Cache cleared for tenant | TenantId: {TenantId}",
                tenantId);

            return Ok(new
            {
                success = true,
                tenantId,
                message = "Cache cleared successfully. Fresh config will be loaded on next request.",
                clearedCaches = new[]
                {
                    "StoreConfig (including PromotionRankingConfig)",
                    "Promotion badges will refresh on next product fetch"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache clear failed | TenantId: {TenantId}", tenantId);
            return StatusCode(500, new
            {
                success = false,
                message = "An unexpected error occurred"
            });
        }
    }

    /// <summary>
    /// Get current Elasticsearch index status and promotion ranking config.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        try
        {
            // Count products with promotions in MongoDB
            var promotedFilter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(x => x.HasActivePromotion, true)
            );
            var promotedCount = await _productCollection.CountDocumentsAsync(promotedFilter, cancellationToken: cancellationToken);

            // Get total product count
            var totalFilter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(x => x.IsActive, true),
                Builders<ProductDocument>.Filter.Eq(x => x.IsOnlineVisible, true)
            );
            var totalCount = await _productCollection.CountDocumentsAsync(totalFilter, cancellationToken: cancellationToken);

            return Ok(new
            {
                tenantId,
                mongoDb = new
                {
                    totalOnlineProducts = totalCount,
                    productsWithActivePromotion = promotedCount
                },
                preExperimentChecklist = new
                {
                    step1_RecreateIndex = "POST /api/admin/search/recreate-index",
                    step2_ReindexProducts = "POST /api/admin/search/reindex",
                    step3_ClearCache = "POST /api/admin/search/clear-cache"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Status check failed | TenantId: {TenantId}", tenantId);
            return StatusCode(500, new { error = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// Maps MongoDB ProductDocument to Elasticsearch ProductSearchDocument.
    /// 
    /// ARCHITECTURE: Search intelligence is in Elasticsearch analyzers.
    /// - NO translation dictionaries
    /// - NO data duplication
    /// - SearchText is built from available content (name + nameAr + description + category + SKU + brand)
    /// - ICU normalization handles Arabic character variants
    /// 
    /// PHASE: Product Discovery - All fields required for filtering/faceting
    /// </summary>
    private static ProductSearchDocument MapToSearchDocument(ProductDocument product)
    {
        return ProductSearchDocumentMapper.MapFromProductDocument(product);
    }

}
