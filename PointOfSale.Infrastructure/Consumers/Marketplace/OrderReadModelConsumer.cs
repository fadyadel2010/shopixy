using Microsoft.Extensions.Logging;
using PointOfSale.Infrastructure.ReadModels.Marketplace;
using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Infrastructure.Consumers.Marketplace;

public sealed class OrderReadModelConsumer
{
    private readonly IMarketplaceOrderReadModelProjectionService _projectionService;
    private readonly ILogger<OrderReadModelConsumer> _logger;

    public OrderReadModelConsumer(
        IMarketplaceOrderReadModelProjectionService projectionService,
        ILogger<OrderReadModelConsumer> logger)
    {
        _projectionService = projectionService;
        _logger = logger;
    }

    public Task ConsumeAsync(MarketplaceEvents.OrderCreatedEvent @event, CancellationToken cancellationToken = default) =>
        ConsumeInternalAsync(nameof(MarketplaceEvents.OrderCreatedEvent), @event, cancellationToken);

    public Task ConsumeAsync(MarketplaceEvents.PaymentCompletedEvent @event, CancellationToken cancellationToken = default) =>
        ConsumeInternalAsync(nameof(MarketplaceEvents.PaymentCompletedEvent), @event, cancellationToken);

    public Task ConsumeAsync(MarketplaceEvents.OrderDeliveredEvent @event, CancellationToken cancellationToken = default) =>
        ConsumeInternalAsync(nameof(MarketplaceEvents.OrderDeliveredEvent), @event, cancellationToken);

    public Task ConsumeAsync(MarketplaceEvents.RefundProcessedEvent @event, CancellationToken cancellationToken = default) =>
        ConsumeInternalAsync(nameof(MarketplaceEvents.RefundProcessedEvent), @event, cancellationToken);

    private async Task ConsumeInternalAsync(
        string eventType,
        MarketplaceEvents.IMarketplaceDomainEvent @event,
        CancellationToken cancellationToken)
    {
        var result = await _projectionService.RebuildAsync(
            @event.EntityId,
            @event.TenantId,
            @event.EventId,
            @event.OccurredAtUtc,
            @event.EventVersion,
            cancellationToken);

        LogResult(eventType, @event, result);
    }

    private void LogResult(
        string eventType,
        MarketplaceEvents.IMarketplaceDomainEvent @event,
        MarketplaceProjectionResult result)
    {
        switch (result)
        {
            case MarketplaceProjectionResult.Applied:
                _logger.LogInformation(
                    "Marketplace order read model projected | EventType: {EventType} | OrderId: {OrderId} | TenantId: {TenantId} | EventId: {EventId} | EventVersion: {EventVersion}",
                    eventType,
                    @event.EntityId,
                    @event.TenantId,
                    @event.EventId,
                    @event.EventVersion);
                break;

            case MarketplaceProjectionResult.SkippedStale:
                _logger.LogInformation(
                    "Marketplace order read model skipped stale event | EventType: {EventType} | OrderId: {OrderId} | TenantId: {TenantId} | EventId: {EventId} | EventVersion: {EventVersion}",
                    eventType,
                    @event.EntityId,
                    @event.TenantId,
                    @event.EventId,
                    @event.EventVersion);
                break;

            case MarketplaceProjectionResult.NotFound:
                _logger.LogWarning(
                    "Marketplace order read model source order not found | EventType: {EventType} | OrderId: {OrderId} | TenantId: {TenantId} | EventId: {EventId}",
                    eventType,
                    @event.EntityId,
                    @event.TenantId,
                    @event.EventId);
                break;

            default:
                _logger.LogInformation(
                    "Marketplace order read model result {Result} | EventType: {EventType} | OrderId: {OrderId} | TenantId: {TenantId} | EventId: {EventId}",
                    result,
                    eventType,
                    @event.EntityId,
                    @event.TenantId,
                    @event.EventId);
                break;
        }
    }
}
