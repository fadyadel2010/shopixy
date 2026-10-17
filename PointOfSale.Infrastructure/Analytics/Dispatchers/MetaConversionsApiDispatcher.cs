using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Analytics.Delivery;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Analytics.Dispatchers;

/// <summary>
/// Dispatcher for Meta Conversions API (Server-Side).
/// 
/// SECURITY GUARANTEES:
/// - NO Meta Pixel on frontend
/// - NO Facebook SDK on storefront
/// - Server-side only dispatch
/// - All PII hashed with SHA256 before sending
/// - Pixel ID never exposed to client
/// 
/// META CAPI:
/// - Endpoint: https://graph.facebook.com/v18.0/{pixel-id}/events
/// - Requires: access_token
/// - Deduplication via event_id
/// 
/// EVENT MAPPING:
/// POS Event           → Meta Event
/// PageView            → PageView
/// ProductView         → ViewContent
/// AddToCart           → AddToCart
/// BeginCheckout       → InitiateCheckout
/// PurchaseCompleted   → Purchase
/// </summary>
public class MetaConversionsApiDispatcher : IAnalyticsDispatcher
{
    private const string MetaApiVersion = "v18.0";
    private const string MetaBaseUrl = "https://graph.facebook.com";

    private readonly IProviderDeliveryOutboxService _providerDeliveryOutbox;
    private readonly IPixelValueResolver _pixelValueResolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MetaConversionsApiDispatcher> _logger;

    private static readonly JsonSerializerOptions ProviderJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AnalyticsProvider Provider => AnalyticsProvider.Meta;
    public int Priority => 20; // After AdminRealtime and GA4

    public MetaConversionsApiDispatcher(
        IProviderDeliveryOutboxService providerDeliveryOutbox,
        IPixelValueResolver pixelValueResolver,
        IServiceScopeFactory scopeFactory,
        ILogger<MetaConversionsApiDispatcher> logger)
    {
        _providerDeliveryOutbox = providerDeliveryOutbox;
        _pixelValueResolver = pixelValueResolver;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public bool IsEnabled(TenantAnalyticsSettings? settings)
    {
        if (settings?.Meta == null) return false;
        if (!settings.Meta.Enabled) return false;
        if (string.IsNullOrWhiteSpace(settings.Meta.PixelId)) return false;
        if (string.IsNullOrWhiteSpace(settings.Meta.AccessToken)) return false;
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
            return AnalyticsDispatchResult.Skipped(Provider, "Meta CAPI not enabled for tenant");
        }

        try
        {
            var request = await BuildProviderDeliveryRequestAsync(analyticsEvent, settings!, cancellationToken);
            if (request == null)
            {
                return AnalyticsDispatchResult.Skipped(Provider, $"Event type {analyticsEvent.Type} not mapped to Meta CAPI");
            }

            var enqueueResult = await _providerDeliveryOutbox.EnqueueAsync(request, cancellationToken);
            stopwatch.Stop();

            _logger.LogDebug(
                "[Analytics Dispatch] Meta CAPI enqueued | TenantId: {TenantId} | EventId: {EventId} | MessageId: {MessageId} | Duplicate: {Duplicate}",
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
                "[Analytics Dispatch] Meta CAPI TIMEOUT | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId,
                analyticsEvent.EventId);

            return AnalyticsDispatchResult.Fail(Provider, "Request timeout", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex,
                "[Analytics Dispatch] Meta CAPI ERROR | TenantId: {TenantId} | EventId: {EventId}",
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

        var metaEvent = await MapToMetaEventAsync(analyticsEvent, cancellationToken);
        if (metaEvent == null)
        {
            return null;
        }

        var payload = BuildMetaPayload(analyticsEvent, metaEvent, settings.Meta!);
        var payloadJson = JsonSerializer.Serialize(payload, ProviderJsonOptions);
        var requestType = metaEvent.EventName ?? analyticsEvent.Type.ToString();
        return new ProviderDeliveryRequest(
            analyticsEvent.TenantId,
            Provider,
            requestType,
            $"{Provider}:{analyticsEvent.TenantId:D}:{analyticsEvent.EventId:D}:{requestType}",
            payloadJson,
            Activity.Current?.Id ?? analyticsEvent.EventId.ToString("D"),
            analyticsEvent.OccurredAtUtc,
            BuildMetaEndpoint(settings.Meta!),
            HttpMethod.Post.Method,
            null,
            "application/json");
    }

    private MetaPayload BuildMetaPayload(AnalyticsEvent analyticsEvent, MetaServerEvent metaEvent, MetaAnalyticsSettings settings)
    {
        // Build user data with hashed PII
        metaEvent.UserData = BuildUserData(analyticsEvent);
        
        // Set event time
        metaEvent.EventTime = ((DateTimeOffset)analyticsEvent.OccurredAtUtc).ToUnixTimeSeconds();
        
        // Use EventId for deduplication
        metaEvent.EventId = analyticsEvent.EventId.ToString();
        
        // Set event source URL
        metaEvent.EventSourceUrl = analyticsEvent.Context?.PageUrl;
        
        // Set action source
        metaEvent.ActionSource = "website";

        var payload = new MetaPayload
        {
            Data = new[] { metaEvent }
        };

        // Add test event code if configured
        if (!string.IsNullOrWhiteSpace(settings.TestEventCode))
        {
            payload.TestEventCode = settings.TestEventCode;
        }

        return payload;
    }

    private MetaUserData BuildUserData(AnalyticsEvent analyticsEvent)
    {
        var userData = new MetaUserData();

        // Session ID as external_id (hashed)
        if (!string.IsNullOrEmpty(analyticsEvent.Actor?.SessionId))
        {
            userData.ExternalId = HashSha256(analyticsEvent.Actor.SessionId);
        }

        // Client IP (hashed) - Note: We use anonymized IP
        if (!string.IsNullOrEmpty(analyticsEvent.Context?.IpAddress))
        {
            userData.ClientIpAddress = analyticsEvent.Context.IpAddress; // Already anonymized
        }

        // User agent
        if (!string.IsNullOrEmpty(analyticsEvent.Context?.UserAgent))
        {
            userData.ClientUserAgent = analyticsEvent.Context.UserAgent;
        }

        return userData;
    }

    private async Task<MetaServerEvent?> MapToMetaEventAsync(AnalyticsEvent analyticsEvent, CancellationToken ct)
    {
        return analyticsEvent.Type switch
        {
            AnalyticsEventType.PageView => new MetaServerEvent
            {
                EventName = "PageView"
            },

            AnalyticsEventType.ProductView => new MetaServerEvent
            {
                EventName = "ViewContent",
                CustomData = new MetaCustomData
                {
                    ContentIds = analyticsEvent.Payload?.ProductId != null 
                        ? new[] { analyticsEvent.Payload.ProductId.Value.ToString() } 
                        : null,
                    ContentName = analyticsEvent.Payload?.ProductName,
                    ContentType = "product",
                    Value = analyticsEvent.Payload?.Value,
                    Currency = ResolveRequiredCurrency(analyticsEvent)
                }
            },

            AnalyticsEventType.AddToCart => new MetaServerEvent
            {
                EventName = "AddToCart",
                CustomData = new MetaCustomData
                {
                    ContentIds = analyticsEvent.Payload?.ProductId != null 
                        ? new[] { analyticsEvent.Payload.ProductId.Value.ToString() } 
                        : null,
                    ContentName = analyticsEvent.Payload?.ProductName,
                    ContentType = "product",
                    Value = analyticsEvent.Payload?.Value,
                    Currency = ResolveRequiredCurrency(analyticsEvent),
                    NumItems = analyticsEvent.Payload?.Quantity
                }
            },

            AnalyticsEventType.BeginCheckout => new MetaServerEvent
            {
                EventName = "InitiateCheckout",
                CustomData = new MetaCustomData
                {
                    Value = analyticsEvent.Payload?.Value,
                    Currency = ResolveRequiredCurrency(analyticsEvent)
                }
            },

            AnalyticsEventType.PurchaseCompleted => await MapPurchaseEventAsync(analyticsEvent, ct),

            // Events not sent to Meta
            AnalyticsEventType.ProductImpression => null,
            AnalyticsEventType.SearchPerformed => null,
            AnalyticsEventType.FilterApplied => null,
            AnalyticsEventType.RemoveFromCart => null,
            AnalyticsEventType.PaymentFailed => null,

            _ => null
        };
    }

    private async Task<MetaServerEvent> MapPurchaseEventAsync(AnalyticsEvent analyticsEvent, CancellationToken ct)
    {
        decimal resolvedValue;
        IReadOnlyList<MetaOrderItem> orderItems = Array.Empty<MetaOrderItem>();
        
        // Phase M2: Try to resolve per-product override if OrderId is present
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
                    .Select(oi => new MetaOrderItem(
                        oi.ProductId,
                        oi.ProductName,
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
                        PixelProvider.Meta,
                        ct);
                }
                else
                {
                    throw new InvalidOperationException("Meta purchase dispatch requires same-tenant SQL order items.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch authoritative order items for Meta purchase payload.");
                throw;
            }
        }
        else
        {
             resolvedValue = await _pixelValueResolver.ResolvePixelValueAsync(
                analyticsEvent.TenantId,
                analyticsEvent.Payload?.Value ?? 0,
                PixelProvider.Meta,
                ct);
        }

        return new MetaServerEvent
        {
            EventName = "Purchase",
            CustomData = new MetaCustomData
            {
                Value = resolvedValue,
                Currency = ResolveRequiredCurrency(analyticsEvent),
                OrderId = analyticsEvent.Payload?.OrderId?.ToString(),
                ContentIds = orderItems.Select(item => item.ProductId.ToString()).ToArray(),
                ContentName = orderItems.Count == 1 ? orderItems[0].ProductName : null,
                ContentType = "product",
                NumItems = orderItems.Sum(item => item.Quantity)
            }
        };
    }

    private static string ResolveRequiredCurrency(AnalyticsEvent analyticsEvent)
    {
        return FinancialCurrencyGuard.RequireCurrency(
            analyticsEvent.Context?.Currency,
            "analytics.context.currency",
            $"tenantId:{analyticsEvent.TenantId:D};eventId:{analyticsEvent.EventId:D}");
    }

    private static string HashSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input.ToLowerInvariant().Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string BuildMetaEndpoint(MetaAnalyticsSettings settings) =>
        $"{MetaBaseUrl}/{MetaApiVersion}/{Uri.EscapeDataString(settings.PixelId)}/events";

    #region Meta CAPI Payload Models

    private class MetaPayload
    {
        [JsonPropertyName("data")]
        public MetaServerEvent[]? Data { get; set; }

        [JsonPropertyName("test_event_code")]
        public string? TestEventCode { get; set; }
    }

    private class MetaServerEvent
    {
        [JsonPropertyName("event_name")]
        public string? EventName { get; set; }

        [JsonPropertyName("event_time")]
        public long EventTime { get; set; }

        [JsonPropertyName("event_id")]
        public string? EventId { get; set; }

        [JsonPropertyName("event_source_url")]
        public string? EventSourceUrl { get; set; }

        [JsonPropertyName("action_source")]
        public string? ActionSource { get; set; }

        [JsonPropertyName("user_data")]
        public MetaUserData? UserData { get; set; }

        [JsonPropertyName("custom_data")]
        public MetaCustomData? CustomData { get; set; }
    }

    private class MetaUserData
    {
        [JsonPropertyName("external_id")]
        public string? ExternalId { get; set; }

        [JsonPropertyName("client_ip_address")]
        public string? ClientIpAddress { get; set; }

        [JsonPropertyName("client_user_agent")]
        public string? ClientUserAgent { get; set; }
    }

    private class MetaCustomData
    {
        [JsonPropertyName("content_ids")]
        public string[]? ContentIds { get; set; }

        [JsonPropertyName("content_name")]
        public string? ContentName { get; set; }

        [JsonPropertyName("content_type")]
        public string? ContentType { get; set; }

        [JsonPropertyName("value")]
        public decimal? Value { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("num_items")]
        public int? NumItems { get; set; }

        [JsonPropertyName("order_id")]
        public string? OrderId { get; set; }
    }

    private sealed record MetaOrderItem(
        Guid ProductId,
        string ProductName,
        int Quantity,
        decimal LineItemTotal);

    #endregion
}
