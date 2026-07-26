using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Analytics.Delivery;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Analytics.Dispatchers;

/// <summary>
/// Dispatcher for Google Analytics 4 using Measurement Protocol.
/// 
/// SECURITY GUARANTEES:
/// - NO gtag.js on frontend
/// - NO GA code on storefront
/// - Server-side only dispatch
/// - Client ID = SessionId (for user journey tracking)
/// 
/// GA4 MEASUREMENT PROTOCOL:
/// - Endpoint: https://www.google-analytics.com/mp/collect
/// - Debug: https://www.google-analytics.com/debug/mp/collect
/// - Requires: measurement_id and api_secret
/// 
/// EVENT MAPPING:
/// POS Event           → GA4 Event
/// PageView            → page_view
/// ProductView         → view_item
/// ProductImpression   → view_item_list
/// AddToCart           → add_to_cart
/// BeginCheckout       → begin_checkout
/// PurchaseCompleted   → purchase
/// SearchPerformed     → search
/// FilterApplied       → select_item
/// PaymentFailed       → purchase_error (custom)
/// </summary>
public class GoogleAnalyticsDispatcher : IAnalyticsDispatcher
{
    private const string Ga4Endpoint = "https://www.google-analytics.com/mp/collect";
    private const string Ga4DebugEndpoint = "https://www.google-analytics.com/debug/mp/collect";

    private readonly IProviderDeliveryOutboxService _providerDeliveryOutbox;
    private readonly IPixelValueResolver _pixelValueResolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GoogleAnalyticsDispatcher> _logger;

    private static readonly JsonSerializerOptions ProviderJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AnalyticsProvider Provider => AnalyticsProvider.GoogleAnalytics;
    public int Priority => 10; // After AdminRealtime

    public GoogleAnalyticsDispatcher(
        IProviderDeliveryOutboxService providerDeliveryOutbox,
        IPixelValueResolver pixelValueResolver,
        IServiceScopeFactory scopeFactory,
        ILogger<GoogleAnalyticsDispatcher> logger)
    {
        _providerDeliveryOutbox = providerDeliveryOutbox;
        _pixelValueResolver = pixelValueResolver;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public bool IsEnabled(TenantAnalyticsSettings? settings)
    {
        if (settings?.Google == null) return false;
        if (!settings.Google.Enabled) return false;
        if (string.IsNullOrWhiteSpace(settings.Google.MeasurementId)) return false;
        if (string.IsNullOrWhiteSpace(settings.Google.ApiSecret)) return false;
        return true;
    }

    public async Task<AnalyticsDispatchResult> DispatchAsync(
        AnalyticsEvent analyticsEvent,
        TenantAnalyticsSettings? settings,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Check if enabled
        if (!IsEnabled(settings))
        {
            return AnalyticsDispatchResult.Skipped(Provider, "GA4 not enabled for tenant");
        }

        try
        {
            var request = await BuildProviderDeliveryRequestAsync(analyticsEvent, settings!, cancellationToken);
            if (request == null)
            {
                return AnalyticsDispatchResult.Skipped(Provider, $"Event type {analyticsEvent.Type} not mapped to GA4");
            }

            var enqueueResult = await _providerDeliveryOutbox.EnqueueAsync(request, cancellationToken);
            stopwatch.Stop();

            _logger.LogDebug(
                "[Analytics Dispatch] GA4 enqueued | TenantId: {TenantId} | EventId: {EventId} | MessageId: {MessageId} | Duplicate: {Duplicate}",
                analyticsEvent.TenantId,
                analyticsEvent.EventId,
                enqueueResult.MessageId,
                enqueueResult.Duplicate);

            return AnalyticsDispatchResult.Ok(Provider, stopwatch.ElapsedMilliseconds);
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "[Analytics Dispatch] GA4 TIMEOUT | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId,
                analyticsEvent.EventId);

            return AnalyticsDispatchResult.Fail(Provider, "Request timeout", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex,
                "[Analytics Dispatch] GA4 ERROR | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId,
                analyticsEvent.EventId);

            return AnalyticsDispatchResult.Fail(Provider, ex.Message, stopwatch.ElapsedMilliseconds);
        }
    }

    public async Task<ProviderDeliveryRequest?> BuildProviderDeliveryRequestAsync(
        AnalyticsEvent analyticsEvent,
        TenantAnalyticsSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled(settings))
        {
            return null;
        }

        var ga4Event = await MapToGa4EventAsync(analyticsEvent, cancellationToken);
        if (ga4Event == null)
        {
            return null;
        }

        var payload = BuildGa4Payload(analyticsEvent, ga4Event);
        var payloadJson = JsonSerializer.Serialize(payload, ProviderJsonOptions);
        var requestType = ga4Event.Name ?? analyticsEvent.Type.ToString();
        return new ProviderDeliveryRequest(
            analyticsEvent.TenantId,
            Provider,
            requestType,
            $"{Provider}:{analyticsEvent.TenantId:D}:{analyticsEvent.EventId:D}:{requestType}",
            payloadJson,
            Activity.Current?.Id ?? analyticsEvent.EventId.ToString("D"),
            analyticsEvent.OccurredAtUtc,
            settings.Google!.DebugMode ? Ga4DebugEndpoint : Ga4Endpoint,
            HttpMethod.Post.Method,
            null,
            "application/json");
    }

    private Ga4Payload BuildGa4Payload(AnalyticsEvent analyticsEvent, Ga4Event ga4Event)
    {
        return new Ga4Payload
        {
            ClientId = analyticsEvent.Actor?.SessionId ?? Guid.NewGuid().ToString(),
            TimestampMicros = ((DateTimeOffset)analyticsEvent.OccurredAtUtc).ToUnixTimeMilliseconds() * 1000,
            NonPersonalizedAds = true, // Privacy-first
            Events = new[] { ga4Event }
        };
    }

    private async Task<Ga4Event?> MapToGa4EventAsync(AnalyticsEvent analyticsEvent, CancellationToken ct)
    {
        return analyticsEvent.Type switch
        {
            AnalyticsEventType.PageView => new Ga4Event
            {
                Name = "page_view",
                Params = new Dictionary<string, object?>
                {
                    ["page_location"] = analyticsEvent.Context?.PageUrl,
                    ["page_referrer"] = analyticsEvent.Context?.Referrer,
                    ["page_title"] = ExtractPageTitle(analyticsEvent.Context?.PageUrl),
                    ["language"] = analyticsEvent.Context?.Language
                }
            },

            AnalyticsEventType.ProductView => new Ga4Event
            {
                Name = "view_item",
                Params = new Dictionary<string, object?>
                {
                    ["currency"] = ResolveRequiredCurrency(analyticsEvent),
                    ["value"] = analyticsEvent.Payload?.Value,
                    ["items"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["item_id"] = analyticsEvent.Payload?.ProductId?.ToString(),
                            ["item_name"] = analyticsEvent.Payload?.ProductName,
                            ["item_variant"] = analyticsEvent.Payload?.VariantId?.ToString(),
                            ["price"] = analyticsEvent.Payload?.Value
                        }
                    }
                }
            },

            AnalyticsEventType.ProductImpression => new Ga4Event
            {
                Name = "view_item_list",
                Params = new Dictionary<string, object?>
                {
                    ["item_list_id"] = "product_list",
                    ["items"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["item_id"] = analyticsEvent.Payload?.ProductId?.ToString(),
                            ["item_name"] = analyticsEvent.Payload?.ProductName,
                            ["index"] = analyticsEvent.Payload?.Extra?.GetValueOrDefault("position")
                        }
                    }
                }
            },

            AnalyticsEventType.AddToCart => new Ga4Event
            {
                Name = "add_to_cart",
                Params = new Dictionary<string, object?>
                {
                    ["currency"] = ResolveRequiredCurrency(analyticsEvent),
                    ["value"] = analyticsEvent.Payload?.Value,
                    ["items"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["item_id"] = analyticsEvent.Payload?.ProductId?.ToString(),
                            ["item_name"] = analyticsEvent.Payload?.ProductName,
                            ["item_variant"] = analyticsEvent.Payload?.VariantId?.ToString(),
                            ["quantity"] = analyticsEvent.Payload?.Quantity,
                            ["price"] = analyticsEvent.Payload?.Value
                        }
                    }
                }
            },

            AnalyticsEventType.RemoveFromCart => new Ga4Event
            {
                Name = "remove_from_cart",
                Params = new Dictionary<string, object?>
                {
                    ["currency"] = ResolveRequiredCurrency(analyticsEvent),
                    ["items"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["item_id"] = analyticsEvent.Payload?.ProductId?.ToString(),
                            ["item_name"] = analyticsEvent.Payload?.ProductName,
                            ["item_variant"] = analyticsEvent.Payload?.VariantId?.ToString(),
                            ["quantity"] = analyticsEvent.Payload?.Quantity
                        }
                    }
                }
            },

            AnalyticsEventType.BeginCheckout => new Ga4Event
            {
                Name = "begin_checkout",
                Params = new Dictionary<string, object?>
                {
                    ["currency"] = ResolveRequiredCurrency(analyticsEvent),
                    ["value"] = analyticsEvent.Payload?.Value
                }
            },

            AnalyticsEventType.PurchaseCompleted => await MapPurchaseEventAsync(analyticsEvent, ct),

            AnalyticsEventType.SearchPerformed => new Ga4Event
            {
                Name = "search",
                Params = new Dictionary<string, object?>
                {
                    ["search_term"] = analyticsEvent.Payload?.SearchQuery
                }
            },

            AnalyticsEventType.FilterApplied => new Ga4Event
            {
                Name = "select_item",
                Params = new Dictionary<string, object?>
                {
                    ["item_list_name"] = "filter",
                    ["filters"] = analyticsEvent.Payload?.Filters != null
                        ? JsonSerializer.Serialize(analyticsEvent.Payload.Filters)
                        : null
                }
            },

            AnalyticsEventType.PaymentFailed => new Ga4Event
            {
                Name = "purchase_error", // Custom event
                Params = new Dictionary<string, object?>
                {
                    ["transaction_id"] = analyticsEvent.Payload?.OrderId?.ToString(),
                    ["error_message"] = analyticsEvent.Payload?.Extra?.GetValueOrDefault("error_message")
                }
            },

            _ => null
        };
    }

    private async Task<Ga4Event> MapPurchaseEventAsync(AnalyticsEvent analyticsEvent, CancellationToken ct)
    {
        decimal resolvedValue;
        IReadOnlyList<Ga4OrderItem> orderItems = Array.Empty<Ga4OrderItem>();
        
        // Phase M2: Try to resolve per-product override if OrderId is present
        if (analyticsEvent.Payload?.OrderId != null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                orderItems = await dbContext.OrderItems
                    .AsNoTracking()
                    .Where(oi => oi.OrderId == analyticsEvent.Payload.OrderId.Value)
                    .OrderBy(oi => oi.Id)
                    .Select(oi => new Ga4OrderItem(
                        oi.ProductId,
                        oi.VariantId,
                        oi.SKU,
                        oi.ProductName,
                        oi.VariantName,
                        oi.UnitPrice,
                        oi.Quantity,
                        oi.Total))
                    .ToListAsync(ct);

                if (orderItems.Any())
                {
                    resolvedValue = await _pixelValueResolver.ResolvePixelValueAsync(
                        analyticsEvent.TenantId,
                        orderItems
                            .Select(item => new OrderItemPixelInfo(item.ProductId, item.LineItemTotal))
                            .ToArray(),
                        PixelProvider.Google,
                        ct);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"GA4 purchase dispatch requires SQL order items for OrderId {analyticsEvent.Payload.OrderId.Value:D}.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch order items required for GA4 purchase payload.");
                throw;
            }
        }
        else
        {
            resolvedValue = await _pixelValueResolver.ResolvePixelValueAsync(
                analyticsEvent.TenantId,
                analyticsEvent.Payload?.Value ?? 0,
                PixelProvider.Google,
                ct);
        }

        return new Ga4Event
        {
            Name = "purchase",
            Params = new Dictionary<string, object?>
            {
                ["transaction_id"] = analyticsEvent.Payload?.OrderId?.ToString(),
                ["currency"] = ResolveRequiredCurrency(analyticsEvent),
                ["value"] = resolvedValue,
                ["items"] = BuildPurchaseItems(analyticsEvent, orderItems)
            }
        };
    }

    private static Ga4Item[] BuildPurchaseItems(
        AnalyticsEvent analyticsEvent,
        IReadOnlyList<Ga4OrderItem> orderItems)
    {
        if (orderItems.Any())
        {
            return orderItems
                .Select(item => new Ga4Item
                {
                    ItemId = !string.IsNullOrWhiteSpace(item.Sku)
                        ? item.Sku
                        : item.ProductId.ToString(),
                    ItemName = item.ProductName,
                    ItemVariant = item.VariantName ?? item.VariantId?.ToString(),
                    Price = item.UnitPrice,
                    Quantity = item.Quantity
                })
                .ToArray();
        }

        if (analyticsEvent.Payload?.ProductId != null || !string.IsNullOrWhiteSpace(analyticsEvent.Payload?.ProductName))
        {
            return
            [
                new Ga4Item
                {
                    ItemId = analyticsEvent.Payload.ProductSku ?? analyticsEvent.Payload.ProductId?.ToString(),
                    ItemName = analyticsEvent.Payload.ProductName,
                    ItemVariant = analyticsEvent.Payload.VariantId?.ToString(),
                    Price = analyticsEvent.Payload.Value,
                    Quantity = analyticsEvent.Payload.Quantity
                }
            ];
        }

        return Array.Empty<Ga4Item>();
    }

    private static string ResolveRequiredCurrency(AnalyticsEvent analyticsEvent)
    {
        return FinancialCurrencyGuard.RequireCurrency(
            analyticsEvent.Context?.Currency,
            "analytics.context.currency",
            $"tenantId:{analyticsEvent.TenantId:D};eventId:{analyticsEvent.EventId:D}");
    }

    private static string? ExtractPageTitle(string? pageUrl)
    {
        if (string.IsNullOrEmpty(pageUrl)) return null;
        try
        {
            var uri = new Uri(pageUrl);
            return uri.AbsolutePath.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? "Home";
        }
        catch
        {
            return null;
        }
    }

    #region GA4 Payload Models

    private class Ga4Payload
    {
        [JsonPropertyName("client_id")]
        public string? ClientId { get; set; }

        [JsonPropertyName("timestamp_micros")]
        public long TimestampMicros { get; set; }

        [JsonPropertyName("non_personalized_ads")]
        public bool NonPersonalizedAds { get; set; }

        [JsonPropertyName("events")]
        public Ga4Event[]? Events { get; set; }
    }

    private class Ga4Event
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("params")]
        public Dictionary<string, object?>? Params { get; set; }
    }

    private class Ga4Item
    {
        [JsonPropertyName("item_id")]
        public string? ItemId { get; set; }

        [JsonPropertyName("item_name")]
        public string? ItemName { get; set; }

        [JsonPropertyName("item_variant")]
        public string? ItemVariant { get; set; }

        [JsonPropertyName("price")]
        public decimal? Price { get; set; }

        [JsonPropertyName("quantity")]
        public int? Quantity { get; set; }
    }

    private sealed record Ga4OrderItem(
        Guid ProductId,
        Guid? VariantId,
        string Sku,
        string ProductName,
        string? VariantName,
        decimal UnitPrice,
        int Quantity,
        decimal LineItemTotal);

    #endregion
}
