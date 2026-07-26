using Microsoft.Extensions.Logging;
using PointOfSale.Core.Interfaces.Marketplace;
using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Infrastructure.Eventing;

/// <summary>
/// Default backbone pipeline. It keeps the write model untouched while reserving
/// clear extension points for Mongo projections, Redis invalidation, search sync,
/// notifications, and analytics fan-out.
/// </summary>
public class LoggingMarketplaceEventPipeline : IMarketplaceEventPipeline
{
    private readonly ILogger<LoggingMarketplaceEventPipeline> _logger;

    public LoggingMarketplaceEventPipeline(ILogger<LoggingMarketplaceEventPipeline> logger)
    {
        _logger = logger;
    }

    public Task ProcessAsync(MarketplaceEvents.OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        Log(@event, "order-read-model, notification-hook, analytics-hook");
        return Task.CompletedTask;
    }

    public Task ProcessAsync(MarketplaceEvents.PaymentCompletedEvent @event, CancellationToken cancellationToken = default)
    {
        Log(@event, "payment-read-model, settlement-hook, analytics-hook");
        return Task.CompletedTask;
    }

    public Task ProcessAsync(MarketplaceEvents.OrderDeliveredEvent @event, CancellationToken cancellationToken = default)
    {
        Log(@event, "delivery-read-model, notification-hook, analytics-hook");
        return Task.CompletedTask;
    }

    public Task ProcessAsync(MarketplaceEvents.RefundProcessedEvent @event, CancellationToken cancellationToken = default)
    {
        Log(@event, "refund-read-model, customer-notification-hook, analytics-hook");
        return Task.CompletedTask;
    }

    public Task ProcessAsync(MarketplaceEvents.ProductUpdatedEvent @event, CancellationToken cancellationToken = default)
    {
        Log(@event, "mongo-projection, redis-invalidation, elastic-index-hook");
        return Task.CompletedTask;
    }

    private void Log(MarketplaceEvents.IMarketplaceDomainEvent @event, string downstreamHooks)
    {
        _logger.LogInformation(
            "Marketplace backbone processed | EventType: {EventType} | EventId: {EventId} | EntityId: {EntityId} | TenantId: {TenantId} | CorrelationId: {CorrelationId} | Hooks: {Hooks}",
            @event.GetType().Name,
            @event.EventId,
            @event.EntityId,
            @event.TenantId,
            @event.CorrelationId,
            downstreamHooks);
    }
}
