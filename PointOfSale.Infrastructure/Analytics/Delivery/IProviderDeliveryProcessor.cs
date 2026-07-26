namespace PointOfSale.Infrastructure.Analytics.Delivery;

public interface IProviderDeliveryProcessor
{
    Task<int> ProcessPendingMessagesAsync(int batchSize, CancellationToken cancellationToken = default);
}
