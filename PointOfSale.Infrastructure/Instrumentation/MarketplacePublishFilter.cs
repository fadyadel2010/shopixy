using MassTransit;
using Microsoft.Extensions.Logging;

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
            await next.Send(context);
            _telemetry.EventsPublished.Add(1);
        }
        catch (Exception ex)
        {
            _telemetry.EventsFailed.Add(1);
            _logger.LogError(ex, "EVENT PUBLISH FAILED | Type: {EventType} | MessageId: {MessageId}", 
                typeof(T).Name, context.MessageId);
            throw;
        }
    }
}
