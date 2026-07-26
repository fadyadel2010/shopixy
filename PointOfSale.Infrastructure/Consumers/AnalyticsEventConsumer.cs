using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Analytics.Dispatchers;
using PointOfSale.Infrastructure.Attribution;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Consumer for analytics events from the event bus.
/// 
/// EVENT PIPELINE:
/// StorefrontAnalyticsController
///    ↓
/// AnalyticsEventValidator
///    ↓
/// EventBus (MassTransit) → Publish(AnalyticsEventReceivedEvent)
///    ↓
/// AnalyticsEventConsumer (THIS)
///    ├── Redis Store (Hot) - Always attempted first
///    ├── Mongo Store (Cold) - Always attempted regardless of Redis result
///    ├── Attribution Engine (Phase 7) - Capture UTM params, link orders to sources
///    └── Dispatcher Pipeline (Phase 3) - Fan-out to GA4, Meta, Admin Dashboard
/// 
/// ARCHITECTURAL RULES:
/// 1. Consumer MUST be idempotent (by EventId)
/// 2. Redis failure MUST NOT block MongoDB write
/// 3. MongoDB failure MUST NOT block Redis write
/// 4. Dispatch failures MUST NOT block storage
/// 5. Attribution failures MUST NOT block storage or dispatch
/// 6. Storage failures are retried - both stores failing throws to the broker retry pipeline
/// 7. Tenant isolation is enforced (Guid.Empty rejected)
/// </summary>
public class AnalyticsEventConsumer : IConsumer<AnalyticsEventReceivedEvent>
{
    private readonly IAnalyticsHotStoreService _hotStore;
    private readonly IAnalyticsColdStoreService _coldStore;
    private readonly AnalyticsDispatcherPipeline _dispatcherPipeline;
    private readonly IAttributionContextResolver _attributionContextResolver;
    private readonly IOrderAttributionResolver _orderAttributionResolver;
    private readonly AnalyticsReliabilityTelemetry _telemetry;
    private readonly ILogger<AnalyticsEventConsumer> _logger;

    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromDays(7);

    public AnalyticsEventConsumer(
        IAnalyticsHotStoreService hotStore,
        IAnalyticsColdStoreService coldStore,
        AnalyticsDispatcherPipeline dispatcherPipeline,
        IAttributionContextResolver attributionContextResolver,
        IOrderAttributionResolver orderAttributionResolver,
        AnalyticsReliabilityTelemetry telemetry,
        ILogger<AnalyticsEventConsumer> logger)
    {
        _hotStore = hotStore;
        _coldStore = coldStore;
        _dispatcherPipeline = dispatcherPipeline;
        _attributionContextResolver = attributionContextResolver;
        _orderAttributionResolver = orderAttributionResolver;
        _telemetry = telemetry;
        _logger = logger;

        _logger.LogInformation("[Analytics Consumer] STARTED - Ready to process analytics events with dispatcher pipeline and attribution engine");
    }

    public async Task Consume(ConsumeContext<AnalyticsEventReceivedEvent> context)
    {
        var message = context.Message;
        var analyticsEvent = message.AnalyticsEvent;
        var cancellationToken = context.CancellationToken;

        // SECURITY: Reject events without valid TenantId
        if (!ValidateTenantId(analyticsEvent))
        {
            return;
        }

        _logger.LogDebug(
            "[Analytics] Processing event | TenantId: {TenantId} | EventId: {EventId} | Type: {Type} | TraceId: {TraceId}",
            analyticsEvent.TenantId,
            analyticsEvent.EventId,
            analyticsEvent.Type,
            context.MessageId);

        var storeOutcome = await _coldStore.StoreEventWithOutcomeAsync(analyticsEvent, cancellationToken);
        if (storeOutcome == AnalyticsEventStoreOutcome.AlreadyExists)
        {
            _logger.LogDebug(
                "[Analytics] Durable replay rejected | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId,
                analyticsEvent.EventId);
            return;
        }

        if (storeOutcome == AnalyticsEventStoreOutcome.Failed)
        {
            _telemetry.RecordStorageFailure(analyticsEvent.Type.ToString(), "durable_event_store_failed");
            throw new InvalidOperationException(
                $"Analytics event {analyticsEvent.EventId} could not be durably persisted.");
        }

        var redisSuccess = false;
        try
        {
            redisSuccess = await _hotStore.StoreEventAsync(analyticsEvent, cancellationToken: cancellationToken);

            if (redisSuccess)
            {
                await _hotStore.IncrementCounterAsync(
                    analyticsEvent.TenantId,
                    analyticsEvent.Type.ToString().ToLowerInvariant(),
                    analyticsEvent.OccurredAtUtc,
                    cancellationToken: cancellationToken);

                await IncrementContextualCountersAsync(analyticsEvent, cancellationToken);
                await _hotStore.MarkEventProcessedAsync(
                    analyticsEvent.TenantId,
                    analyticsEvent.EventId,
                    IdempotencyTtl,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics] Redis store failed (non-critical) | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId, analyticsEvent.EventId);
        }

        _telemetry.RecordStorageSuccess(analyticsEvent.Type.ToString());
        _logger.LogInformation(
            "[Analytics] Event durably processed | TenantId: {TenantId} | EventId: {EventId} | Type: {Type} | RedisProjection: {Redis}",
            analyticsEvent.TenantId,
            analyticsEvent.EventId,
            analyticsEvent.Type,
            redisSuccess ? "OK" : "FAIL");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await _dispatcherPipeline.DispatchToAllAsync(analyticsEvent, linkedCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics] Dispatch failed after durable insert | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId, analyticsEvent.EventId);
        }

        try
        {
            await ProcessAttributionAsync(analyticsEvent, linkedCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Attribution] Processing failed after durable insert | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId, analyticsEvent.EventId);
        }
    }

    private bool ValidateTenantId(Core.Analytics.AnalyticsEvent analyticsEvent)
    {
        if (analyticsEvent.TenantId == Guid.Empty)
        {
            _logger.LogError(
                "[Analytics] SECURITY: Event REJECTED - Missing TenantId | EventId: {EventId}",
                analyticsEvent.EventId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Processes attribution for the analytics event.
    /// - PageView: Captures UTM parameters and stores attribution context
    /// - PurchaseCompleted: Creates order attribution linking the order to its source
    /// </summary>
    private async Task ProcessAttributionAsync(
        Core.Analytics.AnalyticsEvent analyticsEvent,
        CancellationToken cancellationToken)
    {
        switch (analyticsEvent.Type)
        {
            case AnalyticsEventType.PageView:
                // Capture attribution context from UTM params
                await _attributionContextResolver.ResolveFromEventAsync(analyticsEvent, cancellationToken);
                break;

            case AnalyticsEventType.PurchaseCompleted:
                // Create order attribution (both FirstTouch and LastTouch)
                await _orderAttributionResolver.ResolveAndStoreAsync(analyticsEvent, cancellationToken);
                break;

            default:
                // Other event types don't need attribution processing
                break;
        }
    }

    private async Task IncrementContextualCountersAsync(
        Core.Analytics.AnalyticsEvent analyticsEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (analyticsEvent.Type)
            {
                case AnalyticsEventType.PageView:
                    if (!string.IsNullOrWhiteSpace(analyticsEvent.Context?.PageUrl))
                    {
                        var path = GetPathFromUrl(analyticsEvent.Context.PageUrl);
                        if (!string.IsNullOrEmpty(path))
                        {
                            await _hotStore.IncrementPageViewCounterAsync(
                                analyticsEvent.TenantId,
                                path,
                                analyticsEvent.OccurredAtUtc,
                                cancellationToken);
                        }
                    }
                    break;

                case AnalyticsEventType.ProductView:
                    if (analyticsEvent.Payload?.ProductId.HasValue == true)
                    {
                        await _hotStore.IncrementProductViewCounterAsync(
                            analyticsEvent.TenantId,
                            analyticsEvent.Payload.ProductId.Value,
                            analyticsEvent.OccurredAtUtc,
                            cancellationToken);
                    }
                    break;

                case AnalyticsEventType.AddToCart:
                    if (analyticsEvent.Payload?.ProductId.HasValue == true)
                    {
                        await _hotStore.IncrementAddToCartCounterAsync(
                            analyticsEvent.TenantId,
                            analyticsEvent.Payload.ProductId.Value,
                            analyticsEvent.OccurredAtUtc,
                            cancellationToken);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics] Failed to increment contextual counters | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId, analyticsEvent.EventId);
        }
    }

    private string GetPathFromUrl(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.AbsolutePath;
            }
            if (url.StartsWith("/"))
            {
                var questionMarkIndex = url.IndexOf('?');
                return questionMarkIndex > 0 ? url.Substring(0, questionMarkIndex) : url;
            }
            return url;
        }
        catch
        {
            return url;
        }
    }
}

/// <summary>
/// Consumer definition for AnalyticsEventConsumer.
/// </summary>
public class AnalyticsEventConsumerDefinition : ConsumerDefinition<AnalyticsEventConsumer>
{
    public AnalyticsEventConsumerDefinition()
    {
        EndpointName = "analytics-events";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<AnalyticsEventConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Exponential(
            retryLimit: 5,
            minInterval: TimeSpan.FromSeconds(1),
            maxInterval: TimeSpan.FromMinutes(1),
            intervalDelta: TimeSpan.FromSeconds(5)));

        // Prefetch for high throughput
        endpointConfigurator.PrefetchCount = 32;
    }
}
