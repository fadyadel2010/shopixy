using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.ReadModels.Evolution;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// Repository interface for Product read operations against MongoDB.
/// This abstraction allows for testing and clean separation of concerns.
/// </summary>
public interface IProductReadRepository
{
    /// <summary>
    /// Upserts a product document based on TenantId + ProductId.
    /// This is idempotent - duplicate events will not create duplicate documents.
    /// </summary>
    /// <param name="document">The product document to upsert</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if document was inserted/updated, false if skipped due to stale event</returns>
    Task<bool> UpsertAsync(ProductDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a product by TenantId and ProductId
    /// </summary>
    Task<ProductDocument?> FindByProductIdAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a product by TenantId and Slug (for storefront)
    /// </summary>
    Task<ProductDocument?> FindBySlugAsync(Guid tenantId, string slug, bool onlineVisibleOnly = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets paginated product list for storefront.
    /// Returns items and total count for pagination.
    /// AF-12: Supports affiliateModeOnly filter for Affiliate Mode.
    /// </summary>
    Task<(List<ProductDocument> Items, long TotalCount)> GetProductListAsync(
        Guid tenantId,
        int page,
        int pageSize,
        Guid? categoryId = null,
        bool? isActive = null,
        bool onlineVisibleOnly = true,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets paginated product list for storefront, filtering by multiple category IDs.
    /// Used for filtering by a category tree (parent + all subcategories).
    /// Returns items and total count for pagination.
    /// AF-12: Supports affiliateModeOnly filter for Affiliate Mode.
    /// </summary>
    Task<(List<ProductDocument> Items, long TotalCount)> GetProductListByCategoryTreeAsync(
        Guid tenantId,
        int page,
        int pageSize,
        List<Guid> categoryIds,
        bool? isActive = null,
        bool onlineVisibleOnly = true,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches products by text query for degraded candidate retrieval.
    /// Uses basic regex matching on Name, NameAr, SKU, CategoryName.
    /// AF-12: Supports affiliateModeOnly filter for Affiliate Mode.
    /// </summary>
    Task<(List<ProductDocument> Items, long TotalCount)> SearchProductsAsync(
        Guid tenantId,
        string query,
        int page,
        int pageSize,
        Guid? categoryId = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches products by their IDs for hydration.
    /// Preserves order of input IDs in the result.
    /// AF-12: Supports affiliateModeOnly filter for Affiliate Mode.
    /// </summary>
    Task<List<ProductDocument>> GetByProductIdsAsync(
        Guid tenantId,
        List<Guid> productIds,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets featured products for homepage display.
    /// Returns products where IsFeatured == true, IsActive == true, IsOnlineVisible == true.
    /// AF-12: Supports affiliateModeOnly filter for Affiliate Mode.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="limit">Maximum number of products to return</param>
    /// <param name="affiliateModeOnly">If true, only returns IsAffiliateEnabled=true products</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of featured product documents</returns>
    Task<List<ProductDocument>> GetFeaturedProductsAsync(
        Guid tenantId,
        int limit,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets new arrival products for homepage display.
    /// Returns most recently created visible products sorted by CreatedAtUtc DESC.
    /// AF-12: Supports affiliateModeOnly filter for Affiliate Mode.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="limit">Maximum number of products to return</param>
    /// <param name="affiliateModeOnly">If true, only returns IsAffiliateEnabled=true products</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of new arrival product documents</returns>
    Task<List<ProductDocument>> GetNewArrivalsAsync(
        Guid tenantId,
        int limit,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates specific fields of a product document.
    /// Used for partial updates when syncing unit pricing.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="productId">Product identifier</param>
    /// <param name="update">Update definition</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if document was updated, false if not found</returns>
    Task<bool> UpdateAsync(
        Guid tenantId,
        Guid productId,
        UpdateDefinition<ProductDocument> update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a product document only when the incoming event is newer than
    /// the last product-level event already applied to the projection.
    /// </summary>
    Task<bool> UpdateIfFreshAsync(
        Guid tenantId,
        Guid productId,
        DateTime eventOccurredAtUtc,
        int eventVersion,
        UpdateDefinition<ProductDocument> update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a non-visible product tombstone when SQL authority says the product is deleted,
    /// archived, inactive, or unpublished. The tombstone fences stale create/update replay.
    /// </summary>
    Task<bool> TombstoneIfFreshAsync(
        Guid tenantId,
        Guid productId,
        DateTime eventOccurredAtUtc,
        int eventVersion,
        Guid eventId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// MongoDB repository for Product documents.
/// All operations are tenant-scoped by design.
/// </summary>
public class ProductReadRepository : IProductReadRepository
{
    private readonly MongoDbContext _context;
    private readonly ILogger<ProductReadRepository> _logger;
    private readonly IProductLookupValidationCache? _lookupCache;

    public ProductReadRepository(MongoDbContext context)
        : this(context, NullLogger<ProductReadRepository>.Instance, lookupCache: null)
    {
    }

    public ProductReadRepository(
        MongoDbContext context,
        ILogger<ProductReadRepository> logger)
        : this(context, logger, lookupCache: null)
    {
    }

    public ProductReadRepository(
        MongoDbContext context,
        ILogger<ProductReadRepository> logger,
        IProductLookupValidationCache? lookupCache)
    {
        _context = context;
        _logger = logger;
        _lookupCache = lookupCache;
    }

    /// <inheritdoc />
    public async Task<bool> UpsertAsync(ProductDocument document, CancellationToken cancellationToken = default)
    {
        ReadModelEvolutionMetadata.StampCurrent(
            document,
            ReadModelEvolutionRegistry.StorefrontProduct,
            ReadModelStore.MongoDb);

        var filter = Builders<ProductDocument>.Filter.And(
            Builders<ProductDocument>.Filter.Eq(p => p.TenantId, document.TenantId),
            Builders<ProductDocument>.Filter.Eq(p => p.ProductId, document.ProductId),
            BuildFreshEventFilter(
                document.LastEventOccurredAtUtc ?? DateTime.MinValue,
                document.LastEventVersion));

        ReplaceOneResult result;
        try
        {
            result = await _context.Products.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogWarning(
                ex,
                "PROJECTION_EVENT_TRACE | Stage=Mongo | Outcome=SKIPPED | Collection={Collection} | ProductId={ProductId} | TenantId={TenantId} | EventId={EventId} | MatchedCount={MatchedCount} | ModifiedCount={ModifiedCount} | UpsertedId={UpsertedId} | DocumentVersion={DocumentVersion} | ProjectionMutationVersion={ProjectionMutationVersion} | Timestamp={Timestamp:O}",
                _context.Products.CollectionNamespace.CollectionName,
                document.ProductId,
                document.TenantId,
                document.LastEventId,
                0,
                0,
                null,
                document.ProjectionVersion,
                document.LastEventVersion,
                DateTime.UtcNow);
            return false;
        }

        _logger.LogInformation(
            "PROJECTION_EVENT_TRACE | Stage=Mongo | Outcome=WRITE | Collection={Collection} | ProductId={ProductId} | TenantId={TenantId} | EventId={EventId} | MatchedCount={MatchedCount} | ModifiedCount={ModifiedCount} | UpsertedId={UpsertedId} | DocumentVersion={DocumentVersion} | ProjectionMutationVersion={ProjectionMutationVersion} | Timestamp={Timestamp:O}",
            _context.Products.CollectionNamespace.CollectionName,
            document.ProductId,
            document.TenantId,
            document.LastEventId,
            result.MatchedCount,
            result.ModifiedCount,
            result.UpsertedId,
            document.ProjectionVersion,
            document.LastEventVersion,
            DateTime.UtcNow);

        // Return true if we modified or inserted a document
        return result.ModifiedCount > 0 || result.UpsertedId != null;
    }

    /// <inheritdoc />
    public Task<ProductDocument?> FindByProductIdAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        return _lookupCache is null
            ? LoadByProductIdFromMongoAsync(tenantId, productId, cancellationToken)
            : _lookupCache.GetOrAddByIdAsync(
                tenantId,
                productId,
                ct => LoadByProductIdFromMongoAsync(tenantId, productId, ct),
                cancellationToken);
    }

    protected virtual async Task<ProductDocument?> LoadByProductIdFromMongoAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<ProductDocument>.Filter.And(
            Builders<ProductDocument>.Filter.Eq(p => p.TenantId, tenantId),
            Builders<ProductDocument>.Filter.Eq(p => p.ProductId, productId)
        );

        return await _context.Products
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<ProductDocument?> FindBySlugAsync(
        Guid tenantId,
        string slug,
        bool onlineVisibleOnly = true,
        CancellationToken cancellationToken = default)
    {
        return _lookupCache is null
            ? LoadBySlugFromMongoAsync(tenantId, slug, onlineVisibleOnly, cancellationToken)
            : _lookupCache.GetOrAddBySlugAsync(
                tenantId,
                slug,
                onlineVisibleOnly,
                ct => LoadBySlugFromMongoAsync(tenantId, slug, onlineVisibleOnly, ct),
                cancellationToken);
    }

    protected virtual async Task<ProductDocument?> LoadBySlugFromMongoAsync(
        Guid tenantId,
        string slug,
        bool onlineVisibleOnly,
        CancellationToken cancellationToken)
    {
        var filterBuilder = Builders<ProductDocument>.Filter;
        var filters = new List<FilterDefinition<ProductDocument>>
        {
            filterBuilder.Eq(p => p.TenantId, tenantId),
            filterBuilder.Eq(p => p.Slug, slug)
        };

        if (onlineVisibleOnly)
        {
            filters.Add(filterBuilder.Eq(p => p.IsOnlineVisible, true));
        }

        var filter = filterBuilder.And(filters);

        return await _context.Products
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(List<ProductDocument> Items, long TotalCount)> GetProductListAsync(
        Guid tenantId,
        int page,
        int pageSize,
        Guid? categoryId = null,
        bool? isActive = null,
        bool onlineVisibleOnly = true,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<ProductDocument>.Filter;
        var filters = new List<FilterDefinition<ProductDocument>>
        {
            filterBuilder.Eq(p => p.TenantId, tenantId)
        };

        if (onlineVisibleOnly)
        {
            filters.Add(filterBuilder.Eq(p => p.IsOnlineVisible, true));
        }

        if (categoryId.HasValue)
        {
            filters.Add(filterBuilder.Eq(p => p.CategoryId, categoryId.Value));
        }

        if (isActive.HasValue)
        {
            filters.Add(filterBuilder.Eq(p => p.IsActive, isActive.Value));
        }

        // AF-12: Affiliate Mode filter
        if (affiliateModeOnly)
        {
            filters.Add(filterBuilder.Eq(p => p.IsAffiliateEnabled, true));
        }

        var filter = filterBuilder.And(filters);

        // Get total count for pagination
        var totalCount = await _context.Products.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        // Get paginated results sorted by CreatedAtUtc DESC
        var skip = (page - 1) * pageSize;
        var items = await _context.Products
            .Find(filter)
            .SortByDescending(p => p.CreatedAtUtc)
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<(List<ProductDocument> Items, long TotalCount)> GetProductListByCategoryTreeAsync(
        Guid tenantId,
        int page,
        int pageSize,
        List<Guid> categoryIds,
        bool? isActive = null,
        bool onlineVisibleOnly = true,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<ProductDocument>.Filter;
        var filters = new List<FilterDefinition<ProductDocument>>
        {
            filterBuilder.Eq(p => p.TenantId, tenantId)
        };

        if (onlineVisibleOnly)
        {
            filters.Add(filterBuilder.Eq(p => p.IsOnlineVisible, true));
        }

        // Filter by multiple category IDs using $in operator
        if (categoryIds.Count > 0)
        {
            filters.Add(filterBuilder.In(p => p.CategoryId, categoryIds));
        }

        if (isActive.HasValue)
        {
            filters.Add(filterBuilder.Eq(p => p.IsActive, isActive.Value));
        }

        // AF-12: Affiliate Mode filter
        if (affiliateModeOnly)
        {
            filters.Add(filterBuilder.Eq(p => p.IsAffiliateEnabled, true));
        }

        var filter = filterBuilder.And(filters);

        // Get total count for pagination
        var totalCount = await _context.Products.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        // Get paginated results sorted by CreatedAtUtc DESC
        var skip = (page - 1) * pageSize;
        var items = await _context.Products
            .Find(filter)
            .SortByDescending(p => p.CreatedAtUtc)
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<(List<ProductDocument> Items, long TotalCount)> SearchProductsAsync(
        Guid tenantId,
        string query,
        int page,
        int pageSize,
        Guid? categoryId = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<ProductDocument>.Filter;
        var filters = new List<FilterDefinition<ProductDocument>>
        {
            filterBuilder.Eq(p => p.TenantId, tenantId),
            filterBuilder.Eq(p => p.IsActive, true),
            filterBuilder.Eq(p => p.IsOnlineVisible, true)
        };

        // Text search using regex (case-insensitive)
        var searchRegex = new BsonRegularExpression(query, "i");
        var textSearchFilter = filterBuilder.Or(
            filterBuilder.Regex(p => p.Name, searchRegex),
            filterBuilder.Regex(p => p.NameAr, searchRegex),
            filterBuilder.Regex(p => p.SKU, searchRegex),
            filterBuilder.Regex(p => p.CategoryName, searchRegex)
        );
        filters.Add(textSearchFilter);

        // Optional category filter
        if (categoryId.HasValue)
        {
            filters.Add(filterBuilder.Eq(p => p.CategoryId, categoryId.Value));
        }

        // Optional price range filter
        if (minPrice.HasValue)
        {
            filters.Add(filterBuilder.Gte(p => p.Price, minPrice.Value));
        }
        if (maxPrice.HasValue)
        {
            filters.Add(filterBuilder.Lte(p => p.Price, maxPrice.Value));
        }

        // AF-12: Affiliate Mode filter
        if (affiliateModeOnly)
        {
            filters.Add(filterBuilder.Eq(p => p.IsAffiliateEnabled, true));
        }

        var filter = filterBuilder.And(filters);

        // Get total count for pagination
        var totalCount = await _context.Products.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        // Get paginated results
        var skip = (page - 1) * pageSize;
        var items = await _context.Products
            .Find(filter)
            .SortByDescending(p => p.CreatedAtUtc)
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<List<ProductDocument>> GetByProductIdsAsync(
        Guid tenantId,
        List<Guid> productIds,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (productIds == null || productIds.Count == 0)
        {
            return new List<ProductDocument>();
        }

        var filterBuilder = Builders<ProductDocument>.Filter;
        var filters = new List<FilterDefinition<ProductDocument>>
        {
            filterBuilder.Eq(p => p.TenantId, tenantId),
            filterBuilder.In(p => p.ProductId, productIds)
        };

        // AF-12: Affiliate Mode filter
        if (affiliateModeOnly)
        {
            filters.Add(filterBuilder.Eq(p => p.IsAffiliateEnabled, true));
        }

        var filter = filterBuilder.And(filters);

        var documents = await _context.Products
            .Find(filter)
            .ToListAsync(cancellationToken);

        // Preserve order of input productIds
        var documentLookup = documents.ToDictionary(d => d.ProductId);
        var orderedDocuments = productIds
            .Where(id => documentLookup.ContainsKey(id))
            .Select(id => documentLookup[id])
            .ToList();

        return orderedDocuments;
    }

    /// <inheritdoc />
    public async Task<List<ProductDocument>> GetFeaturedProductsAsync(
        Guid tenantId,
        int limit,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<ProductDocument>.Filter;
        var filters = new List<FilterDefinition<ProductDocument>>
        {
            filterBuilder.Eq(p => p.TenantId, tenantId),
            filterBuilder.Eq(p => p.IsActive, true),
            filterBuilder.Eq(p => p.IsOnlineVisible, true),
            filterBuilder.Eq(p => p.IsFeatured, true)
        };

        // AF-12: Affiliate Mode filter
        if (affiliateModeOnly)
        {
            filters.Add(filterBuilder.Eq(p => p.IsAffiliateEnabled, true));
        }

        var filter = filterBuilder.And(filters);

        // Return featured products sorted by name for consistent ordering
        var documents = await _context.Products
            .Find(filter)
            .SortBy(p => p.Name)
            .Limit(limit)
            .ToListAsync(cancellationToken);

        return documents;
    }

    /// <inheritdoc />
    public async Task<List<ProductDocument>> GetNewArrivalsAsync(
        Guid tenantId,
        int limit,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<ProductDocument>.Filter;
        var filters = new List<FilterDefinition<ProductDocument>>
        {
            filterBuilder.Eq(p => p.TenantId, tenantId),
            filterBuilder.Eq(p => p.IsActive, true),
            filterBuilder.Eq(p => p.IsOnlineVisible, true)
        };

        // AF-12: Affiliate Mode filter
        if (affiliateModeOnly)
        {
            filters.Add(filterBuilder.Eq(p => p.IsAffiliateEnabled, true));
        }

        var filter = filterBuilder.And(filters);

        // Return most recently created products (newest first)
        var documents = await _context.Products
            .Find(filter)
            .SortByDescending(p => p.CreatedAtUtc)
            .Limit(limit)
            .ToListAsync(cancellationToken);

        return documents;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        Guid tenantId,
        Guid productId,
        UpdateDefinition<ProductDocument> update,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<ProductDocument>.Filter.And(
            Builders<ProductDocument>.Filter.Eq(p => p.TenantId, tenantId),
            Builders<ProductDocument>.Filter.Eq(p => p.ProductId, productId)
        );

        var result = await _context.Products.UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount > 0;
    }

    public async Task<bool> UpdateIfFreshAsync(
        Guid tenantId,
        Guid productId,
        DateTime eventOccurredAtUtc,
        int eventVersion,
        UpdateDefinition<ProductDocument> update,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<ProductDocument>.Filter.And(
            Builders<ProductDocument>.Filter.Eq(p => p.TenantId, tenantId),
            Builders<ProductDocument>.Filter.Eq(p => p.ProductId, productId),
            BuildFreshEventFilter(eventOccurredAtUtc, eventVersion));

        var result = await _context.Products.UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount > 0;
    }

    public async Task<bool> TombstoneIfFreshAsync(
        Guid tenantId,
        Guid productId,
        DateTime eventOccurredAtUtc,
        int eventVersion,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<ProductDocument>.Filter.And(
            Builders<ProductDocument>.Filter.Eq(p => p.TenantId, tenantId),
            Builders<ProductDocument>.Filter.Eq(p => p.ProductId, productId),
            BuildFreshEventFilter(eventOccurredAtUtc, eventVersion));

        var update = Builders<ProductDocument>.Update
            .SetOnInsert(p => p.TenantId, tenantId)
            .SetOnInsert(p => p.ProductId, productId)
            .SetOnInsert(p => p.CreatedAtUtc, eventOccurredAtUtc)
            .Set(p => p.IsActive, false)
            .Set(p => p.IsOnlineVisible, false)
            .Set(p => p.IsFeatured, false)
            .Set(p => p.SyncedAtUtc, DateTime.UtcNow)
            .Set(p => p.LastEventId, eventId)
            .Set(p => p.LastEventOccurredAtUtc, eventOccurredAtUtc)
            .Set(p => p.LastEventVersion, eventVersion);

        try
        {
            var result = await _context.Products.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);

            _logger.LogInformation(
                "PROJECTION_EVENT_TRACE | Stage=Mongo | Outcome=TOMBSTONE_WRITE | Collection={Collection} | ProductId={ProductId} | TenantId={TenantId} | EventId={EventId} | MatchedCount={MatchedCount} | ModifiedCount={ModifiedCount} | UpsertedId={UpsertedId} | DocumentVersion={DocumentVersion} | ProjectionMutationVersion={ProjectionMutationVersion} | Timestamp={Timestamp:O}",
                _context.Products.CollectionNamespace.CollectionName,
                productId,
                tenantId,
                eventId,
                result.MatchedCount,
                result.ModifiedCount,
                result.UpsertedId,
                ProductDocument.CurrentProjectionVersion,
                eventVersion,
                DateTime.UtcNow);

            return result.ModifiedCount > 0 || result.UpsertedId != null;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogWarning(
                ex,
                "PROJECTION_EVENT_TRACE | Stage=Mongo | Outcome=SKIPPED | Collection={Collection} | ProductId={ProductId} | TenantId={TenantId} | EventId={EventId} | MatchedCount={MatchedCount} | ModifiedCount={ModifiedCount} | UpsertedId={UpsertedId} | DocumentVersion={DocumentVersion} | ProjectionMutationVersion={ProjectionMutationVersion} | Timestamp={Timestamp:O}",
                _context.Products.CollectionNamespace.CollectionName,
                productId,
                tenantId,
                eventId,
                0,
                0,
                null,
                ProductDocument.CurrentProjectionVersion,
                eventVersion,
                DateTime.UtcNow);
            return false;
        }
    }

    private static FilterDefinition<ProductDocument> BuildFreshEventFilter(
        DateTime eventOccurredAtUtc,
        int eventVersion)
    {
        var filter = Builders<ProductDocument>.Filter;
        return filter.Or(
            filter.Exists(p => p.LastEventVersion, false),
            filter.Eq(p => p.LastEventVersion, 0),
            filter.Lt(p => p.LastEventVersion, eventVersion),
            filter.And(
                filter.Eq(p => p.LastEventVersion, eventVersion),
                filter.Or(
                    filter.Eq(p => p.LastEventOccurredAtUtc, null),
                    filter.Lte(p => p.LastEventOccurredAtUtc, eventOccurredAtUtc))));
    }

}
