using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.MongoDB.Services;

public interface ICommerceHubOrderReadRepository
{
    Task<CommerceHubOrderDocument?> GetByMarketplaceOrderIdAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken = default);
}
