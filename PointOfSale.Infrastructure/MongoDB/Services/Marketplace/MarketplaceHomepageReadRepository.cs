using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Documents.Marketplace;

namespace PointOfSale.Infrastructure.MongoDB.Services.Marketplace;

public interface IMarketplaceHomepageReadRepository
{
    Task<ProjectionWriteResult> UpsertAsync(
        MarketplaceHomepageReadModelDocument document,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        CancellationToken cancellationToken = default);

    Task<MarketplaceHomepageReadModelDocument?> GetByTenantOrGlobalAsync(
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    Task<ProjectionWriteResult> DeleteIfFreshAsync(
        Guid? tenantId,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        CancellationToken cancellationToken = default);
}

public sealed class MarketplaceHomepageReadRepository : IMarketplaceHomepageReadRepository
{
    private readonly IMongoCollection<MarketplaceHomepageReadModelDocument> _collection;

    public MarketplaceHomepageReadRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<MarketplaceHomepageReadModelDocument>(MarketplaceHomepageReadModelDocument.CollectionName);
    }

    public async Task<ProjectionWriteResult> UpsertAsync(
        MarketplaceHomepageReadModelDocument document,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<MarketplaceHomepageReadModelDocument>.Filter.And(
            Builders<MarketplaceHomepageReadModelDocument>.Filter.Eq(x => x.Id, document.Id),
            Builders<MarketplaceHomepageReadModelDocument>.Filter.Or(
                Builders<MarketplaceHomepageReadModelDocument>.Filter.Exists(nameof(MarketplaceHomepageReadModelDocument.LastEventOccurredAtUtc), false),
                Builders<MarketplaceHomepageReadModelDocument>.Filter.Lt(x => x.LastEventOccurredAtUtc, incomingOccurredAtUtc),
                Builders<MarketplaceHomepageReadModelDocument>.Filter.And(
                    Builders<MarketplaceHomepageReadModelDocument>.Filter.Eq(x => x.LastEventOccurredAtUtc, incomingOccurredAtUtc),
                    Builders<MarketplaceHomepageReadModelDocument>.Filter.Lt(x => x.LastEventVersion, incomingEventVersion))));

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

    public async Task<MarketplaceHomepageReadModelDocument?> GetByTenantOrGlobalAsync(
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId.HasValue)
        {
            var tenantDocument = await _collection
                .Find(x => x.TenantId == tenantId.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (tenantDocument != null)
            {
                return tenantDocument;
            }
        }

        return await _collection
            .Find(x => x.ScopeKey == MarketplaceHomepageReadModelDocument.GlobalScopeKey)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ProjectionWriteResult> DeleteIfFreshAsync(
        Guid? tenantId,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        CancellationToken cancellationToken = default)
    {
        var scopeKey = tenantId?.ToString("N") ?? MarketplaceHomepageReadModelDocument.GlobalScopeKey;
        var filter = Builders<MarketplaceHomepageReadModelDocument>.Filter.And(
            Builders<MarketplaceHomepageReadModelDocument>.Filter.Eq(x => x.Id, scopeKey),
            Builders<MarketplaceHomepageReadModelDocument>.Filter.Or(
                Builders<MarketplaceHomepageReadModelDocument>.Filter.Exists(nameof(MarketplaceHomepageReadModelDocument.LastEventOccurredAtUtc), false),
                Builders<MarketplaceHomepageReadModelDocument>.Filter.Lt(x => x.LastEventOccurredAtUtc, incomingOccurredAtUtc),
                Builders<MarketplaceHomepageReadModelDocument>.Filter.And(
                    Builders<MarketplaceHomepageReadModelDocument>.Filter.Eq(x => x.LastEventOccurredAtUtc, incomingOccurredAtUtc),
                    Builders<MarketplaceHomepageReadModelDocument>.Filter.Lt(x => x.LastEventVersion, incomingEventVersion))));

        var result = await _collection.DeleteOneAsync(filter, cancellationToken);
        return result.DeletedCount > 0
            ? ProjectionWriteResult.Applied
            : ProjectionWriteResult.SkippedStale;
    }
}
