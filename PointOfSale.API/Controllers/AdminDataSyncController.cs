using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.API.Services.Catalog;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Exception thrown when a MongoDB bulk write operation partially fails.
/// Contains counts of successful and failed operations.
/// </summary>
public class PartialBulkWriteException : Exception
{
    public long SuccessfulCount { get; }
    public long FailedCount { get; }
    public IReadOnlyList<WriteError> WriteErrors { get; }

    public PartialBulkWriteException(long successfulCount, long failedCount, IReadOnlyList<WriteError> writeErrors, Exception innerException)
        : base($"Bulk write partially failed: {successfulCount} succeeded, {failedCount} failed", innerException)
    {
        SuccessfulCount = successfulCount;
        FailedCount = failedCount;
        WriteErrors = writeErrors;
    }
}

/// <summary>
/// Admin Data Sync Controller - Internal endpoint to sync SQL data to MongoDB.
/// 
/// WARNING: This is for one-time data migration/sync operations.
/// In production, use event-driven projections instead.
/// </summary>
[ApiController]
[Route("api/admin/sync")]
[Authorize(Roles = "Owner,Admin")]
public class AdminDataSyncController : ControllerBase
{
    private readonly ApplicationDbContext _sqlContext;
    private readonly IMongoDatabase _mongoDatabase;
    private readonly IStorefrontCategoryService _categoryService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminDataSyncController> _logger;
    private const int SyncBatchSize = 250;
    private const int DebugSampleLimit = 250;

    public AdminDataSyncController(
        ApplicationDbContext sqlContext,
        IMongoDatabase mongoDatabase,
        IStorefrontCategoryService categoryService,
        ITenantContext tenantContext,
        ILogger<AdminDataSyncController> logger)
    {
        _sqlContext = sqlContext;
        _mongoDatabase = mongoDatabase;
        _categoryService = categoryService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Syncs all categories from SQL to MongoDB for the current tenant.
    /// This is a one-time sync operation for existing data.
    /// </summary>
    [HttpPost("categories")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SyncCategories(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        _ = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.ProjectionRepair,
            tenantId,
            tenantId,
            SqlAuthorityOverlayKind.Category);

        _logger.LogInformation("SyncCategories starting | TenantId: {TenantId}", tenantId);

        try
        {
            var collection = _mongoDatabase.GetCollection<CategoryDocument>("categories");
            var writeModels = new List<WriteModel<CategoryDocument>>(SyncBatchSize);
            var syncedCount = 0;
            var lineageSnapshot = await CategoryLineageAuthority.LoadTenantSnapshotAsync(
                _sqlContext,
                tenantId,
                cancellationToken);

            foreach (var category in lineageSnapshot.Rows.OrderBy(c => c.CategoryId))
            {
                writeModels.Add(BuildCategoryUpsert(category, lineageSnapshot.IsLiveInLineage(category.CategoryId)));

                if (writeModels.Count < SyncBatchSize)
                {
                    continue;
                }

                syncedCount += await FlushSyncBatchAsync(collection, writeModels, cancellationToken);
            }

            if (writeModels.Count > 0)
            {
                syncedCount += await FlushSyncBatchAsync(collection, writeModels, cancellationToken);
            }

            var staleDeletedCount = await DeleteStaleCategoryDocumentsAsync(
                collection,
                tenantId,
                lineageSnapshot.Rows.Select(row => row.CategoryId).ToList(),
                cancellationToken);

            if (syncedCount == 0)
            {
                return Ok(new
                {
                    message = "No categories found in SQL to sync",
                    count = 0,
                    staleDeleted = staleDeletedCount
                });
            }

            _logger.LogInformation(
                "SyncCategories completed | TenantId: {TenantId} | Count: {Count} | DeletedStale: {DeletedStale}",
                tenantId,
                syncedCount,
                staleDeletedCount);

            return Ok(new 
            { 
                message = "Categories synced successfully", 
                count = syncedCount,
                tenantId = tenantId,
                staleDeleted = staleDeletedCount,
                cacheInvalidated = 0
            });
        }
        catch (PartialBulkWriteException ex)
        {
            _logger.LogError(ex, "SyncCategories partially failed | TenantId: {TenantId} | Successful: {Successful} | Failed: {Failed}", 
                tenantId, ex.SuccessfulCount, ex.FailedCount);
            
            return StatusCode(500, new 
            { 
                error = "Partial sync failure", 
                message = "Some categories failed to sync",
                successfulCount = ex.SuccessfulCount,
                failedCount = ex.FailedCount,
                writeErrors = ex.WriteErrors.Select(e => new { e.Code, e.Message }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SyncCategories failed | TenantId: {TenantId}", tenantId);
            return StatusCode(500, new { error = "Sync failed", message = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// Syncs all shipping methods from SQL to MongoDB for the current tenant.
    /// This is a one-time sync operation for existing data.
    /// After this, event-driven projections keep data in sync.
    /// </summary>
    [HttpPost("shipping-methods")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SyncShippingMethods(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        _ = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.ProjectionRepair,
            tenantId,
            tenantId,
            SqlAuthorityOverlayKind.Shipping);

        _logger.LogInformation("SyncShippingMethods starting | TenantId: {TenantId}", tenantId);

        try
        {
            var collection = _mongoDatabase.GetCollection<ShippingMethodDocument>("shipping_methods");
            var writeModels = new List<WriteModel<ShippingMethodDocument>>(SyncBatchSize);
            var syncedCount = 0;
            var shippingMethods = await _sqlContext.ShippingMethods
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId)
                .OrderBy(s => s.Id)
                .ToListAsync(cancellationToken);
            var tenantCurrency = await ResolveTenantCurrencyAsync(tenantId, cancellationToken);

            foreach (var shippingMethod in shippingMethods)
            {
                writeModels.Add(BuildShippingMethodUpsert(shippingMethod, tenantCurrency));

                if (writeModels.Count < SyncBatchSize)
                {
                    continue;
                }

                syncedCount += await FlushSyncBatchAsync(collection, writeModels, cancellationToken);
            }

            if (writeModels.Count > 0)
            {
                syncedCount += await FlushSyncBatchAsync(collection, writeModels, cancellationToken);
            }

            var staleDeletedCount = await DeleteStaleShippingMethodDocumentsAsync(
                collection,
                tenantId,
                shippingMethods.Select(method => method.Id).ToList(),
                cancellationToken);

            if (syncedCount == 0)
            {
                return Ok(new
                {
                    message = "No shipping methods found in SQL to sync",
                    count = 0,
                    staleDeleted = staleDeletedCount
                });
            }

            _logger.LogInformation(
                "SyncShippingMethods completed | TenantId: {TenantId} | Count: {Count} | DeletedStale: {DeletedStale}",
                tenantId,
                syncedCount,
                staleDeletedCount);

            return Ok(new 
            { 
                message = "Shipping methods synced successfully", 
                count = syncedCount,
                tenantId = tenantId,
                staleDeleted = staleDeletedCount
            });
        }
        catch (PartialBulkWriteException ex)
        {
            _logger.LogError(ex, "SyncShippingMethods partially failed | TenantId: {TenantId} | Successful: {Successful} | Failed: {Failed}", 
                tenantId, ex.SuccessfulCount, ex.FailedCount);
            
            return StatusCode(500, new 
            { 
                error = "Partial sync failure", 
                message = "Some shipping methods failed to sync",
                successfulCount = ex.SuccessfulCount,
                failedCount = ex.FailedCount,
                writeErrors = ex.WriteErrors.Select(e => new { e.Code, e.Message }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SyncShippingMethods failed | TenantId: {TenantId}", tenantId);
            return StatusCode(500, new { error = "Sync failed", message = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// Generates a URL-friendly slug from a string
    /// </summary>
    private static string GenerateSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "category";
        
        var slug = name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("&", "and");
        
        // Remove special characters
        return System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\-]", "");
    }

    private static WriteModel<CategoryDocument> BuildCategoryUpsert(
        CategoryLineageRow category,
        bool isLiveInLineage)
    {
        var filter = Builders<CategoryDocument>.Filter.And(
            Builders<CategoryDocument>.Filter.Eq(d => d.TenantId, category.TenantId),
            Builders<CategoryDocument>.Filter.Eq(d => d.CategoryId, category.CategoryId));

        var isVisible = isLiveInLineage;
        var update = Builders<CategoryDocument>.Update
            .Set(d => d.TenantId, category.TenantId)
            .Set(d => d.CategoryId, category.CategoryId)
            .Set(d => d.Name, category.Name)
            .Set(d => d.Slug, GenerateSlug(category.Name))
            .Set(d => d.Description, category.Description)
            .Set(d => d.ParentId, category.ParentCategoryId)
            .Set(d => d.SortOrder, 0)
            .Set(d => d.IsActive, isVisible)
            .Set(d => d.IsOnlineVisible, isVisible)
            .Set(d => d.SyncedAtUtc, category.AuthorityTimestampUtc)
            .SetOnInsert(d => d.NameAr, null)
            .SetOnInsert(d => d.CreatedAtUtc, category.CreatedAt == default ? category.AuthorityTimestampUtc : category.CreatedAt);

        return new UpdateOneModel<CategoryDocument>(filter, update)
        {
            IsUpsert = true
        };
    }

    private static WriteModel<ShippingMethodDocument> BuildShippingMethodUpsert(ShippingMethod shippingMethod, string tenantCurrency)
    {
        var authorityTimestamp = shippingMethod.UpdatedAt ?? shippingMethod.CreatedAt;
        if (authorityTimestamp == default)
        {
            authorityTimestamp = DateTime.UtcNow;
        }

        var filter = Builders<ShippingMethodDocument>.Filter.And(
            Builders<ShippingMethodDocument>.Filter.Eq(d => d.TenantId, shippingMethod.TenantId),
            Builders<ShippingMethodDocument>.Filter.Eq(d => d.ShippingMethodId, shippingMethod.Id));

        var isActive = !shippingMethod.IsDeleted && shippingMethod.IsActive;
        var update = Builders<ShippingMethodDocument>.Update
            .Set(d => d.TenantId, shippingMethod.TenantId)
            .Set(d => d.ShippingMethodId, shippingMethod.Id)
            .Set(d => d.Name, shippingMethod.Name)
            .Set(d => d.NameAr, shippingMethod.NameAr)
            .Set(d => d.Description, shippingMethod.Description)
            .Set(d => d.BaseRate, shippingMethod.BaseRate)
            .Set(d => d.PerKgRate, shippingMethod.PerKgRate)
            .Set(d => d.FreeShippingThreshold, shippingMethod.FreeShippingThreshold)
            .Set(d => d.Currency, tenantCurrency)
            .Set(d => d.EstimatedDeliveryDaysMin, shippingMethod.EstimatedDeliveryDaysMin)
            .Set(d => d.EstimatedDeliveryDaysMax, shippingMethod.EstimatedDeliveryDaysMax)
            .Set(d => d.IsActive, isActive)
            .Set(d => d.IsDeleted, shippingMethod.IsDeleted)
            .Set(d => d.SortOrder, shippingMethod.SortOrder)
            .Set(d => d.CarrierName, shippingMethod.CarrierName)
            .Set(d => d.CarrierCode, shippingMethod.CarrierCode)
            .Set(d => d.ServiceLevel, shippingMethod.ServiceLevel)
            .Set(d => d.TrackingUrlTemplate, shippingMethod.TrackingUrlTemplate)
            .Set(d => d.AvailableCountries, shippingMethod.AvailableCountries)
            .Set(d => d.SyncedAtUtc, authorityTimestamp);

        return new UpdateOneModel<ShippingMethodDocument>(filter, update)
        {
            IsUpsert = true
        };
    }

    private async Task<string> ResolveTenantCurrencyAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenantCurrency = await _sqlContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Currency)
            .FirstOrDefaultAsync(cancellationToken);

        return CurrencyNormalizationHelper.NormalizeCurrencyOrThrow(
            tenantCurrency,
            $"Controller: {nameof(AdminDataSyncController)}, Method: {nameof(SyncShippingMethods)}, TenantId: {tenantId}");
    }

    private static async Task<int> FlushSyncBatchAsync<TDocument>(
        IMongoCollection<TDocument> collection,
        List<WriteModel<TDocument>> writeModels,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await collection.BulkWriteAsync(
                writeModels,
                new BulkWriteOptions { IsOrdered = false },
                cancellationToken);

            // For UpdateOneModel with IsUpsert=true:
            // - MatchedCount: documents that existed and were updated (whether content changed or not)
            // - Upserts.Count: documents that didn't exist and were inserted
            // - InsertedCount: only for explicit InsertOneModel (not used in this controller)
            // Total = documents processed (not necessarily modified)
            var processedCount = result.MatchedCount + result.Upserts.Count;
            return (int)processedCount;
        }
        catch (MongoBulkWriteException<TDocument> ex)
        {
            // Partial success - capture counts and rethrow as custom exception
            var processedCount = ex.Result != null 
                ? ex.Result.MatchedCount + ex.Result.Upserts.Count 
                : 0;
            var failedCount = ex.WriteErrors.Count;
            
            throw new PartialBulkWriteException(processedCount, failedCount, ex.WriteErrors, ex);
        }
        finally
        {
            writeModels.Clear();
        }
    }

    private static async Task<long> DeleteStaleCategoryDocumentsAsync(
        IMongoCollection<CategoryDocument> collection,
        Guid tenantId,
        IReadOnlyCollection<Guid> authoritativeCategoryIds,
        CancellationToken cancellationToken)
    {
        var staleFilter = Builders<CategoryDocument>.Filter.Eq(document => document.TenantId, tenantId);
        if (authoritativeCategoryIds.Count > 0)
        {
            staleFilter = Builders<CategoryDocument>.Filter.And(
                staleFilter,
                Builders<CategoryDocument>.Filter.Nin(document => document.CategoryId, authoritativeCategoryIds));
        }

        var deleted = await collection.DeleteManyAsync(staleFilter, cancellationToken);
        return deleted.DeletedCount;
    }

    private static async Task<long> DeleteStaleShippingMethodDocumentsAsync(
        IMongoCollection<ShippingMethodDocument> collection,
        Guid tenantId,
        IReadOnlyCollection<Guid> authoritativeShippingMethodIds,
        CancellationToken cancellationToken)
    {
        var staleFilter = Builders<ShippingMethodDocument>.Filter.Eq(document => document.TenantId, tenantId);
        if (authoritativeShippingMethodIds.Count > 0)
        {
            staleFilter = Builders<ShippingMethodDocument>.Filter.And(
                staleFilter,
                Builders<ShippingMethodDocument>.Filter.Nin(
                    document => document.ShippingMethodId,
                    authoritativeShippingMethodIds));
        }

        var deleted = await collection.DeleteManyAsync(staleFilter, cancellationToken);
        return deleted.DeletedCount;
    }

    /// <summary>
    /// Compatibility response for the retired category cache endpoint.
    /// </summary>
    [HttpPost("categories/invalidate-cache")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult AcknowledgeRetiredCategoryCache()
    {
        var tenantId = _tenantContext.RequiredTenantId;

        return Ok(new
        {
            message = "Category cache is retired; SQL category authority is read directly.",
            tenantId,
            keysInvalidated = 0
        });
    }

    /// <summary>
    /// Debug endpoint to inspect category data in MongoDB for troubleshooting.
    /// Returns raw category documents with ParentId details.
    /// </summary>
    [HttpGet("categories/debug")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DebugCategories(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        _logger.LogInformation("DebugCategories starting | TenantId: {TenantId}", tenantId);

        try
        {
            var collection = _mongoDatabase.GetCollection<CategoryDocument>("categories");
            
            var filter = Builders<CategoryDocument>.Filter.Eq(c => c.TenantId, tenantId);
            var filteredFilter = Builders<CategoryDocument>.Filter.And(
                Builders<CategoryDocument>.Filter.Eq(c => c.TenantId, tenantId),
                Builders<CategoryDocument>.Filter.Eq(c => c.IsActive, true),
                Builders<CategoryDocument>.Filter.Eq(c => c.IsOnlineVisible, true)
            );
            var rootFilter = Builders<CategoryDocument>.Filter.Or(
                Builders<CategoryDocument>.Filter.Eq(c => c.ParentId, (Guid?)null),
                Builders<CategoryDocument>.Filter.Eq(c => c.ParentId, Guid.Empty));

            var totalCategoriesInMongo = await collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
            var filteredCategoriesCount = await collection.CountDocumentsAsync(filteredFilter, cancellationToken: cancellationToken);
            var rootCategoriesAllCount = await collection.CountDocumentsAsync(
                Builders<CategoryDocument>.Filter.And(filter, rootFilter),
                cancellationToken: cancellationToken);
            var rootCategoriesFilteredCount = await collection.CountDocumentsAsync(
                Builders<CategoryDocument>.Filter.And(filteredFilter, rootFilter),
                cancellationToken: cancellationToken);

            var categories = await collection.Find(filter)
                .SortBy(c => c.Name)
                .Limit(DebugSampleLimit)
                .Project(c => new
                {
                    categoryId = c.CategoryId,
                    name = c.Name,
                    parentId = c.ParentId.HasValue ? c.ParentId.Value.ToString() : "NULL",
                    parentIdHasValue = c.ParentId.HasValue,
                    parentIdIsEmpty = c.ParentId.HasValue && c.ParentId.Value == Guid.Empty,
                    isRoot = !c.ParentId.HasValue || c.ParentId.Value == Guid.Empty,
                    isActive = c.IsActive,
                    isOnlineVisible = c.IsOnlineVisible,
                    slug = c.Slug
                })
                .ToListAsync(cancellationToken);

            var debugInfo = new
            {
                tenantId = tenantId,
                totalCategoriesInMongo,
                filteredCategoriesCount,
                rootCategoriesAllCount,
                rootCategoriesFilteredCount,
                categories,
                categoriesSampleLimit = DebugSampleLimit,
                categoriesTruncated = totalCategoriesInMongo > DebugSampleLimit
            };

            _logger.LogInformation(
                "DebugCategories completed | TenantId: {TenantId} | Total: {Total} | Filtered: {Filtered} | RootsAll: {RootsAll} | RootsFiltered: {RootsFiltered}",
                tenantId, totalCategoriesInMongo, filteredCategoriesCount, rootCategoriesAllCount, rootCategoriesFilteredCount);

            return Ok(debugInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DebugCategories failed | TenantId: {TenantId}", tenantId);
            return StatusCode(500, new { error = "Debug failed", message = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// Debug endpoint to inspect product data in MongoDB for troubleshooting.
    /// Returns product counts by category.
    /// </summary>
    [HttpGet("products/debug")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DebugProducts(
        [FromQuery] string? categoryId = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        _logger.LogInformation("DebugProducts starting | TenantId: {TenantId} | CategoryId: {CategoryId}", tenantId, categoryId);

        try
        {
            var collection = _mongoDatabase.GetCollection<ProductDocument>("products");
            
            var baseFilter = Builders<ProductDocument>.Filter.Eq(p => p.TenantId, tenantId);
            var filteredFilter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(p => p.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(p => p.IsActive, true),
                Builders<ProductDocument>.Filter.Eq(p => p.IsOnlineVisible, true)
            );
            var totalProductsInMongo = await collection.CountDocumentsAsync(baseFilter, cancellationToken: cancellationToken);
            var filteredProductsCount = await collection.CountDocumentsAsync(filteredFilter, cancellationToken: cancellationToken);

            var productsByCategory = await collection.Aggregate()
                .Match(baseFilter)
                .Group(
                    product => new { product.CategoryId, product.CategoryName },
                    group => new ProductCategoryDebugAggregate
                    {
                        CategoryId = group.Key.CategoryId,
                        CategoryName = group.Key.CategoryName ?? "Unknown",
                        TotalCount = group.Count(),
                        ActiveVisibleCount = group.Sum(item => item.IsActive && item.IsOnlineVisible ? 1 : 0)
                    })
                .SortByDescending(item => item.TotalCount)
                .ToListAsync(cancellationToken);

            // If categoryId is provided, show products in that category
            List<object>? categoryProducts = null;
            if (!string.IsNullOrWhiteSpace(categoryId))
            {
                Guid? resolvedCategoryId = null;
                if (Guid.TryParse(categoryId, out var catGuid))
                {
                    resolvedCategoryId = catGuid;
                }
                else
                {
                    // Try to find by slug
                    var catCollection = _mongoDatabase.GetCollection<CategoryDocument>("categories");
                    var catFilter = Builders<CategoryDocument>.Filter.And(
                        Builders<CategoryDocument>.Filter.Eq(c => c.TenantId, tenantId),
                        Builders<CategoryDocument>.Filter.Eq(c => c.Slug, categoryId.ToLowerInvariant())
                    );
                    var category = await catCollection.Find(catFilter).FirstOrDefaultAsync(cancellationToken);
                    resolvedCategoryId = category?.CategoryId;
                }

                if (resolvedCategoryId.HasValue)
                {
                    var categoryProductSample = await collection.Find(
                            Builders<ProductDocument>.Filter.And(
                                baseFilter,
                                Builders<ProductDocument>.Filter.Eq(p => p.CategoryId, resolvedCategoryId.Value)))
                        .SortBy(p => p.Name)
                        .Limit(DebugSampleLimit)
                        .Project(p => new
                        {
                            productId = p.ProductId,
                            name = p.Name,
                            categoryId = p.CategoryId,
                            categoryName = p.CategoryName,
                            isActive = p.IsActive,
                            isOnlineVisible = p.IsOnlineVisible,
                            slug = p.Slug
                        })
                        .ToListAsync(cancellationToken);

                    categoryProducts = categoryProductSample
                        .Select(static product => (object)product)
                        .ToList();
                }
            }

            var debugInfo = new
            {
                tenantId = tenantId,
                totalProductsInMongo,
                filteredProductsCount,
                productsByCategory = productsByCategory,
                requestedCategoryId = categoryId,
                productsInRequestedCategory = categoryProducts,
                debugSampleLimit = DebugSampleLimit
            };

            _logger.LogInformation(
                "DebugProducts completed | TenantId: {TenantId} | Total: {Total} | Filtered: {Filtered}",
                tenantId, totalProductsInMongo, filteredProductsCount);

            return Ok(debugInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DebugProducts failed | TenantId: {TenantId}", tenantId);
            return StatusCode(500, new { error = "Debug failed", message = "An unexpected error occurred" });
        }
    }
}

internal sealed class ProductCategoryDebugAggregate
{
    public Guid CategoryId { get; init; }
    public string CategoryName { get; init; } = "Unknown";
    public int TotalCount { get; init; }
    public int ActiveVisibleCount { get; init; }
}
