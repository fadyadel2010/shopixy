using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Attributes;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Payments;

public sealed class WebhookSecurityHardeningTests
{
    [Fact]
    public void ProviderWebhookControllers_MustAllowAnonymousTenantIngress()
    {
        var webhookControllers = new[]
        {
            typeof(PaymentWebhooksController),
            typeof(StripeWebhooksController),
            typeof(PaymobWebhooksController),
            typeof(FawryWebhookController),
            typeof(ShippingWebhooksController)
        };

        foreach (var controller in webhookControllers)
        {
            Assert.Contains(
                controller.GetCustomAttributes(inherit: true),
                attribute => attribute is AllowAnonymousTenantAttribute);
        }
    }

    [Fact]
    public async Task StripeWebhook_WhenClaimedTenantDoesNotMatchResolvedTenant_ReturnsUnauthorized()
    {
        var claimedTenantId = Guid.NewGuid();
        var resolvedTenantId = Guid.NewGuid();
        var stripeService = new Mock<IStripePaymentService>(MockBehavior.Strict);
        var resolver = new Mock<IPaymentWebhookTenantResolver>();
        var rawPayload = """{"data":{"object":{"id":"pi_123"}}}""";

        resolver
            .Setup(service => service.ResolveAsync("stripe", rawPayload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookTenantResolutionResult(
                resolvedTenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "ProviderIntentId",
                "pi_123"));

        var controller = CreateStripeController(
            stripeService.Object,
            resolver.Object,
            rawPayload,
            tenantHeader: claimedTenantId,
            signature: "sig_valid");

        var result = await controller.HandleWebhook(CancellationToken.None);

        var unauthorized = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(unauthorized.Value);
        Assert.Equal("TENANT_MISMATCH", error.Code);
        stripeService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StripeWebhook_WhenSignatureInvalid_ReturnsUnauthorized()
    {
        var tenantId = Guid.NewGuid();
        var stripeService = new Mock<IStripePaymentService>();
        var resolver = new Mock<IPaymentWebhookTenantResolver>();
        var rawPayload = """{"data":{"object":{"id":"pi_456"}}}""";

        resolver
            .Setup(service => service.ResolveAsync("stripe", rawPayload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookTenantResolutionResult(
                tenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "ProviderIntentId",
                "pi_456"));

        stripeService
            .Setup(service => service.ProcessWebhookAsync(
                tenantId,
                "sig_invalid",
                rawPayload,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeWebhookSignatureException());

        var controller = CreateStripeController(
            stripeService.Object,
            resolver.Object,
            rawPayload,
            tenantHeader: null,
            signature: "sig_invalid");

        var result = await controller.HandleWebhook(CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        var error = Assert.IsType<ApiErrorResponse>(unauthorized.Value);
        Assert.Equal("INVALID_SIGNATURE", error.Code);
        Assert.Contains("signature", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StripeWebhook_WhenProcessingRemainsIncomplete_ReturnsInternalServerError()
    {
        var tenantId = Guid.NewGuid();
        var stripeService = new Mock<IStripePaymentService>();
        var resolver = new Mock<IPaymentWebhookTenantResolver>();
        var rawPayload = """{"data":{"object":{"id":"pi_789"}}}""";

        resolver
            .Setup(service => service.ResolveAsync("stripe", rawPayload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookTenantResolutionResult(
                tenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "ProviderIntentId",
                "pi_789"));

        stripeService
            .Setup(service => service.ProcessWebhookAsync(
                tenantId,
                "sig_retry",
                rawPayload,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeWebhookResultDto
            {
                Processed = false,
                Message = "Retry required",
                EventType = "payment_intent.succeeded"
            });

        var controller = CreateStripeController(
            stripeService.Object,
            resolver.Object,
            rawPayload,
            tenantHeader: null,
            signature: "sig_retry");

        var result = await controller.HandleWebhook(CancellationToken.None);

        var failed = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, failed.StatusCode);
        var response = Assert.IsType<StripeWebhookResultDto>(failed.Value);
        Assert.False(response.Processed);

        stripeService.Verify(
            service => service.ProcessWebhookAsync(
                tenantId,
                "sig_retry",
                rawPayload,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PaymobWebhook_WhenClaimedTenantDoesNotMatchResolvedTenant_ReturnsUnauthorized()
    {
        var claimedTenantId = Guid.NewGuid();
        var resolvedTenantId = Guid.NewGuid();
        var paymobService = new Mock<IPaymobPaymentService>(MockBehavior.Strict);
        var resolver = new Mock<IPaymentWebhookTenantResolver>();
        var rawPayload = """{"obj":{"order":{"id":"paymob-order-123"}}}""";

        resolver
            .Setup(service => service.ResolveAsync("paymob", rawPayload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookTenantResolutionResult(
                resolvedTenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "ProviderIntentId",
                "paymob-order-123"));

        var controller = CreatePaymobController(
            paymobService.Object,
            resolver.Object,
            rawPayload,
            tenantHeader: claimedTenantId,
            hmac: "hmac_valid");

        var result = await controller.HandleWebhook(CancellationToken.None);

        var unauthorized = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(unauthorized.Value);
        Assert.Equal("TENANT_MISMATCH", error.Code);
        paymobService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PaymobWebhook_WhenHmacMissing_ReturnsUnauthorized()
    {
        var paymobService = new Mock<IPaymobPaymentService>(MockBehavior.Strict);
        var resolver = new Mock<IPaymentWebhookTenantResolver>(MockBehavior.Strict);
        var rawPayload = """{"obj":{"order":{"id":"paymob-order-456"}}}""";

        var controller = CreatePaymobController(
            paymobService.Object,
            resolver.Object,
            rawPayload,
            tenantHeader: null,
            hmac: null);

        var result = await controller.HandleWebhook(CancellationToken.None);

        var unauthorized = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(unauthorized.Value);
        Assert.Equal("INVALID_SIGNATURE", error.Code);
        resolver.VerifyNoOtherCalls();
        paymobService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PaymobWebhook_WhenProcessingRemainsIncomplete_ReturnsInternalServerError()
    {
        var tenantId = Guid.NewGuid();
        var paymobService = new Mock<IPaymobPaymentService>();
        var resolver = new Mock<IPaymentWebhookTenantResolver>();
        var rawPayload = """{"obj":{"order":{"id":"paymob-order-789"},"id":987654321}}""";

        resolver
            .Setup(service => service.ResolveAsync("paymob", rawPayload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookTenantResolutionResult(
                tenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "ProviderIntentId",
                "paymob-order-789"));

        paymobService
            .Setup(service => service.ProcessWebhookAsync(
                tenantId,
                "hmac_retry",
                rawPayload,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymobWebhookResultDto
            {
                Processed = false,
                Message = "Retry required",
                TransactionId = "987654321"
            });

        var controller = CreatePaymobController(
            paymobService.Object,
            resolver.Object,
            rawPayload,
            tenantHeader: null,
            hmac: "hmac_retry");

        var result = await controller.HandleWebhook(CancellationToken.None);

        var failed = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, failed.StatusCode);
        var response = Assert.IsType<PaymobWebhookResultDto>(failed.Value);
        Assert.False(response.Processed);

        paymobService.Verify(
            service => service.ProcessWebhookAsync(
                tenantId,
                "hmac_retry",
                rawPayload,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void PaymentWebhookLog_Model_MustOwnUniqueIdempotencyIndex()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new ApplicationDbContext(options);
        var entityType = dbContext.Model.FindEntityType(typeof(PaymentWebhookLog));

        Assert.NotNull(entityType);
        var index = Assert.Single(
            entityType!.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId");

        Assert.True(index.IsUnique);
        Assert.Equal(
            new[] { nameof(PaymentWebhookLog.TenantId), nameof(PaymentWebhookLog.Provider), nameof(PaymentWebhookLog.ProviderEventId) },
            index.Properties.Select(property => property.Name).ToArray());
    }

    [Fact]
    public async Task PaymobCallback_WhenStoredContextResolved_RedirectsToResolvedTenantPendingPage()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymobService = new Mock<IPaymobPaymentService>();
        var resolver = new Mock<IPaymentWebhookTenantResolver>(MockBehavior.Strict);

        paymobService
            .Setup(service => service.ValidateCallbackAsync(
                It.Is<Dictionary<string, string>>(parameters =>
                    parameters["order"] == "paymob-order-789" &&
                    parameters["id"] == "987654321" &&
                    parameters["hmac"] == "hmac_valid"),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymobCallbackResultDto
            {
                IsValid = true,
                TenantId = tenantId,
                OrderId = orderId,
                PaymentIntentId = Guid.NewGuid(),
                StorefrontBaseUrl = "https://tenant-a.example.com",
                TransactionId = "987654321"
            });

        var controller = CreatePaymobCallbackController(
            paymobService.Object,
            resolver.Object,
            "/api/webhooks/paymob/callback?order=paymob-order-789&id=987654321&hmac=hmac_valid",
            host: "wrong-tenant.example.com");

        var result = await controller.HandleCallback(CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"https://tenant-a.example.com/checkout/pending?orderId={orderId}", redirect.Url);
    }

    [Fact]
    public async Task PaymobCallback_WhenStoredContextUsesCustomDomain_RedirectsToCustomDomainPendingPage()
    {
        var paymobService = new Mock<IPaymobPaymentService>();
        var resolver = new Mock<IPaymentWebhookTenantResolver>(MockBehavior.Strict);
        var orderId = Guid.NewGuid();

        paymobService
            .Setup(service => service.ValidateCallbackAsync(
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymobCallbackResultDto
            {
                IsValid = true,
                TenantId = Guid.NewGuid(),
                OrderId = orderId,
                PaymentIntentId = Guid.NewGuid(),
                StorefrontBaseUrl = "https://shop.customer-domain.com/",
                TransactionId = "txn-custom-domain"
            });

        var controller = CreatePaymobCallbackController(
            paymobService.Object,
            resolver.Object,
            "/api/webhooks/paymob/callback?order=paymob-order-custom&id=txn-custom-domain&hmac=hmac_valid",
            host: "api.internal.example.com");

        var result = await controller.HandleCallback(CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"https://shop.customer-domain.com/checkout/pending?orderId={orderId}", redirect.Url);
    }

    [Fact]
    public async Task PaymobCallback_WhenStoredContextMissing_ReturnsSafeErrorPage()
    {
        var paymobService = new Mock<IPaymobPaymentService>();
        var resolver = new Mock<IPaymentWebhookTenantResolver>(MockBehavior.Strict);

        paymobService
            .Setup(service => service.ValidateCallbackAsync(
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymobCallbackResultDto
            {
                IsValid = false,
                Message = "Payment callback context was not found."
            });

        var controller = CreatePaymobCallbackController(
            paymobService.Object,
            resolver.Object,
            "/api/webhooks/paymob/callback?order=missing-order&id=missing-txn&hmac=hmac_valid",
            host: "wrong-tenant.example.com");

        var result = await controller.HandleCallback(CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
        Assert.Contains("Unable to verify this payment return", content.Content, StringComparison.Ordinal);
        Assert.Contains("valid tenant-specific routing context", content.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PaymobCallback_ReplayUsesSameResolvedTenantRedirect()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymobService = new Mock<IPaymobPaymentService>();
        var resolver = new Mock<IPaymentWebhookTenantResolver>(MockBehavior.Strict);

        paymobService
            .Setup(service => service.ValidateCallbackAsync(
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymobCallbackResultDto
            {
                IsValid = true,
                TenantId = tenantId,
                OrderId = orderId,
                PaymentIntentId = Guid.NewGuid(),
                StorefrontBaseUrl = "https://tenant-replay.example.com",
                TransactionId = "txn-replay"
            });

        var controllerA = CreatePaymobCallbackController(
            paymobService.Object,
            resolver.Object,
            "/api/webhooks/paymob/callback?order=paymob-order-replay&id=txn-replay&hmac=hmac_valid",
            host: "wrong-tenant.example.com");
        var controllerB = CreatePaymobCallbackController(
            paymobService.Object,
            resolver.Object,
            "/api/webhooks/paymob/callback?order=paymob-order-replay&id=txn-replay&hmac=hmac_valid",
            host: "another-wrong-tenant.example.com");

        var firstResult = await controllerA.HandleCallback(CancellationToken.None);
        var secondResult = await controllerB.HandleCallback(CancellationToken.None);

        var firstRedirect = Assert.IsType<RedirectResult>(firstResult);
        var secondRedirect = Assert.IsType<RedirectResult>(secondResult);
        Assert.Equal(firstRedirect.Url, secondRedirect.Url);
        Assert.Equal($"https://tenant-replay.example.com/checkout/pending?orderId={orderId}", firstRedirect.Url);
    }

    [Fact]
    public async Task GenericWebhook_MustNotBypassPaymentServiceSignatureVerification()
    {
        var tenantId = Guid.NewGuid();
        var paymentService = new Mock<IPaymentService>(MockBehavior.Strict);
        var resolver = new Mock<IPaymentWebhookTenantResolver>();
        var rawPayload = """{"paymentIntentId":"pi_generic_123"}""";

        resolver
            .Setup(service => service.ResolveAsync("stripe", rawPayload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookTenantResolutionResult(
                tenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "ProviderIntentId",
                "pi_generic_123"));

        paymentService
            .Setup(service => service.ProcessWebhookAsync(
                tenantId,
                "stripe",
                "sig_valid",
                rawPayload,
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookResponseDto
            {
                Processed = true,
                Message = "Processed"
            });

        var controller = CreateGenericController(
            paymentService.Object,
            resolver.Object,
            rawPayload,
            tenantHeader: null,
            signature: "sig_valid");

        var result = await controller.HandleWebhook("stripe", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<WebhookResponseDto>(ok.Value);
        Assert.True(response.Processed);
        paymentService.VerifyAll();
    }

    [Fact]
    public async Task GenericWebhook_WhenProcessingRemainsIncomplete_ReturnsInternalServerError()
    {
        var tenantId = Guid.NewGuid();
        var paymentService = new Mock<IPaymentService>();
        var resolver = new Mock<IPaymentWebhookTenantResolver>();
        var rawPayload = """{"paymentIntentId":"pi_generic_retry"}""";

        resolver
            .Setup(service => service.ResolveAsync("stripe", rawPayload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookTenantResolutionResult(
                tenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "ProviderIntentId",
                "pi_generic_retry"));

        paymentService
            .Setup(service => service.ProcessWebhookAsync(
                tenantId,
                "stripe",
                "sig_retry",
                rawPayload,
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookResponseDto
            {
                Processed = false,
                Message = "Retry required"
            });

        var controller = CreateGenericController(
            paymentService.Object,
            resolver.Object,
            rawPayload,
            tenantHeader: null,
            signature: "sig_retry");

        var result = await controller.HandleWebhook("stripe", CancellationToken.None);

        var failed = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, failed.StatusCode);
        var response = Assert.IsType<WebhookResponseDto>(failed.Value);
        Assert.False(response.Processed);

        paymentService.Verify(
            service => service.ProcessWebhookAsync(
                tenantId,
                "stripe",
                "sig_retry",
                rawPayload,
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static StripeWebhooksController CreateStripeController(
        IStripePaymentService stripePaymentService,
        IPaymentWebhookTenantResolver resolver,
        string rawPayload,
        Guid? tenantHeader,
        string? signature)
    {
        var services = new ServiceCollection()
            .AddSingleton(new TenantContext())
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services
        };
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(rawPayload));

        if (tenantHeader.HasValue)
        {
            httpContext.Request.Headers["X-Tenant-Id"] = tenantHeader.Value.ToString("D");
        }

        if (!string.IsNullOrWhiteSpace(signature))
        {
            httpContext.Request.Headers["Stripe-Signature"] = signature;
        }

        var orchestration = new Mock<ICommerceHubPaymentOrchestrationService>();
        orchestration
            .Setup(service => service.ProcessWebhookAsync(
                CommerceHubPaymentMethods.Stripe,
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubPaymentWebhookResultDto
            {
                Processed = true,
                Provider = CommerceHubPaymentMethods.Stripe,
                Message = "processed"
            });

        return new StripeWebhooksController(
            orchestration.Object,
            new PaymentWebhookRequestPipeline(
                resolver,
                NullLogger<PaymentWebhookRequestPipeline>.Instance),
            NullLogger<StripeWebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static PaymobWebhooksController CreatePaymobController(
        IPaymobPaymentService paymobPaymentService,
        IPaymentWebhookTenantResolver resolver,
        string rawPayload,
        Guid? tenantHeader,
        string? hmac)
    {
        var services = new ServiceCollection()
            .AddSingleton(new TenantContext())
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services
        };
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(rawPayload));

        if (tenantHeader.HasValue)
        {
            httpContext.Request.Headers["X-Tenant-Id"] = tenantHeader.Value.ToString("D");
        }

        if (!string.IsNullOrWhiteSpace(hmac))
        {
            httpContext.Request.Headers["Hmac"] = hmac;
        }

        var orchestration = new Mock<ICommerceHubPaymentOrchestrationService>();
        orchestration
            .Setup(service => service.ProcessWebhookAsync(
                CommerceHubPaymentMethods.Paymob,
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubPaymentWebhookResultDto
            {
                Processed = true,
                Provider = CommerceHubPaymentMethods.Paymob,
                Message = "processed"
            });

        return new PaymobWebhooksController(
            orchestration.Object,
            new PaymentWebhookRequestPipeline(
                resolver,
                NullLogger<PaymentWebhookRequestPipeline>.Instance),
            NullLogger<PaymobWebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static PaymobWebhooksController CreatePaymobCallbackController(
        IPaymobPaymentService paymobPaymentService,
        IPaymentWebhookTenantResolver resolver,
        string pathAndQuery,
        string host)
    {
        var services = new ServiceCollection()
            .AddSingleton(new TenantContext())
            .BuildServiceProvider();

        var splitIndex = pathAndQuery.IndexOf('?');
        var path = splitIndex >= 0 ? pathAndQuery[..splitIndex] : pathAndQuery;
        var queryString = splitIndex >= 0 ? pathAndQuery[splitIndex..] : string.Empty;

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services
        };
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString(host);
        httpContext.Request.Path = path;
        httpContext.Request.QueryString = new QueryString(queryString);

        var orchestration = new Mock<ICommerceHubPaymentOrchestrationService>();
        orchestration
            .Setup(service => service.ValidateCallbackAsync(
                CommerceHubPaymentMethods.Paymob,
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubPaymentCallbackResultDto
            {
                IsValid = false,
                Message = "Payment callback context was not found."
            });

        return new PaymobWebhooksController(
            orchestration.Object,
            new PaymentWebhookRequestPipeline(
                resolver,
                NullLogger<PaymentWebhookRequestPipeline>.Instance),
            NullLogger<PaymobWebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static PaymentWebhooksController CreateGenericController(
        IPaymentService paymentService,
        IPaymentWebhookTenantResolver resolver,
        string rawPayload,
        Guid? tenantHeader,
        string? signature)
    {
        var services = new ServiceCollection()
            .AddSingleton(new TenantContext())
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services
        };
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(rawPayload));

        if (tenantHeader.HasValue)
        {
            httpContext.Request.Headers["X-Tenant-Id"] = tenantHeader.Value.ToString("D");
        }

        if (!string.IsNullOrWhiteSpace(signature))
        {
            httpContext.Request.Headers["X-Signature"] = signature;
        }

        return new PaymentWebhooksController(
            paymentService,
            new PaymentWebhookRequestPipeline(
                resolver,
                NullLogger<PaymentWebhookRequestPipeline>.Instance),
            NullLogger<PaymentWebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }
}
