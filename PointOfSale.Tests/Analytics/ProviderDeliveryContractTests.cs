using System.Net.Http;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Analytics.Delivery;
using PointOfSale.Infrastructure.Analytics.Dispatchers;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Analytics;

public sealed class ProviderDeliveryContractTests
{
    private static readonly JsonSerializerOptions DocumentOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(AnalyticsEventType.PageView, "page_view", "google-analytics-page-view.json")]
    [InlineData(AnalyticsEventType.ProductView, "view_item", "google-analytics-view-content.json")]
    [InlineData(AnalyticsEventType.SearchPerformed, "search", "google-analytics-search.json")]
    [InlineData(AnalyticsEventType.AddToCart, "add_to_cart", "google-analytics-add-to-cart.json")]
    [InlineData(AnalyticsEventType.BeginCheckout, "begin_checkout", "google-analytics-initiate-checkout.json")]
    [InlineData(AnalyticsEventType.PurchaseCompleted, "purchase", "google-analytics-purchase.json")]
    public async Task GoogleAnalyticsDispatcher_SendsProviderCompliantGoldenPayloads(
        AnalyticsEventType eventType,
        string expectedRequestType,
        string goldenPayloadFile)
    {
        await using var dbContext = CreateDbContext();
        SeedOrderItems(dbContext);
        await dbContext.SaveChangesAsync();

        var outbox = new RecordingProviderDeliveryOutbox();
        var dispatcher = new GoogleAnalyticsDispatcher(
            outbox,
            new SumPixelValueResolver(),
            CreateScopeFactory(dbContext),
            NullLogger<GoogleAnalyticsDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync(
            CreateCanonicalEvent(eventType),
            new TenantAnalyticsSettings
            {
                Google = new GoogleAnalyticsSettings
                {
                    Enabled = true,
                    MeasurementId = "G-TEST 1",
                    ApiSecret = "secret+value"
                }
            },
            CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(outbox.Request);
        Assert.Equal(AnalyticsProvider.GoogleAnalytics, outbox.Request!.Provider);
        Assert.Equal(expectedRequestType, outbox.Request.RequestType);

        AssertJsonEquals(
            ReadGoldenPayload(goldenPayloadFile),
            outbox.Request.PayloadJson);
        Assert.DoesNotContain("clientId", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("timestampMicros", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("nonPersonalizedAds", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("user_id", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret+value", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.Equal("https://www.google-analytics.com/mp/collect", outbox.Request.EndpointUrl);
        Assert.DoesNotContain("secret+value", outbox.Request.EndpointUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AnalyticsEventType.PageView, "PageView", "meta-conversions-api-page-view.json")]
    [InlineData(AnalyticsEventType.ProductView, "ViewContent", "meta-conversions-api-view-content.json")]
    [InlineData(AnalyticsEventType.AddToCart, "AddToCart", "meta-conversions-api-add-to-cart.json")]
    [InlineData(AnalyticsEventType.BeginCheckout, "InitiateCheckout", "meta-conversions-api-initiate-checkout.json")]
    [InlineData(AnalyticsEventType.PurchaseCompleted, "Purchase", "meta-conversions-api-purchase.json")]
    public async Task MetaConversionsApiDispatcher_SendsProviderCompliantGoldenPayloads(
        AnalyticsEventType eventType,
        string expectedRequestType,
        string goldenPayloadFile)
    {
        await using var dbContext = CreateDbContext();
        SeedOrderItems(dbContext);
        await dbContext.SaveChangesAsync();

        var outbox = new RecordingProviderDeliveryOutbox();
        var dispatcher = new MetaConversionsApiDispatcher(
            outbox,
            new SumPixelValueResolver(),
            CreateScopeFactory(dbContext),
            NullLogger<MetaConversionsApiDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync(
            CreateCanonicalEvent(eventType),
            new TenantAnalyticsSettings
            {
                Meta = new MetaAnalyticsSettings
                {
                    Enabled = true,
                    PixelId = "123 456",
                    AccessToken = "token+value",
                    TestEventCode = "TEST123"
                }
            },
            CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(outbox.Request);
        Assert.Equal(AnalyticsProvider.Meta, outbox.Request!.Provider);
        Assert.Equal(expectedRequestType, outbox.Request.RequestType);

        AssertJsonEquals(
            ReadGoldenPayload(goldenPayloadFile),
            outbox.Request.PayloadJson);
        Assert.DoesNotContain("eventName", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("eventSourceUrl", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("userData", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("customData", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("token+value", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.Equal("https://graph.facebook.com/v18.0/123%20456/events", outbox.Request.EndpointUrl);
        Assert.DoesNotContain("token+value", outbox.Request.EndpointUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MetaConversionsApiDispatcher_CertifiesSearchAsUnsupportedCanonicalEvent()
    {
        await using var dbContext = CreateDbContext();
        var outbox = new RecordingProviderDeliveryOutbox();
        var dispatcher = new MetaConversionsApiDispatcher(
            outbox,
            new SumPixelValueResolver(),
            CreateScopeFactory(dbContext),
            NullLogger<MetaConversionsApiDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync(
            CreateCanonicalEvent(AnalyticsEventType.SearchPerformed),
            new TenantAnalyticsSettings
            {
                Meta = new MetaAnalyticsSettings
                {
                    Enabled = true,
                    PixelId = "123 456",
                    AccessToken = "token+value"
                }
            },
            CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(AnalyticsProvider.Meta, result.Provider);
        Assert.Contains("not mapped to Meta CAPI", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Null(outbox.Request);
    }

    [Theory]
    [InlineData(AnalyticsEventType.PageView, "PageView", "tiktok-events-api-page-view.json")]
    [InlineData(AnalyticsEventType.ProductView, "ViewContent", "tiktok-events-api-view-content.json")]
    [InlineData(AnalyticsEventType.SearchPerformed, "Search", "tiktok-events-api-search.json")]
    [InlineData(AnalyticsEventType.AddToCart, "AddToCart", "tiktok-events-api-add-to-cart.json")]
    [InlineData(AnalyticsEventType.BeginCheckout, "InitiateCheckout", "tiktok-events-api-initiate-checkout.json")]
    [InlineData(AnalyticsEventType.PurchaseCompleted, "Purchase", "tiktok-events-api-purchase.json")]
    public async Task TikTokAnalyticsDispatcher_SendsProviderCompliantGoldenPayloads(
        AnalyticsEventType eventType,
        string expectedRequestType,
        string goldenPayloadFile)
    {
        await using var dbContext = CreateDbContext();
        SeedOrderItems(dbContext);
        await dbContext.SaveChangesAsync();

        var outbox = new RecordingProviderDeliveryOutbox();
        var dispatcher = new TikTokAnalyticsDispatcher(
            outbox,
            new SumPixelValueResolver(),
            CreateScopeFactory(dbContext),
            NullLogger<TikTokAnalyticsDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync(
            CreateCanonicalEvent(eventType),
            new TenantAnalyticsSettings
            {
                TikTok = new TikTokAnalyticsSettings
                {
                    Enabled = true,
                    PixelId = "PIXEL 123",
                    AccessToken = "tiktok-token+value",
                    TestMode = true,
                    Endpoint = "https://business-api.tiktok.com/open_api/v1.3/event/track/",
                    Timeout = 17
                }
            },
            CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(outbox.Request);
        Assert.Equal(AnalyticsProvider.TikTok, outbox.Request!.Provider);
        Assert.Equal(expectedRequestType, outbox.Request.RequestType);
        Assert.Equal("https://business-api.tiktok.com/open_api/v1.3/event/track/", outbox.Request.EndpointUrl);
        Assert.Equal("POST", outbox.Request.HttpMethod);
        Assert.Equal("application/json", outbox.Request.ContentType);
        Assert.Equal(17, outbox.Request.TimeoutSeconds);

        AssertJsonEquals(
            ReadGoldenPayload(goldenPayloadFile),
            outbox.Request.PayloadJson);
        Assert.DoesNotContain("eventSource", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("eventSourceId", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("eventTime", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("eventId", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("tiktok-token+value", outbox.Request.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("tiktok-token+value", outbox.Request.EndpointUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TikTokHttpRequestCustomizer_AppliesEndpointHeadersAuthorizationAndContentType()
    {
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var customizer = new TikTokHttpRequestCustomizer(new StaticAnalyticsSettingsService(new TenantAnalyticsSettings
        {
            TenantId = tenantId,
            TikTok = new TikTokAnalyticsSettings
            {
                Enabled = true,
                PixelId = "PIXEL 123",
                AccessToken = "tiktok-token+value",
                Endpoint = "https://business-api.tiktok.com/open_api/v1.3/event/track/",
                Timeout = 17
            }
        }));

        var message = ProviderDeliveryOutboxMessage.Create(
            tenantId,
            AnalyticsProvider.TikTok.ToString(),
            "PageView",
            "TikTok:delivery",
            "hash",
            "{}",
            "correlation",
            endpointUrl: "https://placeholder.example/track",
            contentType: "application/json",
            timeoutSeconds: 17);

        using var request = new HttpRequestMessage(HttpMethod.Post, message.EndpointUrl)
        {
            Content = new StringContent(message.PayloadJson)
        };

        await customizer.CustomizeAsync(message, request, CancellationToken.None);

        Assert.Equal("https://business-api.tiktok.com/open_api/v1.3/event/track/", request.RequestUri!.ToString());
        Assert.True(request.Headers.TryGetValues("Access-Token", out var accessTokenValues));
        Assert.Equal("tiktok-token+value", Assert.Single(accessTokenValues));
        Assert.Contains(request.Headers.Accept, value => value.MediaType == "application/json");
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
    }

    private static AnalyticsEvent CreatePurchaseEvent() =>
        new()
        {
            EventId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Type = AnalyticsEventType.PurchaseCompleted,
            OccurredAtUtc = new DateTime(2026, 7, 22, 12, 34, 56, DateTimeKind.Utc),
            ReceivedAtUtc = new DateTime(2026, 7, 22, 12, 35, 00, DateTimeKind.Utc),
            Actor = new AnalyticsActor
            {
                SessionId = "checkout-session-123",
                CustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                IsAuthenticated = true
            },
            Context = new AnalyticsContext
            {
                PageUrl = "https://store.example.com/thank-you",
                Referrer = "https://store.example.com",
                Currency = "USD",
                Language = "en",
                DeviceType = "desktop",
                IpAddress = "203.0.113.0",
                UserAgent = "UnitTest/1.0"
            },
            Payload = new AnalyticsPayload
            {
                OrderId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Value = 37.50m
            }
        };

    private static AnalyticsEvent CreateCanonicalEvent(AnalyticsEventType type)
    {
        var analyticsEvent = CreatePurchaseEvent();
        analyticsEvent.Type = type;

        analyticsEvent.Payload = type switch
        {
            AnalyticsEventType.PageView => new AnalyticsPayload(),
            AnalyticsEventType.ProductView => new AnalyticsPayload
            {
                ProductId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                ProductName = "Red Shirt",
                ProductSku = "SKU-RED",
                Quantity = 1,
                Value = 10.00m
            },
            AnalyticsEventType.SearchPerformed => new AnalyticsPayload
            {
                SearchQuery = "red shoes"
            },
            AnalyticsEventType.AddToCart => new AnalyticsPayload
            {
                ProductId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                VariantId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                ProductName = "Red Shirt",
                ProductSku = "SKU-RED",
                Quantity = 2,
                Value = 20.00m
            },
            AnalyticsEventType.BeginCheckout => new AnalyticsPayload
            {
                Value = 37.50m
            },
            AnalyticsEventType.PurchaseCompleted => new AnalyticsPayload
            {
                OrderId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Value = 37.50m
            },
            _ => analyticsEvent.Payload
        };

        return analyticsEvent;
    }

    private static void SeedOrderItems(ApplicationDbContext dbContext)
    {
        dbContext.OrderItems.AddRange(
            new OrderItem
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                OrderId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                ProductId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                VariantId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                SKU = "SKU-RED",
                ProductName = "Red Shirt",
                VariantName = "Red / M",
                UnitPrice = 10.00m,
                Quantity = 2,
                Total = 20.00m
            },
            new OrderItem
            {
                Id = Guid.Parse("88888888-8888-8888-8888-888888888888"),
                OrderId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                ProductId = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                SKU = "SKU-BLUE",
                ProductName = "Blue Hat",
                UnitPrice = 17.50m,
                Quantity = 1,
                Total = 17.50m
            });
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static IServiceScopeFactory CreateScopeFactory(ApplicationDbContext dbContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static string ReadGoldenPayload(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Tests",
            "Analytics",
            "GoldenPayloads",
            fileName));

        return File.ReadAllText(path);
    }

    private static void AssertJsonEquals(string expected, string actual)
    {
        using var expectedDocument = JsonDocument.Parse(expected);
        using var actualDocument = JsonDocument.Parse(actual);

        Assert.Equal(
            JsonSerializer.Serialize(expectedDocument.RootElement, DocumentOptions),
            JsonSerializer.Serialize(actualDocument.RootElement, DocumentOptions));
    }

    private sealed class RecordingProviderDeliveryOutbox : IProviderDeliveryOutboxService
    {
        public ProviderDeliveryRequest? Request { get; private set; }

        public Task<ProviderDeliveryEnqueueResult> EnqueueAsync(
            ProviderDeliveryRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new ProviderDeliveryEnqueueResult(
                true,
                false,
                Guid.Parse("99999999-9999-9999-9999-999999999999"),
                ProviderDeliveryOutboxService.ComputePayloadHash(request.PayloadJson)));
        }

        public Task<bool> ReplayAsync(Guid messageId, string reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> ReplayAsync(Guid tenantId, Guid messageId, string reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> ReplayAsync(
            Guid tenantId,
            Guid messageId,
            string reason,
            string replaySource,
            string? operatorUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<int> ReplayBulkAsync(
            Guid? tenantId,
            AnalyticsProvider? provider,
            string? failureClass,
            int limit,
            string reason,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> ReplayBulkAsync(
            Guid? tenantId,
            AnalyticsProvider? provider,
            string? failureClass,
            int limit,
            string reason,
            string replaySource,
            string? operatorUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> ReplayAfterCredentialChangeAsync(
            Guid tenantId,
            AnalyticsProvider provider,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> ReplayAfterCredentialChangeAsync(
            Guid tenantId,
            AnalyticsProvider provider,
            string replaySource,
            string? operatorUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class SumPixelValueResolver : IPixelValueResolver
    {
        public Task<decimal> ResolvePixelValueAsync(
            Guid tenantId,
            decimal actualOrderTotal,
            PixelProvider provider,
            CancellationToken ct = default) =>
            Task.FromResult(actualOrderTotal);

        public Task<decimal> ResolvePixelValueAsync(
            Guid tenantId,
            IReadOnlyCollection<OrderItemPixelInfo> orderItems,
            PixelProvider provider,
            CancellationToken ct = default) =>
            Task.FromResult(orderItems.Sum(item => item.LineItemTotal));
    }

    private sealed class StaticAnalyticsSettingsService : IAnalyticsSettingsService
    {
        private readonly TenantAnalyticsSettings _settings;

        public StaticAnalyticsSettingsService(TenantAnalyticsSettings settings)
        {
            _settings = settings;
        }

        public Task<TenantAnalyticsSettings?> GetSettingsAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantAnalyticsSettings?>(_settings);

        public Task SaveSettingsAsync(
            TenantAnalyticsSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetProviderEnabledAsync(
            Guid tenantId,
            AnalyticsProvider provider,
            bool enabled,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InvalidateCacheAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
