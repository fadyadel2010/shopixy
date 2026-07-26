using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Documents.Marketplace;

namespace PointOfSale.Infrastructure.MongoDB.Services.Marketplace;

public interface IMarketplaceProductReadRepository
{
    Task<ProjectionWriteResult> UpsertAsync(
        MarketplaceProductReadModelDocument document,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        long incomingEntityMutationVersion,
        CancellationToken cancellationToken = default);

    Task<(List<MarketplaceProductReadModelDocument> Items, long TotalCount)> GetPagedAsync(
        Guid? tenantId,
        string? search,
        string? category,
        Guid? vendorId,
        IReadOnlyCollection<Guid>? excludedVendorIds,
        bool? isAvailable,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> GetDistinctVendorIdsAsync(
        Guid? tenantId,
        string? search,
        string? category,
        Guid? vendorId,
        bool? isAvailable,
        CancellationToken cancellationToken = default);

    Task<MarketplaceProductReadModelDocument?> GetByProductIdAsync(
        Guid? tenantId,
        Guid productId,
        IReadOnlyCollection<Guid>? excludedVendorIds,
        CancellationToken cancellationToken = default);

    Task<ProjectionWriteResult> DeleteIfFreshAsync(
        Guid tenantId,
        Guid productId,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        CancellationToken cancellationToken = default);
}

public sealed class MarketplaceProductReadRepository : IMarketplaceProductReadRepository
{
    private readonly IMongoCollection<MarketplaceProductReadModelDocument> _collection;

    public MarketplaceProductReadRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<MarketplaceProductReadModelDocument>(MarketplaceProductReadModelDocument.CollectionName);
    }

    public async Task<ProjectionWriteResult> UpsertAsync(
        MarketplaceProductReadModelDocument document,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        long incomingEntityMutationVersion,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<MarketplaceProductReadModelDocument>.Filter;
        var sameMutationFreshnessFilter = filter.Or(
            filter.Exists(x => x.LastEventVersion, false),
            filter.Eq(x => x.LastEventVersion, 0),
            filter.Lt(x => x.LastEventVersion, incomingEventVersion),
            filter.And(
                filter.Eq(x => x.LastEventVersion, incomingEventVersion),
                filter.Or(
                    filter.Exists(x => x.LastEventOccurredAtUtc, false),
                    filter.Lte(x => x.LastEventOccurredAtUtc, incomingOccurredAtUtc))));
        var upsertFilter = filter.And(
            filter.Eq(x => x.Id, document.Id),
            filter.Or(
                filter.Exists(x => x.LastEntityMutationVersion, false),
                filter.Lt(x => x.LastEntityMutationVersion, incomingEntityMutationVersion),
                filter.And(
                    filter.Eq(x => x.LastEntityMutationVersion, incomingEntityMutationVersion),
                    sameMutationFreshnessFilter)));

        try
        {
            var result = await _collection.ReplaceOneAsync(
                upsertFilter,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);

            return result.MatchedCount > 0 || result.UpsertedId != null
                ? ProjectionWriteResult.Applied
                : ProjectionWriteResult.SkippedStale;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return ProjectionWriteResult.SkippedStale;
        }
    }

    public async Task<(List<MarketplaceProductReadModelDocument> Items, long TotalCount)> GetPagedAsync(
        Guid? tenantId,
        string? search,
        string? category,
        Guid? vendorId,
        IReadOnlyCollection<Guid>? excludedVendorIds,
        bool? isAvailable,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var filter = BuildFilter(
            tenantId,
            search,
            category,
            vendorId,
            excludedVendorIds,
            isAvailable);
        var totalCount = await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var items = await _collection.Find(filter)
            .SortByDescending(x => x.UpdatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyCollection<Guid>> GetDistinctVendorIdsAsync(
        Guid? tenantId,
        string? search,
        string? category,
        Guid? vendorId,
        bool? isAvailable,
        CancellationToken cancellationToken = default)
    {
        var filter = BuildFilter(
            tenantId,
            search,
            category,
            vendorId,
            excludedVendorIds: null,
            isAvailable);

        return await _collection
            .Distinct<Guid>(nameof(MarketplaceProductReadModelDocument.VendorId), filter)
            .ToListAsync(cancellationToken);
    }

    public async Task<MarketplaceProductReadModelDocument?> GetByProductIdAsync(
        Guid? tenantId,
        Guid productId,
        IReadOnlyCollection<Guid>? excludedVendorIds,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<MarketplaceProductReadModelDocument>.Filter;
        var filters = new List<FilterDefinition<MarketplaceProductReadModelDocument>>
        {
            filterBuilder.Eq(x => x.ProductId, productId)
        };

        if (tenantId.HasValue)
        {
            filters.Add(filterBuilder.Eq(x => x.TenantId, tenantId.Value));
        }

        if (excludedVendorIds != null)
        {
            var normalizedExcludedVendorIds = excludedVendorIds
                .Where(vendor => vendor != Guid.Empty)
                .Distinct()
                .ToList();
            if (normalizedExcludedVendorIds.Count > 0)
            {
                filters.Add(filterBuilder.Nin(x => x.VendorId, normalizedExcludedVendorIds));
            }
        }

        return await _collection.Find(filterBuilder.And(filters))
            .SortByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ProjectionWriteResult> DeleteIfFreshAsync(
        Guid tenantId,
        Guid productId,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<MarketplaceProductReadModelDocument>.Filter;
        var freshnessFilter = filter.Or(
            filter.Exists(x => x.LastEventVersion, false),
            filter.Eq(x => x.LastEventVersion, 0),
            filter.Lt(x => x.LastEventVersion, incomingEventVersion),
            filter.And(
                filter.Eq(x => x.LastEventVersion, incomingEventVersion),
                filter.Or(
                    filter.Exists(x => x.LastEventOccurredAtUtc, false),
                    filter.Lte(x => x.LastEventOccurredAtUtc, incomingOccurredAtUtc))));
        var deleteFilter = filter.And(
            filter.Eq(x => x.TenantId, tenantId),
            filter.Eq(x => x.ProductId, productId),
            freshnessFilter);

        var result = await _collection.DeleteOneAsync(deleteFilter, cancellationToken);
        return result.DeletedCount > 0
            ? ProjectionWriteResult.Applied
            : ProjectionWriteResult.SkippedStale;
    }

    private static FilterDefinition<MarketplaceProductReadModelDocument> BuildFilter(
        Guid? tenantId,
        string? search,
        string? category,
        Guid? vendorId,
        IReadOnlyCollection<Guid>? excludedVendorIds,
        bool? isAvailable)
    {
        var filterBuilder = Builders<MarketplaceProductReadModelDocument>.Filter;
        var filters = new List<FilterDefinition<MarketplaceProductReadModelDocument>>();

        if (tenantId.HasValue)
        {
            filters.Add(filterBuilder.Eq(x => x.TenantId, tenantId.Value));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            filters.Add(filterBuilder.Or(
                filterBuilder.Eq(x => x.Category, category),
                filterBuilder.Eq(x => x.CategoryAr, category)));
        }

        if (vendorId.HasValue)
        {
            filters.Add(filterBuilder.Eq(x => x.VendorId, vendorId.Value));
        }

        if (excludedVendorIds != null)
        {
            var normalizedExcludedVendorIds = excludedVendorIds
                .Where(vendor => vendor != Guid.Empty)
                .Distinct()
                .ToList();
            if (normalizedExcludedVendorIds.Count > 0)
            {
                filters.Add(filterBuilder.Nin(x => x.VendorId, normalizedExcludedVendorIds));
            }
        }

        if (isAvailable.HasValue)
        {
            filters.Add(filterBuilder.Eq(x => x.IsAvailable, isAvailable.Value));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var regex = new BsonRegularExpression(search.Trim(), "i");
            filters.Add(filterBuilder.Or(
                filterBuilder.Regex(x => x.Name, regex),
                filterBuilder.Regex(x => x.NameAr, regex),
                filterBuilder.Regex(x => x.Slug, regex),
                filterBuilder.Regex(x => x.VendorName, regex)));
        }

        return filters.Count == 0
            ? filterBuilder.Empty
            : filterBuilder.And(filters);
    }
}
