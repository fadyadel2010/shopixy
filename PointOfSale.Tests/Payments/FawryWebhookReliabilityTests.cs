using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Payments;

public sealed class FawryWebhookReliabilityTests : IDisposable
{
    private readonly TestDatabaseScope _databaseScope;
    private readonly IDataProtectionProvider _dataProtectionProvider = new EphemeralDataProtectionProvider();

    public FawryWebhookReliabilityTests()
    {
        _databaseScope = TestDatabaseScope.Open();
    }

    [Fact]
    public async Task Handle_WhenLifecycleFails_Returns500AndPersistsFailedWebhookLog()
    {
        var tenantId = Guid.NewGuid();
        const string referenceNumber = "123456789";
        const string eventId = "fawry-event-1";
        const string webhookSecret = "fawry-secret";

        // Use a fresh context for seeding
        await using (var seedContext = CreateDbContext(tenantId))
        {
            await SeedAsync(seedContext, tenantId, referenceNumber, webhookSecret);
        }

        // Use a fresh context for the controller invocation
        await using var dbContext = CreateDbContext(tenantId);

        var lifecycleService = new Mock<IPaymentLifecycleService>();
        lifecycleService
            .Setup(service => service.HandlePaymentResultAsync(It.IsAny<HandlePaymentResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentTransitionResult.Failed("transient processing failure"));

        var payload = BuildPayload(eventId, referenceNumber, "PAID");
        var controller = CreateController(dbContext, lifecycleService.Object, tenantId, payload, webhookSecret);

        var result = await controller.Handle(CancellationToken.None);

        var failed = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, failed.StatusCode);
        var response = Assert.IsType<WebhookResponseDto>(failed.Value);
        Assert.False(response.Processed);

        var log = Assert.Single(dbContext.PaymentWebhookLogs);
        Assert.Equal("fawry", log.Provider);
        Assert.Equal(eventId, log.ProviderEventId);
        Assert.Equal(WebhookProcessingStatus.Failed, log.ProcessingStatus);
        Assert.Contains("transient", log.ProcessingError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_DuplicateWebhook_IsProcessedOnceOnly()
    {
        var tenantId = Guid.NewGuid();
        const string referenceNumber = "223456789";
        const string eventId = "fawry-event-2";
        const string webhookSecret = "fawry-secret";

        // Use a fresh context for seeding
        await using (var seedContext = CreateDbContext(tenantId))
        {
            await SeedAsync(seedContext, tenantId, referenceNumber, webhookSecret);
        }

        var lifecycleService = new Mock<IPaymentLifecycleService>();
        lifecycleService
            .Setup(service => service.HandlePaymentResultAsync(It.IsAny<HandlePaymentResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentTransitionResult.Succeeded(
                PaymentStatus.Collected,
                stateChanged: true,
                inventoryAction: PaymentInventoryAction.Commit));

        var payload = BuildPayload(eventId, referenceNumber, "PAID");

        // Use a fresh context for the first controller invocation
        await using var firstContext = CreateDbContext(tenantId);
        var firstController = CreateController(firstContext, lifecycleService.Object, tenantId, payload, webhookSecret);
        var firstResult = await firstController.Handle(CancellationToken.None);

        // Use a fresh context for the second controller invocation
        await using var secondContext = CreateDbContext(tenantId);
        var secondController = CreateController(secondContext, lifecycleService.Object, tenantId, payload, webhookSecret);
        var secondResult = await secondController.Handle(CancellationToken.None);

        Assert.IsType<OkObjectResult>(firstResult);
        Assert.IsType<OkObjectResult>(secondResult);

        lifecycleService.Verify(
            service => service.HandlePaymentResultAsync(It.IsAny<HandlePaymentResultRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        // Use a fresh context for verification
        await using var verifyContext = CreateDbContext(tenantId);
        var log = Assert.Single(verifyContext.PaymentWebhookLogs);
        Assert.Equal(WebhookProcessingStatus.Completed, log.ProcessingStatus);
        Assert.Equal(eventId, log.ProviderEventId);
    }

    [Fact]
    public async Task Handle_PaidWebhook_PassesProviderConfirmedAmountAndCurrencyToLifecycle()
    {
        var tenantId = Guid.NewGuid();
        const string referenceNumber = "423456789";
        const string eventId = "fawry-event-provider-authority";
        const string webhookSecret = "fawry-secret";

        await using (var seedContext = CreateDbContext(tenantId))
        {
            await SeedAsync(seedContext, tenantId, referenceNumber, webhookSecret);
        }

        HandlePaymentResultRequest? capturedRequest = null;
        var lifecycleService = new Mock<IPaymentLifecycleService>();
        lifecycleService
            .Setup(service => service.HandlePaymentResultAsync(It.IsAny<HandlePaymentResultRequest>(), It.IsAny<CancellationToken>()))
            .Callback<HandlePaymentResultRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(PaymentTransitionResult.Succeeded(
                PaymentStatus.Collected,
                stateChanged: true,
                inventoryAction: PaymentInventoryAction.Commit));

        var payload = BuildPayload(eventId, referenceNumber, "PAID", amount: 150m, currency: "EGP");
        await using var dbContext = CreateDbContext(tenantId);
        var controller = CreateController(dbContext, lifecycleService.Object, tenantId, payload, webhookSecret);

        var result = await controller.Handle(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(capturedRequest);
        Assert.Equal(PaymentLifecycleOutcome.Succeeded, capturedRequest.Outcome);
        Assert.Equal(PaymentLifecycleSource.GatewayWebhook, capturedRequest.Source);
        Assert.Equal("fawry", capturedRequest.Gateway);
        Assert.Equal(150m, capturedRequest.ProviderAmount);
        Assert.Equal("EGP", capturedRequest.ProviderCurrency);
        Assert.Equal(150m, capturedRequest.Amount);
        Assert.Equal("EGP", capturedRequest.Currency);
    }

    [Fact]
    public async Task Handle_ReplayAfterFailure_ReusesSameEventIdAndCompletesIdempotently()
    {
        var tenantId = Guid.NewGuid();
        const string referenceNumber = "323456789";
        const string eventId = "fawry-event-3";
        const string webhookSecret = "fawry-secret";

        // Use a fresh context for seeding
        await using (var seedContext = CreateDbContext(tenantId))
        {
            await SeedAsync(seedContext, tenantId, referenceNumber, webhookSecret);
        }

        var lifecycleService = new Mock<IPaymentLifecycleService>();
        lifecycleService
            .SetupSequence(service => service.HandlePaymentResultAsync(It.IsAny<HandlePaymentResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentTransitionResult.Failed("first attempt crashed"))
            .ReturnsAsync(PaymentTransitionResult.Succeeded(
                PaymentStatus.Collected,
                stateChanged: false,
                inventoryAction: PaymentInventoryAction.Commit));

        var payload = BuildPayload(eventId, referenceNumber, "PAID");

        // Use a fresh context for the first controller invocation
        await using var firstContext = CreateDbContext(tenantId);
        var firstController = CreateController(firstContext, lifecycleService.Object, tenantId, payload, webhookSecret);
        var firstResult = await firstController.Handle(CancellationToken.None);

        // Use a fresh context for the second controller invocation
        await using var secondContext = CreateDbContext(tenantId);
        var secondController = CreateController(secondContext, lifecycleService.Object, tenantId, payload, webhookSecret);
        var secondResult = await secondController.Handle(CancellationToken.None);

        var firstFailure = Assert.IsType<ObjectResult>(firstResult);
        Assert.Equal(StatusCodes.Status500InternalServerError, firstFailure.StatusCode);
        Assert.IsType<OkObjectResult>(secondResult);

        lifecycleService.Verify(
            service => service.HandlePaymentResultAsync(It.IsAny<HandlePaymentResultRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        // Use a fresh context for verification
        await using var verifyContext = CreateDbContext(tenantId);
        var log = Assert.Single(verifyContext.PaymentWebhookLogs);
        Assert.Equal(eventId, log.ProviderEventId);
        Assert.Equal(WebhookProcessingStatus.Completed, log.ProcessingStatus);
    }

    private ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        return _databaseScope.CreateDbContext(tenantId);
    }

    private async Task SeedAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        string referenceNumber,
        string webhookSecret)
    {
        var orderId = Guid.NewGuid();

        dbContext.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = $"ORD-{referenceNumber}",
            PaymentMethod = PaymentMethod.Card,
            PaymentMethodCode = "FAWRY",
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            Currency = "EGP",
            Total = 150m
        });

        dbContext.PaymentIntents.Add(new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = orderId,
            Provider = PaymentProvider.Fawry,
            ProviderIntentId = $"FAWRY-{referenceNumber}",
            ReferenceCode = referenceNumber,
            PaymentReference = $"FWY-{referenceNumber}",
            IdempotencyKey = $"fawry:intent:{referenceNumber}",
            Amount = 150m,
            Currency = "EGP",
            PaymentMethod = PaymentMethod.Card,
            Status = PaymentIntentStatus.Pending
        });

        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "FAWRY",
            DisplayName = "Fawry",
            IsEnabled = true,
            SupportsOnline = true,
            ConfigEncrypted = ProtectFawryConfig(webhookSecret)
        });

        await dbContext.SaveChangesAsync();
    }

    private string ProtectFawryConfig(string webhookSecret)
    {
        var protector = _dataProtectionProvider.CreateProtector("PaymentMethodSecrets");
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["apiKey"] = "api-key",
            ["merchantCode"] = "merchant-code",
            ["webhookSecret"] = webhookSecret
        });

        return protector.Protect(json);
    }

    private static string BuildPayload(
        string eventId,
        string referenceNumber,
        string paymentStatus,
        decimal amount = 150m,
        string currency = "EGP")
    {
        return JsonSerializer.Serialize(new
        {
            eventId,
            referenceNumber,
            paymentStatus,
            paymentAmount = amount,
            paymentCurrency = currency
        });
    }

    private static string ComputeSignature(string payload, string webhookSecret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private FawryWebhookController CreateController(
        ApplicationDbContext dbContext,
        IPaymentLifecycleService lifecycleService,
        Guid tenantId,
        string rawPayload,
        string webhookSecret)
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
        httpContext.Request.Headers["X-Fawry-Signature"] = ComputeSignature(rawPayload, webhookSecret);
        httpContext.Request.QueryString = new QueryString($"?tenant={tenantId:D}");

        return new FawryWebhookController(
            dbContext,
            new PaymentWebhookRequestPipeline(
                new PaymentWebhookTenantResolver(dbContext, NullLogger<PaymentWebhookTenantResolver>.Instance),
                NullLogger<PaymentWebhookRequestPipeline>.Instance),
            new FawrySensitiveArtifactProtector(_dataProtectionProvider),
            lifecycleService,
            Mock.Of<IFawryWebhookRuntimeIntegrationService>(),
            NullLogger<FawryWebhookController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    public void Dispose()
    {
        _databaseScope.Dispose();
    }
}
