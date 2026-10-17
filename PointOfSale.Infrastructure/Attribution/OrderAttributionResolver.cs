using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Analytics.Attribution;
using PointOfSale.Core.Financial;

namespace PointOfSale.Infrastructure.Attribution;

/// <summary>
/// Order Attribution Resolver - Links orders to their marketing source.
/// 
/// FLOW:
/// 1. PurchaseCompleted event received
/// 2. Get SessionId from event
/// 3. Lookup AttributionContext (FirstTouch and/or LastTouch)
/// 4. Create OrderAttribution records
/// 5. Store in OrderAttributionStore
/// 
/// CRITICAL RULES:
/// - Order attribution is IMMUTABLE after creation
/// - Both FirstTouch and LastTouch are stored for comparison
/// - If no AttributionContext found, default to direct traffic
/// </summary>
public interface IOrderAttributionResolver
{
    /// <summary>
    /// Resolves and stores order attribution from a PurchaseCompleted event.
    /// Creates both FirstTouch and LastTouch attribution records.
    /// </summary>
    Task<(OrderAttribution? firstTouch, OrderAttribution? lastTouch)> ResolveAndStoreAsync(
        AnalyticsEvent purchaseEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves order attribution for a specific model without storing.
    /// Used for preview/testing.
    /// </summary>
    Task<OrderAttribution?> ResolveWithoutStoringAsync(
        AnalyticsEvent purchaseEvent,
        AttributionModel model,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of OrderAttributionResolver.
/// </summary>
public class OrderAttributionResolver : IOrderAttributionResolver
{
    private readonly IAttributionContextStore _contextStore;
    private readonly IOrderAttributionStore _attributionStore;
    private readonly ILogger<OrderAttributionResolver> _logger;

    public OrderAttributionResolver(
        IAttributionContextStore contextStore,
        IOrderAttributionStore attributionStore,
        ILogger<OrderAttributionResolver> logger)
    {
        _contextStore = contextStore;
        _attributionStore = attributionStore;
        _logger = logger;
    }

    public async Task<(OrderAttribution? firstTouch, OrderAttribution? lastTouch)> ResolveAndStoreAsync(
        AnalyticsEvent purchaseEvent,
        CancellationToken cancellationToken = default)
    {
        // Validate event type
        if (purchaseEvent.Type != AnalyticsEventType.PurchaseCompleted)
        {
            _logger.LogWarning(
                "[Attribution] ResolveAndStoreAsync called with non-purchase event | Type: {Type}",
                purchaseEvent.Type);
            return (null, null);
        }

        var orderId = purchaseEvent.Payload?.OrderId;
        if (!orderId.HasValue || orderId.Value == Guid.Empty)
        {
            _logger.LogWarning(
                "[Attribution] PurchaseCompleted event missing OrderId | EventId: {EventId}",
                purchaseEvent.EventId);
            return (null, null);
        }

        var sessionId = purchaseEvent.Actor?.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogWarning(
                "[Attribution] PurchaseCompleted event missing SessionId | EventId: {EventId}",
                purchaseEvent.EventId);
            return (null, null);
        }

        var orderValue = purchaseEvent.Payload?.Value ?? 0m;
        var currency = ResolveRequiredCurrency(purchaseEvent);

        // Check if attribution already exists (idempotency)
        var hasFirstTouch = await _attributionStore.HasAttributionAsync(
            purchaseEvent.TenantId, orderId.Value, AttributionModel.FirstTouch, cancellationToken);
        var hasLastTouch = await _attributionStore.HasAttributionAsync(
            purchaseEvent.TenantId, orderId.Value, AttributionModel.LastTouch, cancellationToken);

        if (hasFirstTouch && hasLastTouch)
        {
            _logger.LogDebug(
                "[Attribution] Order already attributed | TenantId: {TenantId} | OrderId: {OrderId}",
                purchaseEvent.TenantId, orderId.Value);
            return (null, null);
        }

        OrderAttribution? firstTouchAttribution = null;
        OrderAttribution? lastTouchAttribution = null;

        // Resolve FirstTouch
        if (!hasFirstTouch)
        {
            var firstTouchContext = await GetContextAtConversionAsync(purchaseEvent, AttributionModel.FirstTouch, cancellationToken);

            if (firstTouchContext != null)
            {
                firstTouchAttribution = OrderAttribution.FromContext(
                    firstTouchContext, orderId.Value, orderValue, currency, AttributionModel.FirstTouch, purchaseEvent.OccurredAtUtc);
            }
            else
            {
                // Default to direct traffic
                var directContext = AttributionContext.CreateDirect(purchaseEvent.TenantId, sessionId);
                directContext.CapturedAtUtc = purchaseEvent.OccurredAtUtc;
                directContext.CustomerId = purchaseEvent.Actor?.CustomerId;
                firstTouchAttribution = OrderAttribution.FromContext(
                    directContext, orderId.Value, orderValue, currency, AttributionModel.FirstTouch, purchaseEvent.OccurredAtUtc);
            }

            firstTouchAttribution.CustomerId = purchaseEvent.Actor?.CustomerId;
            await _attributionStore.StoreAttributionAsync(firstTouchAttribution, cancellationToken);
        }

        // Resolve LastTouch
        if (!hasLastTouch)
        {
            var lastTouchContext = await GetContextAtConversionAsync(purchaseEvent, AttributionModel.LastTouch, cancellationToken);

            if (lastTouchContext != null)
            {
                lastTouchAttribution = OrderAttribution.FromContext(
                    lastTouchContext, orderId.Value, orderValue, currency, AttributionModel.LastTouch, purchaseEvent.OccurredAtUtc);
            }
            else
            {
                // Default to direct traffic
                var directContext = AttributionContext.CreateDirect(purchaseEvent.TenantId, sessionId);
                directContext.CapturedAtUtc = purchaseEvent.OccurredAtUtc;
                directContext.CustomerId = purchaseEvent.Actor?.CustomerId;
                lastTouchAttribution = OrderAttribution.FromContext(
                    directContext, orderId.Value, orderValue, currency, AttributionModel.LastTouch, purchaseEvent.OccurredAtUtc);
            }

            lastTouchAttribution.CustomerId = purchaseEvent.Actor?.CustomerId;
            await _attributionStore.StoreAttributionAsync(lastTouchAttribution, cancellationToken);
        }

        _logger.LogInformation(
            "[Attribution] Order attributed | TenantId: {TenantId} | OrderId: {OrderId} | FirstTouch: {FirstSource} | LastTouch: {LastSource} | Value: {Value}",
            purchaseEvent.TenantId,
            orderId.Value,
            firstTouchAttribution?.Source ?? "(existing)",
            lastTouchAttribution?.Source ?? "(existing)",
            orderValue);

        return (firstTouchAttribution, lastTouchAttribution);
    }

    public async Task<OrderAttribution?> ResolveWithoutStoringAsync(
        AnalyticsEvent purchaseEvent,
        AttributionModel model,
        CancellationToken cancellationToken = default)
    {
        if (purchaseEvent.Type != AnalyticsEventType.PurchaseCompleted)
        {
            return null;
        }

        var orderId = purchaseEvent.Payload?.OrderId;
        if (!orderId.HasValue || orderId.Value == Guid.Empty)
        {
            return null;
        }

        var sessionId = purchaseEvent.Actor?.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var orderValue = purchaseEvent.Payload?.Value ?? 0m;
        var currency = ResolveRequiredCurrency(purchaseEvent);

        AttributionContext? context = await GetContextAtConversionAsync(purchaseEvent, model, cancellationToken);

        if (context == null)
        {
            context = AttributionContext.CreateDirect(purchaseEvent.TenantId, sessionId);
            context.CapturedAtUtc = purchaseEvent.OccurredAtUtc;
            context.CustomerId = purchaseEvent.Actor?.CustomerId;
        }

        var attribution = OrderAttribution.FromContext(context, orderId.Value, orderValue, currency, model, purchaseEvent.OccurredAtUtc);
        attribution.CustomerId = purchaseEvent.Actor?.CustomerId;

        return attribution;
    }

    private async Task<AttributionContext?> GetContextAtConversionAsync(AnalyticsEvent purchaseEvent, AttributionModel model, CancellationToken ct)
    {
        // Event time, not delivery/cache-write order, defines first/last touch. Exclude later visits.
        var contexts = await _contextStore.GetAllContextsForSessionAsync(purchaseEvent.TenantId, purchaseEvent.Actor.SessionId!, ct);
        var eligible = contexts.Where(c => c.TenantId == purchaseEvent.TenantId && c.SessionId == purchaseEvent.Actor.SessionId && c.CapturedAtUtc <= purchaseEvent.OccurredAtUtc)
            .OrderBy(c => c.CapturedAtUtc).ThenBy(c => c.Source, StringComparer.Ordinal).ThenBy(c => c.Campaign, StringComparer.Ordinal);
        return model == AttributionModel.FirstTouch ? eligible.FirstOrDefault() : eligible.LastOrDefault();
    }

    private static string ResolveRequiredCurrency(AnalyticsEvent purchaseEvent)
    {
        return FinancialCurrencyGuard.RequireCurrency(
            purchaseEvent.Context?.Currency,
            "analytics.context.currency",
            $"tenantId:{purchaseEvent.TenantId:D};eventId:{purchaseEvent.EventId:D}");
    }
}
