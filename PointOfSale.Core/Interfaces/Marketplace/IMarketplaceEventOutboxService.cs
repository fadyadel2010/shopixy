using PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Core.Interfaces.Marketplace;

/// <summary>
/// Queues marketplace domain events for reliable post-commit publication.
/// </summary>
public interface IMarketplaceEventOutboxService
{
    Task EnqueueAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IMarketplaceDomainEvent;
}
