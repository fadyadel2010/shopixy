using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Eventing;
using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Infrastructure.Consumers.Marketplace;

public sealed class MarketplaceFaultMonitorConsumer :
    IConsumer<Fault<MarketplaceEvents.OrderCreatedEvent>>,
    IConsumer<Fault<MarketplaceEvents.PaymentCompletedEvent>>,
    IConsumer<Fault<MarketplaceEvents.OrderDeliveredEvent>>,
    IConsumer<Fault<MarketplaceEvents.RefundProcessedEvent>>,
    IConsumer<Fault<MarketplaceEvents.ProductUpdatedEvent>>,
    IConsumer<Fault<HomepageSectionsPublishedEvent>>
{
    private readonly IMarketplaceEventAlertHook _alertHook;
    private readonly ILogger<MarketplaceFaultMonitorConsumer> _logger;

    public MarketplaceFaultMonitorConsumer(
        IMarketplaceEventAlertHook alertHook,
        ILogger<MarketplaceFaultMonitorConsumer> logger)
    {
        _alertHook = alertHook;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<Fault<MarketplaceEvents.OrderCreatedEvent>> context) =>
        HandleFaultAsync(nameof(MarketplaceOrderCreatedConsumer), context.Message.Message.EventId, context.Message.Message.CorrelationId, nameof(MarketplaceEvents.OrderCreatedEvent), context.Message.Exceptions, context.CancellationToken);

    public Task Consume(ConsumeContext<Fault<MarketplaceEvents.PaymentCompletedEvent>> context) =>
        HandleFaultAsync(nameof(MarketplacePaymentCompletedConsumer), context.Message.Message.EventId, context.Message.Message.CorrelationId, nameof(MarketplaceEvents.PaymentCompletedEvent), context.Message.Exceptions, context.CancellationToken);

    public Task Consume(ConsumeContext<Fault<MarketplaceEvents.OrderDeliveredEvent>> context) =>
        HandleFaultAsync(nameof(MarketplaceOrderDeliveredConsumer), context.Message.Message.EventId, context.Message.Message.CorrelationId, nameof(MarketplaceEvents.OrderDeliveredEvent), context.Message.Exceptions, context.CancellationToken);

    public Task Consume(ConsumeContext<Fault<MarketplaceEvents.RefundProcessedEvent>> context) =>
        HandleFaultAsync(nameof(MarketplaceRefundProcessedConsumer), context.Message.Message.EventId, context.Message.Message.CorrelationId, nameof(MarketplaceEvents.RefundProcessedEvent), context.Message.Exceptions, context.CancellationToken);

    public Task Consume(ConsumeContext<Fault<MarketplaceEvents.ProductUpdatedEvent>> context) =>
        HandleFaultAsync(nameof(MarketplaceProductUpdatedConsumer), context.Message.Message.EventId, context.Message.Message.CorrelationId, nameof(MarketplaceEvents.ProductUpdatedEvent), context.Message.Exceptions, context.CancellationToken);

    public Task Consume(ConsumeContext<Fault<HomepageSectionsPublishedEvent>> context) =>
        HandleFaultAsync(nameof(HomepageProjectionConsumer), context.Message.Message.EventId, context.Message.Message.CorrelationId, nameof(HomepageSectionsPublishedEvent), context.Message.Exceptions, context.CancellationToken);

    private async Task HandleFaultAsync(
        string consumerName,
        Guid eventId,
        string? correlationId,
        string eventType,
        IEnumerable<ExceptionInfo> exceptions,
        CancellationToken cancellationToken)
    {
        var error = string.Join(" | ", exceptions.Select(exception => $"{exception.ExceptionType}: {exception.Message}"));
        var alert = new MarketplaceDeadLetterAlert(
            consumerName,
            eventType,
            eventId,
            correlationId,
            string.IsNullOrWhiteSpace(error) ? "Unknown fault" : error,
            DateTime.UtcNow);

        _logger.LogError(
            "Marketplace DLQ fault observed | Consumer: {ConsumerName} | EventType: {EventType} | EventId: {EventId} | CorrelationId: {CorrelationId} | Error: {Error}",
            consumerName,
            eventType,
            eventId,
            correlationId,
            alert.Error);

        await _alertHook.NotifyDeadLetterAsync(alert, cancellationToken);
    }
}

public sealed class MarketplaceFaultMonitorConsumerDefinition : ConsumerDefinition<MarketplaceFaultMonitorConsumer>
{
    public MarketplaceFaultMonitorConsumerDefinition()
    {
        EndpointName = "marketplace-fault-monitor";
        ConcurrentMessageLimit = 4;
    }
}
