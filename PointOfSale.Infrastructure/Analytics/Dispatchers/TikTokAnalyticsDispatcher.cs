using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Analytics.Delivery;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Analytics.Dispatchers;

/// <summary>
/// Dispatcher for TikTok Events API.
///
/// Responsibilities:
/// - Map AnalyticsEvent to TikTok request payload
/// - Enqueue through the generic provider delivery outbox
/// - Never send HTTP directly
/// </summary>
public sealed class TikTokAnalyticsDispatcher : IAnalyticsDispatcher
{
    private readonly IProviderDeliveryOutboxService _providerDeliveryOutbox;
    private readonly IPixelValueResolver _pixelValueResolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TikTokAnalyticsDispatcher> _logger;

    private static readonly JsonSerializerOptions ProviderJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AnalyticsProvider Provider => AnalyticsProvider.TikTok;
    public int Priority => 30;

    public TikTokAnalyticsDispatcher(
        IProviderDeliveryOutboxService providerDeliveryOutbox,
        IPixelValueResolver pixelValueResolver,
        IServiceScopeFactory scopeFactory,
        ILogger<TikTokAnalyticsDispatcher> logger)
    {
        _providerDeliveryOutbox = providerDeliveryOutbox;
        _pixelValueResolver = pixelValueResolver;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public bool IsEnabled(TenantAnalyticsSettings? settings)
    {
        if (settings?.TikTok == null) return false;
        if (!settings.TikTok.Enabled) return false;
        if (string.IsNullOrWhiteSpace(settings.TikTok.PixelId)) return false;
        if (string.IsNullOrWhiteSpace(settings.TikTok.AccessToken)) return false;
        return true;
    }

    public async Task<AnalyticsDispatchResult> DispatchAsync(
        AnalyticsEvent analyticsEvent,
        TenantAnalyticsSettings? settings,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!IsEnabled(settings))
        {
            return AnalyticsDispatchResult.Skipped(Provider, "TikTok Events API not enabled for tenant");
        }

        try
        {
            var request = await BuildProviderDeliveryRequestAsync(analyticsEvent, settings!, cancellationToken);
            if (request == null)
            {
                return AnalyticsDispatchResult.Skipped(Provider, $"Event type {analyticsEvent.Type} not mapped to TikTok Events API");
            }

            var enqueueResult = await _providerDeliveryOutbox.EnqueueAsync(request, cancellationToken);
            stopwatch.Stop();

            _logger.LogDebug(
                "[Analytics Dispatch] TikTok enqueued | TenantId: {TenantId} | EventId: {EventId} | MessageId: {MessageId} | Duplicate: {Duplicate}",
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
                "[Analytics Dispatch] TikTok TIMEOUT | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId,
                analyticsEvent.EventId);

            return AnalyticsDispatchResult.Fail(Provider, "Request timeout", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "[Analytics Dispatch] TikTok ERROR | TenantId: {TenantId} | EventId: {EventId}",
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

        var tikTokEvent = await MapToTikTokEventAsync(analyticsEvent, cancellationToken);
        if (tikTokEvent == null)
        {
            return null;
        }

        var payload = new TikTokPayload
        {
            EventSource = "web",
            EventSourceId = settings.TikTok!.PixelId,
            Data = new[] { tikTokEvent }
        };

        var payloadJson = JsonSerializer.Serialize(payload, ProviderJsonOptions);
        var requestType = tikTokEvent.Event ?? analyticsEvent.Type.ToString();
        return new ProviderDeliveryRequest(
            analyticsEvent.TenantId,
            Provider,
            requestType,
            $"{Provider}:{analyticsEvent.TenantId:D}:{analyticsEvent.EventId:D}:{requestType}",
            payloadJson,
            Activity.Current?.Id ?? analyticsEvent.EventId.ToString("D"),
            analyticsEvent.OccurredAtUtc,
            ResolveEndpoint(settings.TikTok),
            HttpMethod.Post.Method,
            null,
            "application/json",
            ResolveTimeout(settings.TikTok));
    }

    private async Task<TikTokEvent?> MapToTikTokEventAsync(AnalyticsEvent analyticsEvent, CancellationToken ct)
    {
        var tikTokEvent = analyticsEvent.Type switch
        {
            AnalyticsEventType.PageView => new TikTokEvent
            {
                Event = "PageView",
                Properties = new TikTokProperties
                {
                    ContentType = "page",
                    Description = ExtractPageDescription(analyticsEvent.Context?.PageUrl)
                }
            },

            AnalyticsEventType.ProductView => new TikTokEvent
            {
                Event = "ViewContent",
                Properties = new TikTokProperties
                {
                    Contents = BuildSingleContent(analyticsEvent),
                    ContentType = "product",
                    Description = analyticsEvent.Payload?.ProductName,
                    Currency = ResolveRequiredCurrency(analyticsEvent),
                    Value = analyticsEvent.Payload?.Value
                }
            },

            AnalyticsEventType.SearchPerformed => new TikTokEvent
            {
                Event = "Search",
                Properties = new TikTokProperties
                {
                    Query = analyticsEvent.Payload?.SearchQuery,
                    ContentType = "product"
                }
            },

            AnalyticsEventType.AddToCart => new TikTokEvent
            {
                Event = "AddToCart",
                Properties = new TikTokProperties
                {
                    Contents = BuildSingleContent(analyticsEvent),
                    ContentType = "product",
                    Description = analyticsEvent.Payload?.ProductName,
                    Currency = ResolveRequiredCurrency(analyticsEvent),
                    Value = analyticsEvent.Payload?.Value
                }
            },

            AnalyticsEventType.BeginCheckout => new TikTokEvent
            {
                Event = "InitiateCheckout",
                Properties = new TikTokProperties
                {
                    Currency = ResolveRequiredCurrency(analyticsEvent),
                    Value = analyticsEvent.Payload?.Value
                }
            },

            AnalyticsEventType.PurchaseCompleted => await MapPurchaseEventAsync(analyticsEvent, ct),

            _ => null
        };

        if (tikTokEvent == null)
        {
            return null;
        }

        tikTokEvent.EventTime = ((DateTimeOffset)analyticsEvent.OccurredAtUtc).ToUnixTimeSeconds();
        tikTokEvent.EventId = analyticsEvent.EventId.ToString("D");
        tikTokEvent.User = BuildUser(analyticsEvent);
        tikTokEvent.Page = BuildPage(analyticsEvent);
        return tikTokEvent;
    }

    private async Task<TikTokEvent> MapPurchaseEventAsync(AnalyticsEvent analyticsEvent, CancellationToken ct)
    {
        decimal resolvedValue;
        IReadOnlyList<TikTokOrderItem> orderItems = Array.Empty<TikTokOrderItem>();

        if (analyticsEvent.Payload?.OrderId != null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                orderItems = await dbContext.OrderItems
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(oi => oi.OrderId == analyticsEvent.Payload.OrderId.Value && oi.Order.TenantId == analyticsEvent.TenantId && !oi.Order.IsDeleted && !oi.IsDeleted)
                    .OrderBy(oi => oi.Id)
                    .Select(oi => new TikTokOrderItem(
                        oi.ProductId,
                        oi.SKU,
                        oi.ProductName,
                        oi.UnitPrice,
                        oi.Quantity,
                        oi.Total))
                    .ToListAsync(ct);

                resolvedValue = orderItems.Any()
                    ? await _pixelValueResolver.ResolvePixelValueAsync(
                        analyticsEvent.TenantId,
                        orderItems
                            .Select(item => new OrderItemPixelInfo(item.ProductId, item.LineItemTotal))
                            .ToArray(),
                        PixelProvider.TikTok,
                        ct)
                    : throw new InvalidOperationException("TikTok purchase dispatch requires same-tenant SQL order items.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch authoritative order items for TikTok purchase payload.");
                throw;
            }
        }
        else
        {
            resolvedValue = await _pixelValueResolver.ResolvePixelValueAsync(
                analyticsEvent.TenantId,
                analyticsEvent.Payload?.Value ?? 0,
                PixelProvider.TikTok,
                ct);
        }

        return new TikTokEvent
        {
            Event = "Purchase",
            Properties = new TikTokProperties
            {
                Contents = BuildPurchaseContents(analyticsEvent, orderItems),
                ContentType = "product",
                Currency = ResolveRequiredCurrency(analyticsEvent),
                Value = resolvedValue,
                OrderId = analyticsEvent.Payload?.OrderId?.ToString()
            }
        };
    }

    private static TikTokUser BuildUser(AnalyticsEvent analyticsEvent)
    {
        var identifier = analyticsEvent.Actor?.CustomerId?.ToString("D")
            ?? analyticsEvent.Actor?.SessionId;

        return new TikTokUser
        {
            ExternalId = string.IsNullOrWhiteSpace(identifier) ? null : HashSha256(identifier),
            Ip = string.IsNullOrWhiteSpace(analyticsEvent.Context?.IpAddress) ? null : analyticsEvent.Context.IpAddress,
            UserAgent = string.IsNullOrWhiteSpace(analyticsEvent.Context?.UserAgent) ? null : analyticsEvent.Context.UserAgent
        };
    }

    private static TikTokPage BuildPage(AnalyticsEvent analyticsEvent) =>
        new()
        {
            Url = analyticsEvent.Context?.PageUrl,
            Referrer = analyticsEvent.Context?.Referrer
        };

    private static TikTokContent[]? BuildSingleContent(AnalyticsEvent analyticsEvent)
    {
        if (analyticsEvent.Payload?.ProductId == null &&
            string.IsNullOrWhiteSpace(analyticsEvent.Payload?.ProductSku) &&
            string.IsNullOrWhiteSpace(analyticsEvent.Payload?.ProductName))
        {
            return null;
        }

        return
        [
            new TikTokContent
            {
                ContentId = analyticsEvent.Payload.ProductSku ?? analyticsEvent.Payload.ProductId?.ToString(),
                ContentName = analyticsEvent.Payload.ProductName,
                ContentType = "product",
                Quantity = analyticsEvent.Payload.Quantity,
                Price = analyticsEvent.Payload.Value
            }
        ];
    }

    private static TikTokContent[]? BuildPurchaseContents(
        AnalyticsEvent analyticsEvent,
        IReadOnlyList<TikTokOrderItem> orderItems)
    {
        if (orderItems.Any())
        {
            return orderItems
                .Select(item => new TikTokContent
                {
                    ContentId = !string.IsNullOrWhiteSpace(item.Sku)
                        ? item.Sku
                        : item.ProductId.ToString(),
                    ContentName = item.ProductName,
                    ContentType = "product",
                    Quantity = item.Quantity,
                    Price = item.UnitPrice
                })
                .ToArray();
        }

        return BuildSingleContent(analyticsEvent);
    }

    private static string ResolveEndpoint(TikTokAnalyticsSettings settings)
    {
        var endpoint = string.IsNullOrWhiteSpace(settings.Endpoint)
            ? TikTokAnalyticsSettings.DefaultEndpoint
            : settings.Endpoint.Trim();

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("TikTok endpoint must be an absolute HTTP or HTTPS URL.");
        }

        return uri.ToString();
    }

    private static int ResolveTimeout(TikTokAnalyticsSettings settings) =>
        Math.Clamp(settings.Timeout <= 0 ? 30 : settings.Timeout, 1, 120);

    private static string ResolveRequiredCurrency(AnalyticsEvent analyticsEvent)
    {
        return FinancialCurrencyGuard.RequireCurrency(
            analyticsEvent.Context?.Currency,
            "analytics.context.currency",
            $"tenantId:{analyticsEvent.TenantId:D};eventId:{analyticsEvent.EventId:D}");
    }

    private static string? ExtractPageDescription(string? pageUrl)
    {
        if (string.IsNullOrWhiteSpace(pageUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segment = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();

        return string.IsNullOrWhiteSpace(segment) ? "home" : segment;
    }

    private static string HashSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input.ToLowerInvariant().Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed class TikTokPayload
    {
        [JsonPropertyName("event_source")]
        public string? EventSource { get; set; }

        [JsonPropertyName("event_source_id")]
        public string? EventSourceId { get; set; }

        [JsonPropertyName("data")]
        public TikTokEvent[]? Data { get; set; }
    }

    private sealed class TikTokEvent
    {
        [JsonPropertyName("event")]
        public string? Event { get; set; }

        [JsonPropertyName("event_time")]
        public long EventTime { get; set; }

        [JsonPropertyName("event_id")]
        public string? EventId { get; set; }

        [JsonPropertyName("user")]
        public TikTokUser? User { get; set; }

        [JsonPropertyName("page")]
        public TikTokPage? Page { get; set; }

        [JsonPropertyName("properties")]
        public TikTokProperties? Properties { get; set; }
    }

    private sealed class TikTokUser
    {
        [JsonPropertyName("external_id")]
        public string? ExternalId { get; set; }

        [JsonPropertyName("ip")]
        public string? Ip { get; set; }

        [JsonPropertyName("user_agent")]
        public string? UserAgent { get; set; }
    }

    private sealed class TikTokPage
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("referrer")]
        public string? Referrer { get; set; }
    }

    private sealed class TikTokProperties
    {
        [JsonPropertyName("contents")]
        public TikTokContent[]? Contents { get; set; }

        [JsonPropertyName("content_type")]
        public string? ContentType { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("query")]
        public string? Query { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("value")]
        public decimal? Value { get; set; }

        [JsonPropertyName("order_id")]
        public string? OrderId { get; set; }
    }

    private sealed class TikTokContent
    {
        [JsonPropertyName("content_id")]
        public string? ContentId { get; set; }

        [JsonPropertyName("content_name")]
        public string? ContentName { get; set; }

        [JsonPropertyName("content_type")]
        public string? ContentType { get; set; }

        [JsonPropertyName("quantity")]
        public int? Quantity { get; set; }

        [JsonPropertyName("price")]
        public decimal? Price { get; set; }
    }

    private sealed record TikTokOrderItem(
        Guid ProductId,
        string Sku,
        string ProductName,
        decimal UnitPrice,
        int Quantity,
        decimal LineItemTotal);
}
