using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Core.Interfaces.Marketplace;

/// <summary>
/// Backend extension point for CQRS/read-model/search/cache side effects.
/// </summary>
public interface IMarketplaceEventPipeline
{
    Task ProcessAsync(MarketplaceEvents.OrderCreatedEvent @event, CancellationToken cancellationToken = default);
    Task ProcessAsync(MarketplaceEvents.PaymentCompletedEvent @event, CancellationToken cancellationToken = default);
    Task ProcessAsync(MarketplaceEvents.OrderDeliveredEvent @event, CancellationToken cancellationToken = default);
    Task ProcessAsync(MarketplaceEvents.RefundProcessedEvent @event, CancellationToken cancellationToken = default);
    Task ProcessAsync(MarketplaceEvents.ProductUpdatedEvent @event, CancellationToken cancellationToken = default);
}
