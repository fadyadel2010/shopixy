using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Catalog;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Implementation of storefront category service.
/// 
/// ARCHITECTURAL BOUNDARY (Phase 2.4):
/// - This service is READ-ONLY - no writes, no events, no sync logic
/// - Has NO dependency on Products, Inventory, Pricing, or Stock
/// - Makes NO cross-service calls
/// - Is fully self-contained within the Read Side
/// - Uses ONLY ICategoryReadRepository (read-only interface)
/// 
/// Read Flow (strict order):
/// 1. Build Redis cache key
/// 2. Try Redis cache (graceful on miss/error)
/// 3. Query MongoDB (authoritative read source)
/// 4. Build tree structure in-memory (for tree endpoint)
/// 5. Populate Redis cache (best-effort, fire-and-forget)
/// 6. Return mapped DTO
/// 
/// Error Semantics:
/// - Redis failures → Log as WARNING, continue to MongoDB (never return 503)
/// - MongoDB failures → Log as ERROR, throw exception (controller returns 503)
/// - 503 SERVICE_UNAVAILABLE is returned ONLY when MongoDB read fails
/// 
/// Design Principles:
/// - MongoDB is the read-model source for storefront reads
/// - Redis is optional cache layer (categories are low volatility)
/// - Cache failures never break the request
/// - All operations are tenant-scoped
/// - Tree building is done in-memory, not in MongoDB
/// </summary>
public class StorefrontCategoryService : IStorefrontCategoryService
{
    private readonly ICategoryReadRepository _repository;
    private readonly ApplicationDbContext _dbContext;
    private readonly RedisConnectionFactory _redisFactory;
    private readonly RedisSettings _redisSettings;
    private readonly ILogger<StorefrontCategoryService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    // TTL for category cache (30 minutes - categories are structural, low volatility)
    private const int CategoryCacheTtlMinutes = 30;

    public StorefrontCategoryService(
        ICategoryReadRepository repository,
        ApplicationDbContext dbContext,
        RedisConnectionFactory redisFactory,
        IOptions<RedisSettings> redisSettings,
        ILogger<StorefrontCategoryService> logger)
    {
        _repository = repository;
        _dbContext = dbContext;
        _redisFactory = redisFactory;
        _redisSettings = redisSettings.Value;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    #region Get Category Tree

    /// <inheritdoc />
    public async Task<List<CategoryTreeDto>> GetCategoryTreeAsync(
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // Build cache key
        var cacheKey = CacheKeyBuilder.CategoriesTreeKey(_redisSettings.InstanceName, tenantId);

        _logger.LogDebug(
            "GetCategoryTree starting | TenantId: {TenantId} | CacheKey: {CacheKey} | CorrelationId: {CorrelationId}",
            tenantId, cacheKey, correlationId);

        // Category structure is public visibility authority, so Redis/Mongo are not
        // return authority here. Recompose from current SQL lineage on every read.
        var tree = await BuildSqlCategoryTreeAsync(tenantId, correlationId, cancellationToken);

        _logger.LogInformation(
            "Category tree built from SQL authority | TenantId: {TenantId} | RootCategories: {Roots} | CorrelationId: {CorrelationId}",
            tenantId, tree.Count, correlationId);

        // Step 4: Populate cache (fire-and-forget)
        _ = PopulateTreeCacheAsync(tenantId, cacheKey, tree, correlationId);

        return tree;
    }

    /// <summary>
    /// Builds hierarchical category tree from flat list.
    /// Root categories have ParentId == null or Guid.Empty.
    /// Children are recursively assigned.
    /// 
    /// IMPORTANT: Root detection handles multiple edge cases:
    /// - ParentId == null (standard null)
    /// - ParentId == Guid.Empty (00000000-0000-0000-0000-000000000000)
    /// - ParentId references a category not in the filtered list (orphan hidden)
    /// </summary>
    private List<CategoryTreeDto> BuildCategoryTree(List<CategoryDocument> documents)
    {
        // Create lookup for O(1) child assignment
        var lookup = documents.ToDictionary(
            d => d.CategoryId,
            d => new CategoryTreeDto
            {
                Id = d.CategoryId,
                Name = d.Name,
                NameAr = d.NameAr,
                Slug = d.Slug,
                Children = new List<CategoryTreeDto>(),
                // Promotion banner fields (Phase: Promotions Engine)
                HasActivePromotion = d.HasActivePromotion,
                PromotionBannerText = d.PromotionBannerText,
                PromotionEndDate = d.PromotionEndDate,
                PromotionBannerStyle = d.PromotionBannerStyle
            });

        var rootCategories = new List<CategoryTreeDto>();
        var childCount = 0;
        var orphanCount = 0;

        foreach (var doc in documents)
        {
            var treeNode = lookup[doc.CategoryId];

            // Check if this is a root category
            // Root = ParentId is null OR ParentId is Guid.Empty
            if (IsRootCategory(doc.ParentId))
            {
                rootCategories.Add(treeNode);
                _logger.LogDebug(
                    "Root category detected | CategoryId: {CategoryId} | Name: {Name} | ParentId: {ParentId}",
                    doc.CategoryId, doc.Name, doc.ParentId?.ToString() ?? "null");
            }
            else if (lookup.TryGetValue(doc.ParentId!.Value, out var parent))
            {
                // Valid parent found in filtered list - add as child
                parent.Children.Add(treeNode);
                childCount++;
            }
            else
            {
                // Orphan: ParentId exists but parent is not in the live filtered list.
                // Hide it instead of promoting it to a root; parent lineage is authority.
                orphanCount++;
                _logger.LogWarning(
                    "Orphan category hidden from storefront tree | CategoryId: {CategoryId} | Name: {Name} | MissingParentId: {ParentId}",
                    doc.CategoryId, doc.Name, doc.ParentId);
            }
        }

        _logger.LogDebug(
            "BuildCategoryTree completed | Total: {Total} | Roots: {Roots} | Children: {Children} | Orphans: {Orphans}",
            documents.Count, rootCategories.Count, childCount, orphanCount);

        return rootCategories;
    }

    /// <summary>
    /// Determines if a category is a root category.
    /// 
    /// Root category criteria:
    /// - ParentId is null
    /// - ParentId is Guid.Empty (00000000-0000-0000-0000-000000000000)
    /// 
    /// This handles edge cases where:
    /// - MongoDB stores null as Guid.Empty due to BSON serialization
    /// - SQL Server has null but sync writes Guid.Empty
    /// </summary>
    private static bool IsRootCategory(Guid? parentId)
    {
        return !parentId.HasValue || parentId.Value == Guid.Empty;
    }

    /// <summary>
    /// Attempts to retrieve category tree from Redis cache.
    /// Returns null on miss or any error (graceful degradation).
    /// </summary>
    private async Task<List<CategoryTreeDto>?> TryGetTreeFromCacheAsync(
        Guid tenantId, 
        string cacheKey,
        string correlationId)
    {
        try
        {
            var database = _redisFactory.GetDatabase();
            if (database == null)
            {
                _logger.LogDebug(
                    "Cache SKIP (tree - no connection) | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    tenantId, correlationId);
                return null;
            }

            var value = await database.StringGetAsync(cacheKey);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            var tree = JsonSerializer.Deserialize<List<CategoryTreeDto>>(value!, _jsonOptions);
            return tree;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache GET failed (tree) | TenantId: {TenantId} | Key: {Key} | CorrelationId: {CorrelationId}",
                tenantId, cacheKey, correlationId);
            return null;
        }
    }

    /// <summary>
    /// Populates Redis cache with category tree.
    /// Fire-and-forget - failures are logged but never fail the request.
    /// </summary>
    private async Task PopulateTreeCacheAsync(
        Guid tenantId, 
        string cacheKey, 
        List<CategoryTreeDto> tree,
        string correlationId)
    {
        try
        {
            var database = _redisFactory.GetDatabase();
            if (database == null)
            {
                return;
            }

            var json = JsonSerializer.Serialize(tree, _jsonOptions);
            var ttl = TimeSpan.FromMinutes(CategoryCacheTtlMinutes);

            await database.StringSetAsync(cacheKey, json, ttl);

            _logger.LogDebug(
                "Cache SET (tree) | TenantId: {TenantId} | RootCategories: {Count} | TTL: {TtlMinutes}min | CorrelationId: {CorrelationId}",
                tenantId, tree.Count, ttl.TotalMinutes, correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache SET failed (tree) | TenantId: {TenantId} | Key: {Key} | CorrelationId: {CorrelationId}",
                tenantId, cacheKey, correlationId);
            // Swallow exception - cache failure should not fail the request
        }
    }

    #endregion

    #region Get Category by Slug

    /// <inheritdoc />
    public async Task<CategoryReadDto?> GetCategoryBySlugAsync(
        Guid tenantId,
        string slug,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // Normalize slug
        slug = slug.ToLowerInvariant().Trim();

        // Build cache key
        var cacheKey = CacheKeyBuilder.CategoryBySlugKey(_redisSettings.InstanceName, tenantId, slug);

        _logger.LogDebug(
            "GetCategoryBySlug starting | TenantId: {TenantId} | Slug: {Slug} | CacheKey: {CacheKey} | CorrelationId: {CorrelationId}",
            tenantId, slug, cacheKey, correlationId);

        var category = await LoadSqlCategoryBySlugAsync(tenantId, slug, cancellationToken);
        if (category == null)
        {
            _logger.LogInformation(
                "Category not found | TenantId: {TenantId} | Slug: {Slug} | CorrelationId: {CorrelationId}",
                tenantId, slug, correlationId);
            return null;
        }

        var dto = MapToDto(category);

        _logger.LogInformation(
            "SQL category authority success | TenantId: {TenantId} | Slug: {Slug} | CategoryId: {CategoryId} | CorrelationId: {CorrelationId}",
            tenantId, slug, dto.Id, correlationId);

        // Step 4: Populate cache (fire-and-forget)
        _ = PopulateCacheAsync(tenantId, slug, cacheKey, dto, correlationId);

        return dto;
    }

    /// <summary>
    /// Maps MongoDB CategoryDocument to CategoryReadDto.
    /// </summary>
    private static CategoryReadDto MapToDto(CategoryDocument document)
    {
        return new CategoryReadDto
        {
            Id = document.CategoryId,
            Name = document.Name,
            NameAr = document.NameAr,
            Slug = document.Slug,
            ParentId = document.ParentId
        };
    }

    private static CategoryReadDto MapToDto(CategoryLineageRow row)
    {
        return new CategoryReadDto
        {
            Id = row.CategoryId,
            Name = row.Name,
            NameAr = null,
            Slug = GenerateSlug(row.Name),
            ParentId = row.ParentCategoryId
        };
    }

    private async Task<List<CategoryTreeDto>> BuildSqlCategoryTreeAsync(
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, tenantId, cancellationToken))
        {
            _logger.LogWarning(
                "Category tree suppressed because tenant SQL lineage is inactive or missing | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId,
                correlationId);
            return [];
        }

        var lineage = await CategoryLineageAuthority.LoadTenantSnapshotAsync(
            _dbContext,
            tenantId,
            cancellationToken);

        var liveRows = lineage.Rows
            .Where(row => lineage.IsLiveInLineage(row.CategoryId))
            .OrderBy(row => row.Name)
            .ToList();

        var lookup = liveRows.ToDictionary(
            row => row.CategoryId,
            row => new CategoryTreeDto
            {
                Id = row.CategoryId,
                Name = row.Name,
                NameAr = null,
                Slug = GenerateSlug(row.Name),
                Children = []
            });

        var roots = new List<CategoryTreeDto>();
        foreach (var row in liveRows)
        {
            var node = lookup[row.CategoryId];
            if (IsRootCategory(row.ParentCategoryId))
            {
                roots.Add(node);
            }
            else if (lookup.TryGetValue(row.ParentCategoryId!.Value, out var parent))
            {
                parent.Children.Add(node);
            }
        }

        return roots;
    }

    private async Task<CategoryLineageRow?> LoadSqlCategoryBySlugAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken)
    {
        if (!await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, tenantId, cancellationToken))
        {
            return null;
        }

        var lineage = await CategoryLineageAuthority.LoadTenantSnapshotAsync(
            _dbContext,
            tenantId,
            cancellationToken);

        return lineage.Rows
            .Where(row => lineage.IsLiveInLineage(row.CategoryId))
            .FirstOrDefault(row => string.Equals(GenerateSlug(row.Name), slug, StringComparison.OrdinalIgnoreCase));
    }

    private static string GenerateSlug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "category";
        }

        var slug = name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("&", "and");

        return System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\-]", "");
    }

    /// <summary>
    /// Attempts to retrieve category from Redis cache.
    /// Returns null on miss or any error (graceful degradation).
    /// </summary>
    private async Task<CategoryReadDto?> TryGetFromCacheAsync(
        Guid tenantId, 
        string slug, 
        string cacheKey,
        string correlationId)
    {
        try
        {
            var database = _redisFactory.GetDatabase();
            if (database == null)
            {
                _logger.LogDebug(
                    "Cache SKIP (no connection) | TenantId: {TenantId} | Slug: {Slug} | CorrelationId: {CorrelationId}",
                    tenantId, slug, correlationId);
                return null;
            }

            var value = await database.StringGetAsync(cacheKey);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            var dto = JsonSerializer.Deserialize<CategoryReadDto>(value!, _jsonOptions);
            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache GET failed | TenantId: {TenantId} | Slug: {Slug} | Key: {Key} | CorrelationId: {CorrelationId}",
                tenantId, slug, cacheKey, correlationId);
            return null;
        }
    }

    /// <summary>
    /// Populates Redis cache with category DTO.
    /// Fire-and-forget - failures are logged but never fail the request.
    /// </summary>
    private async Task PopulateCacheAsync(
        Guid tenantId, 
        string slug, 
        string cacheKey, 
        CategoryReadDto dto,
        string correlationId)
    {
        try
        {
            var database = _redisFactory.GetDatabase();
            if (database == null)
            {
                return;
            }

            var json = JsonSerializer.Serialize(dto, _jsonOptions);
            var ttl = TimeSpan.FromMinutes(CategoryCacheTtlMinutes);

            await database.StringSetAsync(cacheKey, json, ttl);

            _logger.LogDebug(
                "Cache SET | TenantId: {TenantId} | Slug: {Slug} | CategoryId: {CategoryId} | TTL: {TtlMinutes}min | CorrelationId: {CorrelationId}",
                tenantId, slug, dto.Id, ttl.TotalMinutes, correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache SET failed | TenantId: {TenantId} | Slug: {Slug} | Key: {Key} | CorrelationId: {CorrelationId}",
                tenantId, slug, cacheKey, correlationId);
            // Swallow exception - cache failure should not fail the request
        }
    }

    #endregion

    #region Cache Invalidation

    /// <inheritdoc />
    public async Task<int> InvalidateCategoryCacheAsync(
        Guid tenantId,
        string correlationId)
    {
        var invalidatedCount = 0;

        try
        {
            var database = _redisFactory.GetDatabase();
            if (database == null)
            {
                _logger.LogWarning(
                    "Cache invalidation skipped (no connection) | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    tenantId, correlationId);
                return 0;
            }

            // Invalidate category tree cache
            var treeKey = CacheKeyBuilder.CategoriesTreeKey(_redisSettings.InstanceName, tenantId);
            if (await database.KeyDeleteAsync(treeKey))
            {
                invalidatedCount++;
                _logger.LogDebug(
                    "Cache key deleted | Key: {Key} | CorrelationId: {CorrelationId}",
                    treeKey, correlationId);
            }

            // Invalidate homepage categories cache
            var homeCategoriesKey = CacheKeyBuilder.HomeCategoriesKey(_redisSettings.InstanceName, tenantId);
            if (await database.KeyDeleteAsync(homeCategoriesKey))
            {
                invalidatedCount++;
                _logger.LogDebug(
                    "Cache key deleted | Key: {Key} | CorrelationId: {CorrelationId}",
                    homeCategoriesKey, correlationId);
            }

            // Note: Individual category-by-slug keys will expire naturally (30 min TTL)
            // For bulk invalidation, we would need to use SCAN with pattern matching
            // which is expensive. The tree cache is the critical one.

            _logger.LogInformation(
                "CategoryTree cache invalidated due to structural fix | TenantId: {TenantId} | KeysInvalidated: {Count} | CorrelationId: {CorrelationId}",
                tenantId, invalidatedCount, correlationId);

            return invalidatedCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache invalidation failed | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId, correlationId);
            return invalidatedCount;
        }
    }

    #endregion
}
