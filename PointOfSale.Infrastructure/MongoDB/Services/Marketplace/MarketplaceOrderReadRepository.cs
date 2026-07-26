using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Documents.Marketplace;

namespace PointOfSale.Infrastructure.MongoDB.Services.Marketplace;

public interface IMarketplaceOrderReadRepository
{
    Task<ProjectionWriteResult> UpsertAsync(
        MarketplaceOrderReadModelDocument document,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        long incomingEntityMutationVersion,
        CancellationToken cancellationToken = default);

    Task<(List<MarketplaceOrderReadModelDocument> Items, long TotalCount)> GetPagedAsync(
        Guid tenantId,
        string? search,
        string? status,
        int page,
        int pageSize,
        Guid? onlineCustomerId = null,
        CancellationToken cancellationToken = default);
}

public sealed class MarketplaceOrderReadRepository : IMarketplaceOrderReadRepository
{
    private readonly IMongoCollection<MarketplaceOrderReadModelDocument> _collection;

    public MarketplaceOrderReadRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<MarketplaceOrderReadModelDocument>(MarketplaceOrderReadModelDocument.CollectionName);
    }

    public async Task<ProjectionWriteResult> UpsertAsync(
        MarketplaceOrderReadModelDocument document,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        long incomingEntityMutationVersion,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<MarketplaceOrderReadModelDocument>.Filter.And(
            Builders<MarketplaceOrderReadModelDocument>.Filter.Eq(x => x.Id, document.Id),
            Builders<MarketplaceOrderReadModelDocument>.Filter.Or(
                Builders<MarketplaceOrderReadModelDocument>.Filter.Exists(nameof(MarketplaceOrderReadModelDocument.LastEntityMutationVersion), false),
                Builders<MarketplaceOrderReadModelDocument>.Filter.Lt(x => x.LastEntityMutationVersion, incomingEntityMutationVersion),
                Builders<MarketplaceOrderReadModelDocument>.Filter.And(
                    Builders<MarketplaceOrderReadModelDocument>.Filter.Eq(x => x.LastEntityMutationVersion, incomingEntityMutationVersion),
                    Builders<MarketplaceOrderReadModelDocument>.Filter.Or(
                        Builders<MarketplaceOrderReadModelDocument>.Filter.Lt(x => x.LastEventOccurredAtUtc, incomingOccurredAtUtc),
                        Builders<MarketplaceOrderReadModelDocument>.Filter.And(
                            Builders<MarketplaceOrderReadModelDocument>.Filter.Eq(x => x.LastEventOccurredAtUtc, incomingOccurredAtUtc),
                            Builders<MarketplaceOrderReadModelDocument>.Filter.Lt(x => x.LastEventVersion, incomingEventVersion))))));

        try
        {
            var result = await _collection.ReplaceOneAsync(
                filter,
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

    public async Task<(List<MarketplaceOrderReadModelDocument> Items, long TotalCount)> GetPagedAsync(
        Guid tenantId,
        string? search,
        string? status,
        int page,
        int pageSize,
        Guid? onlineCustomerId = null,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<MarketplaceOrderReadModelDocument>.Filter;
        var filters = new List<FilterDefinition<MarketplaceOrderReadModelDocument>>
        {
            filterBuilder.Eq(x => x.TenantId, tenantId)
        };

        // SECURITY: Filter by customer ID if provided (for customer-scoped access)
        if (onlineCustomerId.HasValue)
        {
            filters.Add(filterBuilder.Eq(x => x.OnlineCustomerId, onlineCustomerId.Value));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            filters.Add(filterBuilder.Eq(x => x.Status, status));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var regex = new BsonRegularExpression(search.Trim(), "i");
            filters.Add(filterBuilder.Or(
                filterBuilder.Regex(x => x.CustomerName, regex),
                filterBuilder.Regex(x => x.OrderNumber, regex)));
        }

        var filter = filterBuilder.And(filters);
        var totalCount = await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var items = await _collection.Find(filter)
            .SortByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
