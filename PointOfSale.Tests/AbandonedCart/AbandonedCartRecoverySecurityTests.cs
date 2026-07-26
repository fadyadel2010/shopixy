using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Cart;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Services;
using AbandonedCartEntity = PointOfSale.Core.Entities.AbandonedCart;

namespace PointOfSale.Tests.AbandonedCart;

public sealed class AbandonedCartRecoverySecurityTests
{
    [Fact]
    public async Task RecoverCart_WhenTokenIsMalformed_IsRejected()
    {
        await using var context = CreateDbContext();
        var service = CreateService(context);
        var controller = CreateController(service);

        var result = await controller.RecoverCart("not-a-valid-token", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<CartRecoveryErrorResponse>(badRequest.Value);
        Assert.Equal("Invalid or expired recovery link", payload.Error);
    }

    [Fact]
    public async Task RecoverCart_WhenTokenIsTampered_IsRejected()
    {
        await using var context = CreateDbContext();
        var cart = await SeedRecoverableCartAsync(context);
        var service = CreateService(context);
        var controller = CreateController(service);

        var token = service.GenerateRecoveryToken(cart);
        var tamperedToken = token[..^1] + (token[^1] == 'A' ? "B" : "A");

        var result = await controller.RecoverCart(tamperedToken, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<CartRecoveryErrorResponse>(badRequest.Value);
        Assert.Equal("Invalid or expired recovery link", payload.Error);
    }

    [Fact]
    public async Task RecoverCart_WhenCartOwnershipChanges_IsRejected()
    {
        await using var context = CreateDbContext();
        var cart = await SeedRecoverableCartAsync(context);
        var service = CreateService(context);
        var controller = CreateController(service);

        var token = service.GenerateRecoveryToken(cart);
        cart.SessionId = Guid.NewGuid().ToString();
        await context.SaveChangesAsync();

        var result = await controller.RecoverCart(token, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<CartRecoveryErrorResponse>(badRequest.Value);
        Assert.Equal("Invalid or expired recovery link", payload.Error);
    }

    [Fact]
    public async Task RecoverCart_WhenLiveCartIsBackendAdmitted_ReturnsHydratableCartSnapshot()
    {
        await using var context = CreateDbContext();
        var cart = await SeedRecoverableCartAsync(context);
        var service = CreateService(context);
        var controller = CreateController(service, CreateLiveCartService(cart));

        var token = service.GenerateRecoveryToken(cart);

        var result = await controller.RecoverCart(token, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<CartRecoveryResponse>(ok.Value);
        var snapshot = Assert.IsType<RecoveredCartSnapshotDto>(payload.CartSnapshot);

        Assert.Equal(cart.TenantId, payload.TenantId);
        Assert.Equal(cart.CartId, payload.CartId);
        Assert.Equal(cart.SessionId, payload.SessionId);
        Assert.Equal("Your cart has been restored.", payload.Message);
        Assert.Equal(cart.TenantId, snapshot.TenantId);
        Assert.Equal(cart.CartId, snapshot.CartId);
        Assert.Equal("session", snapshot.OwnerType);
        Assert.Equal(cart.SessionId, snapshot.OwnerId);
        Assert.Equal("EGP", snapshot.Currency);
        Assert.Equal(2, snapshot.ItemCount);
        Assert.Equal(100m, snapshot.Subtotal);
        Assert.Equal(0m, snapshot.DiscountTotal);
        Assert.Equal(0m, snapshot.PromotionDiscountTotal);
        Assert.Equal(0m, snapshot.CartPromotionDiscountTotal);
        Assert.Equal(0m, snapshot.TotalDiscount);
        Assert.Equal(103m, snapshot.GrandTotal);
        Assert.Null(snapshot.AppliedCouponCode);
        Assert.Null(snapshot.AppliedCoupon);
        Assert.Null(snapshot.AppliedCartPromotion);

        var item = Assert.Single(snapshot.Items);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(50m, item.UnitPrice);
        Assert.Equal(100m, item.LineTotal);
        Assert.Equal("Recovered Product", item.ProductName);
        Assert.False(item.HasActivePromotion);
        Assert.Null(item.DiscountedUnitPrice);
        Assert.Equal(0m, item.LineDiscount);
        Assert.Equal(100m, item.DiscountedLineTotal);
    }

    [Fact]
    public async Task RecoverCart_WhenLiveCartIsUnavailable_RejectsStoredSnapshot()
    {
        await using var context = CreateDbContext();
        var cart = await SeedRecoverableCartAsync(context);
        var service = CreateService(context);
        var controller = CreateController(service);

        var token = service.GenerateRecoveryToken(cart);

        var result = await controller.RecoverCart(token, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<CartRecoveryErrorResponse>(badRequest.Value);
        Assert.Equal("Cart recovery data is invalid", payload.Error);
        Assert.Contains("could not safely restore", payload.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateRecoveryToken_ProducesUrlSafeToken()
    {
        await using var context = CreateDbContext();
        var cart = await SeedRecoverableCartAsync(context);
        var service = CreateService(context);

        var token = service.GenerateRecoveryToken(cart);

        Assert.DoesNotContain("+", token, StringComparison.Ordinal);
        Assert.DoesNotContain("/", token, StringComparison.Ordinal);
        Assert.DoesNotContain("=", token, StringComparison.Ordinal);
    }

    [Fact]
    public async Task N8nRecoveryOrchestrator_SignsWebhookWithTenantSecretAndReplayHeaders()
    {
        var tenantId = Guid.NewGuid();
        var abandonedCartId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        const string secret = "tenant-recovery-secret-with-enough-entropy";
        using var handler = new CapturingHandler();
        var orchestrator = CreateOrchestrator(handler, tenantId, secret);

        var accepted = await orchestrator.TriggerRecoveryWorkflowAsync(
            tenantId,
            abandonedCartId,
            cartId,
            "session-1",
            null,
            "+201000000000",
            null,
            "phone",
            "{\"items\":[]}",
            60,
            1440,
            new TimeSpan(9, 0, 0),
            new TimeSpan(21, 0, 0),
            "Recovery Store",
            "https://store.example.test",
            "https://store.example.test/recover-cart/token",
            CancellationToken.None);

        Assert.True(accepted);
        Assert.NotNull(handler.Request);
        Assert.NotNull(handler.Body);
        var request = handler.Request!;
        var body = handler.Body!;

        Assert.Equal("v1", Header(request, "X-Recovery-Signature-Version"));
        Assert.Equal(tenantId.ToString("D"), Header(request, "X-Recovery-Tenant-Id"));
        Assert.Equal(abandonedCartId.ToString("D"), Header(request, "X-Recovery-Abandoned-Cart-Id"));
        Assert.Equal(cartId.ToString("D"), Header(request, "X-Recovery-Cart-Id"));
        Assert.False(string.IsNullOrWhiteSpace(Header(request, "X-Recovery-Timestamp")));
        Assert.False(string.IsNullOrWhiteSpace(Header(request, "X-Recovery-Nonce")));

        var idempotencyKey = Header(request, "Idempotency-Key");
        Assert.Equal($"recovery-trigger:{tenantId:D}:{abandonedCartId:D}:{cartId:D}", idempotencyKey);

        var signatureBase = string.Join(
            ".",
            tenantId.ToString("D"),
            abandonedCartId.ToString("D"),
            cartId.ToString("D"),
            Header(request, "X-Recovery-Timestamp"),
            Header(request, "X-Recovery-Nonce"),
            idempotencyKey,
            body);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedSignature = "sha256=" + Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureBase))).ToLowerInvariant();

        Assert.Equal(expectedSignature, Header(request, "X-Recovery-Signature"));
    }

    [Fact]
    public async Task N8nRecoveryOrchestrator_WhenTenantSecretMissing_FailsClosedWithoutCallingWebhook()
    {
        var tenantId = Guid.NewGuid();
        using var handler = new CapturingHandler();
        var orchestrator = CreateOrchestrator(handler, tenantId, secret: null);

        var accepted = await orchestrator.TriggerRecoveryWorkflowAsync(
            tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session-1",
            null,
            "+201000000000",
            null,
            "phone",
            "{\"items\":[]}",
            60,
            1440,
            new TimeSpan(9, 0, 0),
            new TimeSpan(21, 0, 0),
            "Recovery Store",
            "https://store.example.test",
            "https://store.example.test/recover-cart/token",
            CancellationToken.None);

        Assert.False(accepted);
        Assert.Null(handler.Request);
    }

    [Fact]
    public void RecoveryClosure_IsTenantScopedAndCleanupIsScheduled()
    {
        var serviceSource = File.ReadAllText(InfrastructurePath("Services", "AbandonedCartService.cs"));
        Assert.Contains("WHERE \"\"TenantId\"\" = @p0", serviceSource, StringComparison.Ordinal);
        Assert.Contains("AND \"\"CartId\"\" = @p1", serviceSource, StringComparison.Ordinal);

        var consumerSource = File.ReadAllText(InfrastructurePath("Consumers", "AbandonedCart", "OrderRecoveryConsumer.cs"));
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", consumerSource, StringComparison.Ordinal);
        Assert.Contains("MarkAsRecoveredAsync(tenantId", consumerSource, StringComparison.Ordinal);

        var programSource = File.ReadAllText(ApiPath("Program.cs"));
        Assert.Contains("AddScoped<PointOfSale.Infrastructure.Jobs.AbandonedCartCleanupJob>", programSource, StringComparison.Ordinal);

        var hangfireSource = File.ReadAllText(ApiPath("Jobs", "HangfireInitializationHostedService.cs"));
        Assert.Contains("\"abandoned-cart-cleanup\"", hangfireSource, StringComparison.Ordinal);
        Assert.Contains("Cron.Daily(3)", hangfireSource, StringComparison.Ordinal);
    }

    private static StorefrontCartRecoveryController CreateController(
        IAbandonedCartService service,
        ICartService? liveCartService = null)
    {
        liveCartService ??= CreateLiveCartService();

        return new StorefrontCartRecoveryController(
            service,
            liveCartService,
            new AbandonedCartRecoveryTelemetry(),
            new CommerceReliabilityTelemetry(),
            NullLogger<StorefrontCartRecoveryController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static AbandonedCartService CreateService(ApplicationDbContext context)
    {
        return new AbandonedCartService(
            context,
            Mock.Of<ITenantAutomationSettingsService>(),
            new EphemeralDataProtectionProvider(),
            NullLogger<AbandonedCartService>.Instance);
    }

    private static N8nRecoveryOrchestrator CreateOrchestrator(
        CapturingHandler handler,
        Guid tenantId,
        string? secret)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AbandonedCart:N8nWebhookUrl"] = "https://n8n.example.test/webhook/recovery"
            })
            .Build();

        var settings = new Mock<ITenantAutomationSettingsService>();
        settings
            .Setup(x => x.GetSettingsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAutomationSettings
            {
                TenantId = tenantId,
                EnableAbandonedRecovery = true,
                N8nWebhookApiKey = secret
            });

        return new N8nRecoveryOrchestrator(
            new CapturingHttpClientFactory(handler),
            configuration,
            settings.Object,
            NullLogger<N8nRecoveryOrchestrator>.Instance);
    }

    private static async Task<AbandonedCartEntity> SeedRecoverableCartAsync(ApplicationDbContext context)
    {
        var cart = new AbandonedCartEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            CartId = Guid.NewGuid(),
            SessionId = Guid.NewGuid().ToString(),
            PhoneNumber = "+201000000000",
            PhoneVerified = true,
            ContactChannel = "phone",
            RecoveryStatus = AbandonedCartRecoveryStatus.Abandoned,
            LastActivityAtUtc = DateTime.UtcNow.AddHours(-3),
            CreatedAtUtc = DateTime.UtcNow.AddHours(-3),
            UpdatedAtUtc = DateTime.UtcNow.AddHours(-3)
        };

        cart.CartSnapshotJson = JsonSerializer.Serialize(BuildRecoveredCartSnapshot(cart));

        context.AbandonedCarts.Add(cart);
        await context.SaveChangesAsync();
        return cart;
    }

    private static RecoveredCartSnapshotDto BuildRecoveredCartSnapshot(AbandonedCartEntity cart)
    {
        var nowUtc = DateTime.UtcNow;

        return new RecoveredCartSnapshotDto
        {
            CartId = cart.CartId,
            TenantId = cart.TenantId,
            OwnerType = "session",
            OwnerId = cart.SessionId ?? string.Empty,
            ItemCount = 2,
            Subtotal = 100m,
            DiscountTotal = 5m,
            TaxTotal = 3m,
            GrandTotal = 88m,
            Currency = "EGP",
            LastUpdatedUtc = nowUtc,
            ExpiresAtUtc = nowUtc.AddDays(2),
            AppliedCouponCode = "SAVE5",
            AppliedCoupon = new RecoveredAppliedCouponDto
            {
                Code = "SAVE5",
                Type = "fixed",
                Value = 5m,
                Amount = 5m
            },
            PromotionDiscountTotal = 0m,
            CartPromotionDiscountTotal = 10m,
            AppliedCartPromotion = new RecoveredAppliedCartPromotionDto
            {
                PromotionId = Guid.NewGuid(),
                Name = "Recovered Promotion",
                Description = "Auto-applied recovery promotion",
                DiscountType = "fixed",
                DiscountValue = 10m,
                DiscountAmount = 10m,
                Badge = "10 OFF"
            },
            TotalDiscount = 15m,
            Items =
            [
                new RecoveredCartItemSnapshotDto
                {
                    ProductId = Guid.NewGuid(),
                    ProductName = "Recovered Product",
                    SKU = "RECOVERED-001",
                    Quantity = 2,
                    UnitPrice = 50m,
                    LineTotal = 100m,
                    ImageUrl = "https://cdn.example.test/recovered-item.png",
                    VariantAttributes =
                    [
                        new RecoveredCartVariantAttributeDto
                        {
                            Name = "Size",
                            Value = "Large"
                        }
                    ]
                }
            ]
        };
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static string Header(HttpRequestMessage request, string name)
    {
        Assert.True(request.Headers.TryGetValues(name, out var values), $"Missing header {name}");
        return Assert.Single(values);
    }

    private static string ApiPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.API" }.Concat(parts).ToArray()));

    private static string InfrastructurePath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.Infrastructure" }.Concat(parts).ToArray()));

    private static ICartService CreateLiveCartService(AbandonedCartEntity? cart = null)
    {
        var liveCartService = new Mock<ICartService>();
        var cartId = cart?.CartId ?? Guid.NewGuid();
        var tenantId = cart?.TenantId ?? Guid.NewGuid();
        var ownerId = cart?.CustomerId?.ToString("N") ?? cart?.SessionId ?? "non-authoritative-live-cart";
        liveCartService
            .Setup(service => service.GetCartAsync(
                It.IsAny<Guid>(),
                It.IsAny<CartOwner>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartDto
            {
                CartId = cartId,
                TenantId = tenantId,
                OwnerType = "session",
                OwnerId = ownerId,
                Currency = "EGP",
                Items =
                [
                    new CartItemDto
                    {
                        ProductId = Guid.NewGuid(),
                        Name = "Recovered Product",
                        SKU = "RECOVERED-001",
                        Quantity = 2,
                        UnitPrice = 50m,
                        AddedAtUtc = DateTime.UtcNow
                    }
                ],
                Subtotal = 100m,
                DiscountTotal = 0m,
                TaxTotal = 3m,
                GrandTotal = 103m,
                LastUpdatedUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
            });

        return liveCartService.Object;
    }

    private sealed class CapturingHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public CapturingHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }
}
