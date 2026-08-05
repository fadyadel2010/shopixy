using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Catalog;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Implementation of storefront category service.
/// 
/// ARCHITECTURAL BOUNDARY:
/// - This service is READ-ONLY - no writes, no events, no sync logic
/// - Has NO dependency on Products, Inventory, Pricing, or Stock
/// - Makes NO cross-service calls
/// - Is fully self-contained within the Read Side
/// - Uses the SQL category lineage as the exclusive public category identity authority
/// 
/// Read Flow:
/// 1. Load current SQL category lineage
/// 2. Apply live tenant/category visibility
/// 3. Build the public DTO or category tree
/// 
/// Design Principles:
/// - SQL category lineage owns public visibility, identity, slugs, and descendants
/// - All operations are tenant-scoped
/// - Tree building is done in-memory from the SQL lineage snapshot
/// </summary>
public class StorefrontCategoryService : IStorefrontCategoryService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<StorefrontCategoryService> _logger;

    public StorefrontCategoryService(
        ApplicationDbContext dbContext,
        ILogger<StorefrontCategoryService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    #region Get Category Tree

    /// <inheritdoc />
    public async Task<List<CategoryTreeDto>> GetCategoryTreeAsync(
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "GetCategoryTree starting | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            tenantId, correlationId);

        // Category structure is public visibility authority, so Redis/Mongo are not
        // return authority here. Recompose from current SQL lineage on every read.
        var tree = await BuildSqlCategoryTreeAsync(tenantId, correlationId, cancellationToken);

        _logger.LogInformation(
            "Category tree built from SQL authority | TenantId: {TenantId} | RootCategories: {Roots} | CorrelationId: {CorrelationId}",
            tenantId, tree.Count, correlationId);

        return tree;
    }

    /// <summary>
    /// Determines if a category is a root category.
    /// 
    /// Root category criteria:
    /// - ParentId is null
    /// - ParentId is Guid.Empty (00000000-0000-0000-0000-000000000000)
    /// 
    /// This handles lineages that use either null or Guid.Empty for a root.
    /// </summary>
    private static bool IsRootCategory(Guid? parentId)
    {
        return !parentId.HasValue || parentId.Value == Guid.Empty;
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

        _logger.LogDebug(
            "GetCategoryBySlug starting | TenantId: {TenantId} | Slug: {Slug} | CorrelationId: {CorrelationId}",
            tenantId, slug, correlationId);

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

        return dto;
    }

    /// <inheritdoc />
    public async Task<List<Guid>> ResolveCategoryTreeIdsAsync(
        Guid tenantId,
        string categoryIdOrSlug,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(categoryIdOrSlug) ||
            !await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, tenantId, cancellationToken))
        {
            return [];
        }

        var snapshot = await CategoryLineageAuthority.LoadTenantSnapshotAsync(
            _dbContext,
            tenantId,
            cancellationToken);

        CategoryLineageRow? root = null;
        if (Guid.TryParse(categoryIdOrSlug, out var categoryId))
        {
            snapshot.TryGetLiveCategory(categoryId, out root);
        }
        else
        {
            var normalizedSlug = categoryIdOrSlug.Trim().ToLowerInvariant();
            root = snapshot.Rows.FirstOrDefault(row =>
                snapshot.IsLiveInLineage(row.CategoryId) &&
                string.Equals(GenerateSlug(row.Name), normalizedSlug, StringComparison.OrdinalIgnoreCase));
        }

        if (root == null)
        {
            _logger.LogInformation(
                "Category filter not found in SQL authority | TenantId: {TenantId} | Category: {Category} | CorrelationId: {CorrelationId}",
                tenantId,
                categoryIdOrSlug,
                correlationId);
            return [];
        }

        var liveRows = snapshot.Rows
            .Where(row => snapshot.IsLiveInLineage(row.CategoryId))
            .ToList();
        var result = new List<Guid> { root.CategoryId };
        var pending = new Queue<Guid>();
        pending.Enqueue(root.CategoryId);

        while (pending.Count > 0)
        {
            var parentId = pending.Dequeue();
            foreach (var child in liveRows.Where(row => row.ParentCategoryId == parentId))
            {
                if (result.Contains(child.CategoryId))
                {
                    continue;
                }

                result.Add(child.CategoryId);
                pending.Enqueue(child.CategoryId);
            }
        }

        return result;
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

    #endregion
}
