using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.MongoDB.Services;

public class CommerceHubOrderReadRepository : ICommerceHubOrderReadRepository
{
    private readonly IMongoCollection<CommerceHubOrderDocument> _collection;
    private readonly ILogger<CommerceHubOrderReadRepository> _logger;

    public CommerceHubOrderReadRepository(
        IMongoDatabase mongoDatabase,
        ILogger<CommerceHubOrderReadRepository> logger)
    {
        _collection = mongoDatabase.GetCollection<CommerceHubOrderDocument>("commercehub_orders");
        _logger = logger;
    }

    public async Task<CommerceHubOrderDocument?> GetByMarketplaceOrderIdAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "[CommerceHubOrderRead] GetByMarketplaceOrderIdAsync | MarketplaceOrderId: {MarketplaceOrderId}",
            marketplaceOrderId);

        var documents = await _collection
            .Find(x => x.MarketplaceOrderId == marketplaceOrderId)
            .ToListAsync(cancellationToken);

        return documents
            .OrderByDescending(document => document.LastSourceMutationTicks)
            .ThenByDescending(document => document.LastEventVersion)
            .ThenByDescending(document => document.LastEventOccurredAtUtc)
            .ThenByDescending(document => document.UpdatedAt)
            .ThenByDescending(document => document.CreatedAt)
            .FirstOrDefault();
    }
}
