using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.ReadModels.Evolution;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// READ-ONLY repository interface for Category operations against MongoDB.
/// 
/// ARCHITECTURAL BOUNDARY:
/// - This interface is used ONLY by Storefront APIs (Phase 2.4)
/// - Contains NO write methods - strictly read-only
/// - All operations are tenant-scoped by design
/// - Has NO dependency on Products, Inventory, Pricing, or Stock
/// - Makes NO cross-service calls
/// - Is fully self-contained within the Read Side
/// 
/// For write operations (sync consumers), use ICategorySyncRepository.
/// </summary>
public interface ICategoryReadRepository
{
    /// <summary>
    /// Gets all visible categories for a tenant.
    /// Returns flat list - tree building happens in the service layer.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of category documents sorted by Name</returns>
    Task<List<CategoryDocument>> GetAllCategoriesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a category by its SEO-friendly slug.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="slug">SEO-friendly URL slug</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Category document if found, null otherwise</returns>
    Task<CategoryDocument?> FindBySlugAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets top-level categories for homepage navigation.
    /// Returns categories where ParentId == null OR ParentId == Guid.Empty, 
    /// IsActive == true, IsOnlineVisible == true.
    /// 
    /// Note: Handles both null and Guid.Empty for root detection to account for
    /// edge cases where MongoDB stores null as Guid.Empty or SQL sync writes Guid.Empty.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of top-level category documents</returns>
    Task<List<CategoryDocument>> GetTopLevelCategoriesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all subcategory IDs for a given parent category (recursive).
    /// Used for filtering products by category tree (parent + all descendants).
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="parentCategoryId">Parent category ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of category IDs including the parent and all descendants</returns>
    Task<List<Guid>> GetCategoryTreeIdsAsync(
        Guid tenantId,
        Guid parentCategoryId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// WRITE repository interface for Category sync operations.
/// 
/// IMPORTANT: This interface is for INFRASTRUCTURE USE ONLY.
/// - Used by MassTransit consumers to sync category data from events
/// - NOT used by Storefront APIs or Controllers
/// - Phase 2.4 is READ-ONLY - this interface is for future sync consumers
/// </summary>
public interface ICategorySyncRepository
{
    /// <summary>
    /// Upserts a category document based on TenantId + CategoryId.
    /// This is idempotent - duplicate events will not create duplicate documents.
    /// 
    /// INFRASTRUCTURE USE ONLY - Not for Storefront APIs.
    /// </summary>
    /// <param name="document">The category document to upsert</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if document was inserted/updated</returns>
    Task<bool> UpsertAsync(CategoryDocument document, CancellationToken cancellationToken = default);
}

/// <summary>
/// MongoDB repository for Category documents.
/// Implements both read (Storefront) and sync (Infrastructure) interfaces.
/// 
/// All operations are tenant-scoped by design.
/// </summary>
public class CategoryReadRepository : ICategoryReadRepository, ICategorySyncRepository
{
    private readonly MongoDbContext _context;

    public CategoryReadRepository(MongoDbContext context)
    {
        _context = context;
    }

    #region Read Operations (Storefront APIs)

    /// <inheritdoc />
    public async Task<List<CategoryDocument>> GetAllCategoriesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<CategoryDocument>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.Eq(c => c.TenantId, tenantId),
            filterBuilder.Eq(c => c.IsActive, true),
            filterBuilder.Eq(c => c.IsOnlineVisible, true)
        );

        // Sort by Name ASC for deterministic ordering
        var categories = await _context.Categories
            .Find(filter)
            .SortBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return PruneInvalidProjectionLineage(categories);
    }

    /// <inheritdoc />
    public async Task<CategoryDocument?> FindBySlugAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken = default)
    {
        // Normalize slug for comparison
        slug = slug.ToLowerInvariant().Trim();

        var categories = await GetAllCategoriesAsync(tenantId, cancellationToken);
        return categories.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<List<CategoryDocument>> GetTopLevelCategoriesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var categories = await GetAllCategoriesAsync(tenantId, cancellationToken);
        return categories
            .Where(c => !c.ParentId.HasValue || c.ParentId.Value == Guid.Empty)
            .OrderBy(c => c.Name)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<List<Guid>> GetCategoryTreeIdsAsync(
        Guid tenantId,
        Guid parentCategoryId,
        CancellationToken cancellationToken = default)
    {
        var allCategories = await GetAllCategoriesAsync(tenantId, cancellationToken);
        
        // Build the tree of IDs starting from the parent
        var result = new List<Guid> { parentCategoryId };
        CollectChildCategoryIds(parentCategoryId, allCategories, result);
        
        return result;
    }

    /// <summary>
    /// Recursively collects all child category IDs for a given parent.
    /// </summary>
    private static void CollectChildCategoryIds(
        Guid parentId,
        List<CategoryDocument> allCategories,
        List<Guid> result)
    {
        var children = allCategories.Where(c => 
            c.ParentId.HasValue && 
            c.ParentId.Value == parentId &&
            c.ParentId.Value != Guid.Empty);
        
        foreach (var child in children)
        {
            result.Add(child.CategoryId);
            // Recursively get grandchildren
            CollectChildCategoryIds(child.CategoryId, allCategories, result);
        }
    }

    private static List<CategoryDocument> PruneInvalidProjectionLineage(List<CategoryDocument> categories)
    {
        var byId = categories
            .GroupBy(c => c.CategoryId)
            .ToDictionary(g => g.Key, g => g.First());
        var memo = new Dictionary<Guid, bool>();

        return categories
            .Where(category => IsProjectionLineageVisible(category.CategoryId, byId, memo, new HashSet<Guid>()))
            .ToList();
    }

    private static bool IsProjectionLineageVisible(
        Guid categoryId,
        IReadOnlyDictionary<Guid, CategoryDocument> byId,
        Dictionary<Guid, bool> memo,
        HashSet<Guid> visiting)
    {
        if (memo.TryGetValue(categoryId, out var cached))
        {
            return cached;
        }

        if (!byId.TryGetValue(categoryId, out var category) ||
            !category.IsActive ||
            !category.IsOnlineVisible)
        {
            memo[categoryId] = false;
            return false;
        }

        if (!category.ParentId.HasValue || category.ParentId.Value == Guid.Empty)
        {
            memo[categoryId] = true;
            return true;
        }

        if (category.ParentId.Value == categoryId || !visiting.Add(categoryId))
        {
            memo[categoryId] = false;
            return false;
        }

        var visible = IsProjectionLineageVisible(category.ParentId.Value, byId, memo, visiting);
        visiting.Remove(categoryId);
        memo[categoryId] = visible;
        return visible;
    }

    #endregion

    #region Sync Operations (Infrastructure - MassTransit Consumers)

    /// <inheritdoc />
    public async Task<bool> UpsertAsync(CategoryDocument document, CancellationToken cancellationToken = default)
    {
        ReadModelEvolutionMetadata.StampCurrent(
            document,
            ReadModelEvolutionRegistry.StorefrontCategory,
            ReadModelStore.MongoDb);

        // Build filter: match by TenantId AND CategoryId (business key)
        var filter = Builders<CategoryDocument>.Filter.And(
            Builders<CategoryDocument>.Filter.Eq(c => c.TenantId, document.TenantId),
            Builders<CategoryDocument>.Filter.Eq(c => c.CategoryId, document.CategoryId)
        );

        // Use ReplaceOne with IsUpsert = true for idempotent insert/update
        var options = new ReplaceOptions { IsUpsert = true };

        var result = await _context.Categories.ReplaceOneAsync(
            filter,
            document,
            options,
            cancellationToken);

        // Return true if we modified or inserted a document
        return result.ModifiedCount > 0 || result.UpsertedId != null;
    }

    #endregion
}

