using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.Catalog;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin Search Sync Controller - Endpoints for syncing data to Elasticsearch.
/// 
/// Route: /api/admin/sync/search
/// 
/// CRITICAL RULES:
/// - These endpoints are ADMIN-ONLY
/// - Storefront NEVER calls these endpoints
/// - Used for initial data population or recovery
/// - Regular sync is via MassTransit events
/// 
/// WARNING: These are expensive operations. Use sparingly.
/// </summary>
[ApiController]
[Route("api/admin/sync/search")]
public class AdminSearchSyncController : ControllerBase
{
    private readonly ApplicationDbContext _sqlContext;
    private readonly IUnifiedSearchIndexService _searchIndexService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminSearchSyncController> _logger;
    private const int SearchSyncBatchSize = 250;
    private const int MaxReportedErrors = 10;

    public AdminSearchSyncController(
        ApplicationDbContext sqlContext,
        IUnifiedSearchIndexService searchIndexService,
        ITenantContext tenantContext,
        ILogger<AdminSearchSyncController> logger)
    {
        _sqlContext = sqlContext;
        _searchIndexService = searchIndexService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Syncs all active, online-visible products from PostgreSQL to Elasticsearch.
    /// This is for initial data population or disaster recovery.
    /// 
    /// IMPORTANT - SQL-FIRST RECOVERY ONLY:
    /// ===================================
    /// This endpoint recomposes product visibility from the current SQL search
    /// authority overlay before mutating Elasticsearch.
    /// It must not manufacture search visibility from partial SQL rows, stale
    /// Mongo documents, or historical repair snapshots.
    /// </summary>
    [HttpPost("products")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SyncProducts(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var tenantId = _tenantContext.RequiredTenantId;

        _logger.LogInformation(
            "SyncProducts to Elasticsearch starting | TenantId: {TenantId}",
            tenantId);

        try
        {
            var totals = new SyncAccumulator();
            var batch = new List<ProductSearchDocument>(SearchSyncBatchSize);
            var productIds = await _sqlContext.Products
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(product => product.TenantId == tenantId)
                .OrderBy(product => product.CreatedAt)
                .ThenBy(product => product.Id)
                .Select(product => product.Id)
                .ToListAsync(cancellationToken);

            foreach (var productId in productIds)
            {
                var document = await LoadProductSearchDocumentFromSqlAsync(
                    tenantId,
                    productId,
                    cancellationToken);

                if (document == null)
                {
                    _logger.LogInformation(
                        "Deleting product search visibility during admin SQL-first sync because current SQL authority no longer permits it | TenantId: {TenantId} | ProductId: {ProductId}",
                        tenantId,
                        productId);

                    await DeleteProductFromSearchAsync(
                        tenantId,
                        productId,
                        totals,
                        cancellationToken);
                    continue;
                }

                batch.Add(document);

                if (batch.Count < SearchSyncBatchSize)
                {
                    continue;
                }

                await FlushProductBatchAsync(
                    tenantId,
                    batch,
                    totals,
                    cancellationToken);
            }

            if (batch.Count > 0)
            {
                await FlushProductBatchAsync(
                    tenantId,
                    batch,
                    totals,
                    cancellationToken);
            }

            stopwatch.Stop();

            if (totals.TotalCount == 0)
            {
                return Ok(new
                {
                    message = "No products found in PostgreSQL to sync",
                    tenantId = tenantId,
                    count = 0
                });
            }

            _logger.LogInformation(
                "SyncProducts to Elasticsearch completed | TenantId: {TenantId} | Success: {Success} | Failed: {Failed} | TimeMs: {TimeMs}",
                tenantId, totals.SuccessCount, totals.FailureCount, stopwatch.ElapsedMilliseconds);

            return Ok(new
            {
                message = "Products synced to Elasticsearch",
                tenantId = tenantId,
                totalProducts = totals.TotalCount,
                successCount = totals.SuccessCount,
                failureCount = totals.FailureCount,
                deletedFromSearch = totals.DeletedCount,
                errors = totals.Errors,
                elapsedMs = stopwatch.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "SyncProducts to Elasticsearch failed | TenantId: {TenantId} | TimeMs: {TimeMs}",
                tenantId, stopwatch.ElapsedMilliseconds);

            return StatusCode(500, new
            {
                error = "Product sync failed",
                message = "An unexpected error occurred",
                tenantId = tenantId
            });
        }
    }

    /// <summary>
    /// Syncs all active, online-visible categories from PostgreSQL to Elasticsearch.
    /// </summary>
    [HttpPost("categories")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SyncCategories(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var tenantId = _tenantContext.RequiredTenantId;
        _ = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.ProjectionRepair,
            tenantId,
            tenantId,
            SqlAuthorityOverlayKind.Category);

        _logger.LogInformation(
            "SyncCategories to Elasticsearch starting | TenantId: {TenantId}",
            tenantId);

        try
        {
            var totals = new SyncAccumulator();
            var batch = new List<CategorySearchDocument>(SearchSyncBatchSize);
            var lineageSnapshot = await CategoryLineageAuthority.LoadTenantSnapshotAsync(
                _sqlContext,
                tenantId,
                cancellationToken);

            foreach (var row in lineageSnapshot.Rows.OrderBy(row => row.CategoryId))
            {
                if (!lineageSnapshot.IsLiveInLineage(row.CategoryId))
                {
                    await DeleteCategoryFromSearchAsync(
                        tenantId,
                        row.CategoryId,
                        totals,
                        cancellationToken);
                    continue;
                }

                batch.Add(MapToCategorySearchDocument(row));

                if (batch.Count < SearchSyncBatchSize)
                {
                    continue;
                }

                await FlushCategoryBatchAsync(
                    tenantId,
                    batch,
                    totals,
                    cancellationToken);
            }

            if (batch.Count > 0)
            {
                await FlushCategoryBatchAsync(
                    tenantId,
                    batch,
                    totals,
                    cancellationToken);
            }

            totals.StaleDeletedCount += (int)await _searchIndexService.DeleteStaleCategoriesAsync(
                tenantId,
                lineageSnapshot.Rows.Select(row => row.CategoryId).ToList(),
                cancellationToken);

            stopwatch.Stop();

            if (totals.TotalCount == 0)
            {
                return Ok(new
                {
                    message = "No categories found in PostgreSQL to sync",
                    tenantId = tenantId,
                    count = 0,
                    staleDeletedFromSearch = totals.StaleDeletedCount
                });
            }

            _logger.LogInformation(
                "SyncCategories to Elasticsearch completed | TenantId: {TenantId} | Success: {Success} | Failed: {Failed} | Deleted: {Deleted} | StaleDeleted: {StaleDeleted} | TimeMs: {TimeMs}",
                tenantId,
                totals.SuccessCount,
                totals.FailureCount,
                totals.DeletedCount,
                totals.StaleDeletedCount,
                stopwatch.ElapsedMilliseconds);

            return Ok(new
            {
                message = "Categories synced to Elasticsearch",
                tenantId = tenantId,
                totalCategories = totals.TotalCount,
                successCount = totals.SuccessCount,
                failureCount = totals.FailureCount,
                deletedFromSearch = totals.DeletedCount + totals.StaleDeletedCount,
                staleDeletedFromSearch = totals.StaleDeletedCount,
                errors = totals.Errors,
                elapsedMs = stopwatch.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "SyncCategories to Elasticsearch failed | TenantId: {TenantId}",
                tenantId);

            return StatusCode(500, new
            {
                error = "Category sync failed",
                message = "An unexpected error occurred",
                tenantId = tenantId
            });
        }
    }

    /// <summary>
    /// Syncs all published CMS pages from PostgreSQL to Elasticsearch.
    /// </summary>
    [HttpPost("pages")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SyncCmsPages(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var tenantId = _tenantContext.RequiredTenantId;
        _ = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.ProjectionRepair,
            tenantId,
            tenantId,
            SqlAuthorityOverlayKind.StorefrontCms);

        _logger.LogInformation(
            "SyncCmsPages to Elasticsearch starting | TenantId: {TenantId}",
            tenantId);

        try
        {
            var tenantIsActive = await _sqlContext.Tenants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .AnyAsync(
                    tenant => tenant.Id == tenantId &&
                              tenant.IsActive &&
                              tenant.Status == PointOfSale.Core.Enums.TenantStatus.Active,
                    cancellationToken);

            if (!tenantIsActive)
            {
                var staleDeleted = await _searchIndexService.DeleteStaleCmsPagesAsync(
                    tenantId,
                    Array.Empty<string>(),
                    cancellationToken);

                return Ok(new
                {
                    message = "Tenant is not active; CMS search visibility deleted",
                    tenantId = tenantId,
                    indexed = 0,
                    staleDeletedFromSearch = staleDeleted
                });
            }

            var publishedPages = await _sqlContext.CmsPages
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(page => page.TenantId == tenantId)
                .Where(page => !page.IsDeleted)
                .Where(page => page.IsPublished && !page.IsDraft)
                .Where(page => page.PublishedAt != null)
                .OrderBy(page => page.Slug)
                .ThenByDescending(page => page.Version)
                .ThenByDescending(page => page.PublishedAt)
                .ToListAsync(cancellationToken);

            var authoritativePages = publishedPages
                .GroupBy(page => page.PageGroupId)
                .Select(group => group
                    .OrderByDescending(page => page.Version)
                    .ThenByDescending(page => page.PublishedAt)
                    .First())
                .ToList();

            var totals = new SyncAccumulator();
            foreach (var page in authoritativePages)
            {
                totals.TotalCount++;
                var document = MapToCmsPageSearchDocument(page);
                if (await _searchIndexService.IndexCmsPageAsync(document, cancellationToken))
                {
                    totals.SuccessCount++;
                    continue;
                }

                totals.FailureCount++;
                AppendErrors(totals.Errors, new[] { $"Failed to index CMS page search document {tenantId}_{page.Slug}" });
            }

            totals.StaleDeletedCount = (int)await _searchIndexService.DeleteStaleCmsPagesAsync(
                tenantId,
                authoritativePages.Select(page => page.Slug).ToArray(),
                cancellationToken);

            stopwatch.Stop();

            return Ok(new
            {
                message = "CMS pages synced to Elasticsearch from current SQL authority",
                tenantId = tenantId,
                totalPages = totals.TotalCount,
                successCount = totals.SuccessCount,
                failureCount = totals.FailureCount,
                staleDeletedFromSearch = totals.StaleDeletedCount,
                errors = totals.Errors,
                elapsedMs = stopwatch.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "SyncCmsPages to Elasticsearch failed | TenantId: {TenantId}",
                tenantId);

            return StatusCode(500, new
            {
                error = "CMS page sync failed",
                message = "An unexpected error occurred",
                tenantId = tenantId
            });
        }
    }

    /// <summary>
    /// Ensures all search indices exist with proper mappings.
    /// Use this to initialize or repair Elasticsearch indices.
    /// </summary>
    [HttpPost("ensure-indices")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> EnsureIndices(CancellationToken cancellationToken)
    {
        _logger.LogInformation("EnsureIndices starting");

        try
        {
            var success = await _searchIndexService.EnsureIndicesExistAsync(cancellationToken);

            if (success)
            {
                _logger.LogInformation("EnsureIndices completed successfully");
                return Ok(new
                {
                    message = "All search indices verified/created successfully",
                    indices = new[] { "products", "categories", "cmspages" }
                });
            }

            return StatusCode(500, new
            {
                error = "One or more indices could not be created",
                message = "Check logs for details"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EnsureIndices failed");
            return StatusCode(500, new
            {
                error = "Index initialization failed",
                message = "An unexpected error occurred"
            });
        }
    }

    /// <summary>
    /// Full sync of all searchable entities for a tenant.
    /// WARNING: This is an expensive operation.
    /// </summary>
    [HttpPost("full")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> FullSync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var tenantId = _tenantContext.RequiredTenantId;

        _logger.LogWarning(
            "Full Elasticsearch sync starting | TenantId: {TenantId} | WARNING: This is an expensive operation",
            tenantId);

        try
        {
            // Ensure indices exist
            await _searchIndexService.EnsureIndicesExistAsync(cancellationToken);

            var results = new Dictionary<string, object>();

            // Sync products
            var productsResponse = await SyncProducts(cancellationToken) as OkObjectResult;
            results["products"] = productsResponse?.Value ?? new { error = "Failed" };

            // Sync categories
            var categoriesResponse = await SyncCategories(cancellationToken) as OkObjectResult;
            results["categories"] = categoriesResponse?.Value ?? new { error = "Failed" };

            // Sync CMS pages
            var cmsPagesResponse = await SyncCmsPages(cancellationToken) as OkObjectResult;
            results["cmsPages"] = cmsPagesResponse?.Value ?? new { error = "Failed" };

            stopwatch.Stop();

            _logger.LogInformation(
                "Full Elasticsearch sync completed | TenantId: {TenantId} | TimeMs: {TimeMs}",
                tenantId, stopwatch.ElapsedMilliseconds);

            return Ok(new
            {
                message = "Full sync completed",
                tenantId = tenantId,
                elapsedMs = stopwatch.ElapsedMilliseconds,
                results
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Full sync failed | TenantId: {TenantId}", tenantId);

            return StatusCode(500, new
            {
                error = "Full sync failed",
                message = "An unexpected error occurred",
                tenantId = tenantId
            });
        }
    }

    private static string GenerateSlug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "item";

        var slug = name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("&", "and");

        return System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\-]", "");
    }

    private async Task<ProductSearchDocument?> LoadProductSearchDocumentFromSqlAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var sqlProjection = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            _sqlContext,
            new ProductDocument
            {
                TenantId = tenantId,
                ProductId = productId
            },
            tenantId,
            cancellationToken);

        if (sqlProjection is null || !sqlProjection.IsActive || !sqlProjection.IsOnlineVisible || !sqlProjection.IsSearchable)
        {
            return null;
        }

        return ProductSearchDocumentMapper.MapFromProductDocument(sqlProjection);
    }

    private CategorySearchDocument MapToCategorySearchDocument(CategoryLineageRow row)
    {
        var document = new CategorySearchDocument
        {
            TenantId = row.TenantId,
            CategoryId = row.CategoryId,
            EntityType = "category",
            Name = row.Name ?? string.Empty,
            NameAr = null,
            Description = row.Description,
            Slug = GenerateSlug(row.Name),
            ParentId = row.ParentCategoryId,
            IsActive = true,
            IsOnlineVisible = true,
            CreatedAtUtc = row.CreatedAt,
            IndexedAtUtc = DateTime.UtcNow
        };

        document.BuildSearchText();
        return document;
    }

    private static CmsPageSearchDocument MapToCmsPageSearchDocument(PointOfSale.Core.Entities.CmsPage page)
    {
        var description = !string.IsNullOrWhiteSpace(page.MetaDescription)
            ? page.MetaDescription
            : BuildCmsSearchDescription(page.Content);

        var document = new CmsPageSearchDocument
        {
            TenantId = page.TenantId,
            PageId = page.PageGroupId,
            EntityType = "cms",
            Slug = page.Slug,
            Name = page.Title,
            NameAr = null,
            Description = description,
            MetaDescription = page.MetaDescription,
            IsActive = true,
            IsOnlineVisible = true,
            Version = page.Version,
            PublishedAtUtc = page.PublishedAt,
            IndexedAtUtc = DateTime.UtcNow
        };

        document.BuildSearchText();
        return document;
    }

    private static string? BuildCmsSearchDescription(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var withoutTags = System.Text.RegularExpressions.Regex.Replace(content, "<.*?>", " ");
        var compact = System.Text.RegularExpressions.Regex.Replace(withoutTags, "\\s+", " ").Trim();
        return compact.Length <= 500 ? compact : compact[..500];
    }

    private async Task FlushProductBatchAsync(
        Guid tenantId,
        List<ProductSearchDocument> batch,
        SyncAccumulator totals,
        CancellationToken cancellationToken)
    {
        var bulkResult = await _searchIndexService.BulkIndexProductsAsync(batch, cancellationToken);

        totals.TotalCount += batch.Count;
        totals.SuccessCount += bulkResult.SuccessCount;
        totals.FailureCount += bulkResult.FailureCount;
        AppendErrors(totals.Errors, bulkResult.Errors);

        _logger.LogInformation(
            "Indexed product search batch | TenantId: {TenantId} | BatchSize: {BatchSize} | Success: {Success} | Failed: {Failed}",
            tenantId,
            batch.Count,
            bulkResult.SuccessCount,
            bulkResult.FailureCount);

        batch.Clear();
    }

    private async Task DeleteProductFromSearchAsync(
        Guid tenantId,
        Guid productId,
        SyncAccumulator totals,
        CancellationToken cancellationToken)
    {
        totals.TotalCount++;
        if (await ProductSearchTombstoneWriter.WriteAsync(
                _searchIndexService,
                _sqlContext,
                tenantId,
                productId,
                eventId: null,
                cancellationToken))
        {
            totals.SuccessCount++;
            totals.DeletedCount++;
            return;
        }

        totals.FailureCount++;
        AppendErrors(totals.Errors, new[] { $"Failed to delete stale product search document {tenantId}_{productId}" });
    }

    private async Task DeleteCategoryFromSearchAsync(
        Guid tenantId,
        Guid categoryId,
        SyncAccumulator totals,
        CancellationToken cancellationToken)
    {
        totals.TotalCount++;
        if (await _searchIndexService.DeleteCategoryAsync(tenantId, categoryId, cancellationToken))
        {
            totals.SuccessCount++;
            totals.DeletedCount++;
            return;
        }

        totals.FailureCount++;
        AppendErrors(totals.Errors, new[] { $"Failed to delete stale category search document {tenantId}_{categoryId}" });
    }

    private async Task FlushCategoryBatchAsync(
        Guid tenantId,
        List<CategorySearchDocument> batch,
        SyncAccumulator totals,
        CancellationToken cancellationToken)
    {
        var bulkResult = await _searchIndexService.BulkIndexCategoriesAsync(batch, cancellationToken);

        totals.TotalCount += batch.Count;
        totals.SuccessCount += bulkResult.SuccessCount;
        totals.FailureCount += bulkResult.FailureCount;
        AppendErrors(totals.Errors, bulkResult.Errors);

        _logger.LogInformation(
            "Indexed category search batch | TenantId: {TenantId} | BatchSize: {BatchSize} | Success: {Success} | Failed: {Failed}",
            tenantId,
            batch.Count,
            bulkResult.SuccessCount,
            bulkResult.FailureCount);

        batch.Clear();
    }

    private static void AppendErrors(List<string> target, IEnumerable<string> source)
    {
        foreach (var error in source)
        {
            if (target.Count >= MaxReportedErrors)
            {
                break;
            }

            target.Add(error);
        }
    }

    private sealed class SyncAccumulator
    {
        public int TotalCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int DeletedCount { get; set; }
        public int StaleDeletedCount { get; set; }
        public List<string> Errors { get; } = new();
    }
}
