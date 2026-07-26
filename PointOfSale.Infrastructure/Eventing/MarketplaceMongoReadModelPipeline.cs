using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Consumers.Marketplace;
using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Infrastructure.Eventing;

/// <summary>
/// Coordinates marketplace event-driven MongoDB projections without changing the write model.
/// Existing queue consumers remain the transport/idempotency boundary; this pipeline performs
/// the actual read-model fan-out.
/// </summary>
public sealed class MarketplaceMongoReadModelPipeline : IMarketplaceEventPipeline
{
    private readonly OrderReadModelConsumer _orderReadModelConsumer;
    private readonly ProductReadModelConsumer _productReadModelConsumer;

    public MarketplaceMongoReadModelPipeline(
        OrderReadModelConsumer orderReadModelConsumer,
        ProductReadModelConsumer productReadModelConsumer)
    {
        _orderReadModelConsumer = orderReadModelConsumer;
        _productReadModelConsumer = productReadModelConsumer;
    }

    public Task ProcessAsync(MarketplaceEvents.OrderCreatedEvent @event, CancellationToken cancellationToken = default) =>
        _orderReadModelConsumer.ConsumeAsync(@event, cancellationToken);

    public Task ProcessAsync(MarketplaceEvents.PaymentCompletedEvent @event, CancellationToken cancellationToken = default) =>
        _orderReadModelConsumer.ConsumeAsync(@event, cancellationToken);

    public Task ProcessAsync(MarketplaceEvents.OrderDeliveredEvent @event, CancellationToken cancellationToken = default) =>
        _orderReadModelConsumer.ConsumeAsync(@event, cancellationToken);

    public Task ProcessAsync(MarketplaceEvents.RefundProcessedEvent @event, CancellationToken cancellationToken = default) =>
        _orderReadModelConsumer.ConsumeAsync(@event, cancellationToken);

    public Task ProcessAsync(MarketplaceEvents.ProductUpdatedEvent @event, CancellationToken cancellationToken = default) =>
        _productReadModelConsumer.ConsumeAsync(@event, cancellationToken);
}
