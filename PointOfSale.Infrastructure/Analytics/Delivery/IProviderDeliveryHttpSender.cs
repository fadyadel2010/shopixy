using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public interface IProviderDeliveryHttpSender
{
    Task<ProviderDeliverySendResult> SendAsync(
        ProviderDeliveryOutboxMessage message,
        CancellationToken cancellationToken = default);
}
