namespace PointOfSale.Core.Interfaces.Marketplace;

/// <summary>
/// Publishes persisted marketplace events to the message bus after commit.
/// </summary>
public interface IMarketplaceEventOutboxProcessor
{
    Task<int> ProcessPendingMessagesAsync(int batchSize = 100, CancellationToken cancellationToken = default);

    Task<bool> RetryFailedMessageAsync(Guid messageId, CancellationToken cancellationToken = default);
}
