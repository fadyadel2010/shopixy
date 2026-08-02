using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events;

namespace PointOfSale.Infrastructure.Instrumentation;

/// <summary>
/// Operational Telemetry for MassTransit Events.
/// Intercepts all published events to record success/failure metrics.
/// Phase CR-2: Event Bus Delivery Monitoring.
/// </summary>
public class MarketplacePublishFilter<T> : IFilter<PublishContext<T>> where T : class
{
    private readonly MarketplaceTelemetry _telemetry;
    private readonly ILogger<MarketplacePublishFilter<T>> _logger;

    public MarketplacePublishFilter(
        MarketplaceTelemetry telemetry, 
        ILogger<MarketplacePublishFilter<T>> logger)
    {
        _telemetry = telemetry;
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("marketplace-publish-metrics");
    }

    public async Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        try
        {
            LogProductProjectionDispatch(context, dispatchStarted: true, dispatchCompleted: false);
            await next.Send(context);
            LogProductProjectionDispatch(context, dispatchStarted: false, dispatchCompleted: true);
            _telemetry.EventsPublished.Add(1);
        }
        catch (Exception ex)
        {
            _telemetry.EventsFailed.Add(1);
            LogProductProjectionDispatchException(context, ex);
            _logger.LogError(ex, "EVENT PUBLISH FAILED | Type: {EventType} | MessageId: {MessageId}", 
                typeof(T).Name, context.MessageId);
            throw;
        }
    }

    private void LogProductProjectionDispatch(
        PublishContext<T> context,
        bool dispatchStarted,
        bool dispatchCompleted)
    {
        if (!TryGetProductProjectionEvent(context.Message, out var integrationEvent, out var productId))
        {
            return;
        }

        _logger.LogInformation(
            "PROJECTION_EVENT_TRACE | Stage=BusOutboxDispatcher | OutboxMessageId={OutboxMessageId} | EventType={EventType} | ProductId={ProductId} | TenantId={TenantId} | EventId={EventId} | DispatchStarted={DispatchStarted} | DispatchCompleted={DispatchCompleted} | RetryCount={RetryCount} | CorrelationId={CorrelationId} | Timestamp={Timestamp:O}",
            context.MessageId,
            integrationEvent.EventType,
            productId,
            integrationEvent.TenantId,
            integrationEvent.EventId,
            dispatchStarted,
            dispatchCompleted,
            0,
            integrationEvent.CorrelationId ?? context.CorrelationId?.ToString(),
            DateTime.UtcNow);
    }

    private void LogProductProjectionDispatchException(
        PublishContext<T> context,
        Exception exception)
    {
        if (!TryGetProductProjectionEvent(context.Message, out var integrationEvent, out var productId))
        {
            return;
        }

        _logger.LogError(
            exception,
            "PROJECTION_EVENT_TRACE | Stage=BusOutboxDispatcher | Outcome=EXCEPTION | OutboxMessageId={OutboxMessageId} | EventType={EventType} | ProductId={ProductId} | TenantId={TenantId} | EventId={EventId} | DispatchStarted={DispatchStarted} | DispatchCompleted={DispatchCompleted} | RetryCount={RetryCount} | CorrelationId={CorrelationId} | Timestamp={Timestamp:O}",
            context.MessageId,
            integrationEvent.EventType,
            productId,
            integrationEvent.TenantId,
            integrationEvent.EventId,
            true,
            false,
            0,
            integrationEvent.CorrelationId ?? context.CorrelationId?.ToString(),
            DateTime.UtcNow);
    }

    private static bool TryGetProductProjectionEvent(
        T message,
        out IntegrationEvent integrationEvent,
        out Guid productId)
    {
        productId = Guid.Empty;
        if (message is not IntegrationEvent candidate)
        {
            integrationEvent = null!;
            return false;
        }

        if (message is not ProductCreatedEvent &&
            message is not ProductUpdatedEvent &&
            message is not ProductDeletedEvent &&
            message is not ProductArchivedEvent &&
            message is not ProductPublishStatusChangedEvent)
        {
            integrationEvent = null!;
            return false;
        }

        var property = message.GetType().GetProperty("ProductId");
        if (property?.GetValue(message) is not Guid value || value == Guid.Empty)
        {
            integrationEvent = null!;
            return false;
        }

        integrationEvent = candidate;
        productId = value;
        return true;
    }
}
