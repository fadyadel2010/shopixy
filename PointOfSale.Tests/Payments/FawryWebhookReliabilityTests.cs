using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Configuration;
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
using PaymentRuntimeOrchestrator = PointOfSale.Core.Payments.Runtime.Domain.PaymentRuntimeOrchestrator;

namespace PointOfSale.Tests.Payments;

public sealed class FawryWebhookReliabilityTests : IDisposable
{
    private readonly TestDatabaseScope _databaseScope;
    private readonly IDataProtectionProvider _dataProtectionProvider = new EphemeralDataProtectionProvider();

    public FawryWebhookReliabilityTests()
    {
        _databaseScope = TestDatabaseScope.Open();
    }

    private const string FinancialAuthorityCode = "FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE";

    [Fact]
    public async Task Handle_PaidWebhook_FailsClosedWithStableAuthorityAndSkipsLifecycle()
    {
        // T-09: authenticated Fawry transport evidence is not clearing or
        // settlement authority. Success admission returns the stable permanent
        // rejection without entering the money-mutation lifecycle.
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

        var lifecycleService = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);

        var payload = BuildPayload(eventId, referenceNumber, "PAID");
        var controller = CreateController(dbContext, lifecycleService.Object, tenantId, payload, webhookSecret);

        var result = await controller.Handle(CancellationToken.None);

        var rejected = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, rejected.StatusCode);
        var response = Assert.IsType<WebhookResponseDto>(rejected.Value);
        Assert.False(response.Processed);
        Assert.Equal(FinancialAuthorityCode, response.ErrorCode);

        lifecycleService.Verify(
            service => service.HandlePaymentResultAsync(It.IsAny<HandlePaymentResultRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var log = Assert.Single(dbContext.PaymentWebhookLogs);
        Assert.Equal("fawry", log.Provider);
        Assert.Equal(eventId, log.ProviderEventId);
        Assert.Equal(WebhookProcessingStatus.Skipped, log.ProcessingStatus);
        Assert.Contains(FinancialAuthorityCode, log.ProcessingError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_DuplicateWebhook_ReplaysStableAuthorityRejection()
    {
        // T-09: the first success-shaped callback is rejected with the stable
        // authority code and kept as a Skipped audit log; the retry is an
        // idempotent duplicate that never enters the lifecycle.
        var tenantId = Guid.NewGuid();
        const string referenceNumber = "223456789";
        const string eventId = "fawry-event-2";
        const string webhookSecret = "fawry-secret";

        // Use a fresh context for seeding
        await using (var seedContext = CreateDbContext(tenantId))
        {
            await SeedAsync(seedContext, tenantId, referenceNumber, webhookSecret);
        }

        var lifecycleService = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);

        var payload = BuildPayload(eventId, referenceNumber, "PAID");

        // Use a fresh context for the first controller invocation
        await using var firstContext = CreateDbContext(tenantId);
        var firstController = CreateController(firstContext, lifecycleService.Object, tenantId, payload, webhookSecret);
        var firstResult = await firstController.Handle(CancellationToken.None);

        // Use a fresh context for the second controller invocation
        await using var secondContext = CreateDbContext(tenantId);
        var secondController = CreateController(secondContext, lifecycleService.Object, tenantId, payload, webhookSecret);
        var secondResult = await secondController.Handle(CancellationToken.None);

        var rejected = Assert.IsType<ObjectResult>(firstResult);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, rejected.StatusCode);
        var secondRejection = Assert.IsType<ObjectResult>(secondResult);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, secondRejection.StatusCode);
        var secondResponse = Assert.IsType<WebhookResponseDto>(secondRejection.Value);
        Assert.False(secondResponse.Processed);
        Assert.Equal(FinancialAuthorityCode, secondResponse.ErrorCode);

        lifecycleService.Verify(
            service => service.HandlePaymentResultAsync(It.IsAny<HandlePaymentResultRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // Use a fresh context for verification
        await using var verifyContext = CreateDbContext(tenantId);
        var log = Assert.Single(verifyContext.PaymentWebhookLogs);
        Assert.Equal(WebhookProcessingStatus.Skipped, log.ProcessingStatus);
        Assert.Equal(eventId, log.ProviderEventId);
    }

    [Fact]
    public async Task Handle_PaidWebhook_NeverEntersLifecycle()
    {
        // T-09: provider-confirmed amounts are transport evidence, not
        // settlement authority. The lifecycle money mutation is never invoked.
        var tenantId = Guid.NewGuid();
        const string referenceNumber = "423456789";
        const string eventId = "fawry-event-provider-authority";
        const string webhookSecret = "fawry-secret";

        await using (var seedContext = CreateDbContext(tenantId))
        {
            await SeedAsync(seedContext, tenantId, referenceNumber, webhookSecret);
        }

        var lifecycleService = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);

        var payload = BuildPayload(eventId, referenceNumber, "PAID", amount: 150m, currency: "EGP");
        await using var dbContext = CreateDbContext(tenantId);
        var controller = CreateController(dbContext, lifecycleService.Object, tenantId, payload, webhookSecret);

        var result = await controller.Handle(CancellationToken.None);

        var rejected = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, rejected.StatusCode);
        var response = Assert.IsType<WebhookResponseDto>(rejected.Value);
        Assert.False(response.Processed);
        Assert.Equal(FinancialAuthorityCode, response.ErrorCode);

        lifecycleService.Verify(
            service => service.HandlePaymentResultAsync(It.IsAny<HandlePaymentResultRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReplayAfterRejection_ReusesSameEventIdAndStaysSkipped()
    {
        // T-09: a retry of a fail-closed callback reuses the same audit log and
        // stays out of the lifecycle.
        var tenantId = Guid.NewGuid();
        const string referenceNumber = "323456789";
        const string eventId = "fawry-event-3";
        const string webhookSecret = "fawry-secret";

        // Use a fresh context for seeding
        await using (var seedContext = CreateDbContext(tenantId))
        {
            await SeedAsync(seedContext, tenantId, referenceNumber, webhookSecret);
        }

        var lifecycleService = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);

        var payload = BuildPayload(eventId, referenceNumber, "PAID");

        // Use a fresh context for the first controller invocation
        await using var firstContext = CreateDbContext(tenantId);
        var firstController = CreateController(firstContext, lifecycleService.Object, tenantId, payload, webhookSecret);
        var firstResult = await firstController.Handle(CancellationToken.None);

        // Use a fresh context for the second controller invocation
        await using var secondContext = CreateDbContext(tenantId);
        var secondController = CreateController(secondContext, lifecycleService.Object, tenantId, payload, webhookSecret);
        var secondResult = await secondController.Handle(CancellationToken.None);

        var firstRejection = Assert.IsType<ObjectResult>(firstResult);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, firstRejection.StatusCode);
        var secondRejection = Assert.IsType<ObjectResult>(secondResult);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, secondRejection.StatusCode);
        Assert.Equal(
            FinancialAuthorityCode,
            Assert.IsType<WebhookResponseDto>(secondRejection.Value).ErrorCode);

        lifecycleService.Verify(
            service => service.HandlePaymentResultAsync(It.IsAny<HandlePaymentResultRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // Use a fresh context for verification
        await using var verifyContext = CreateDbContext(tenantId);
        var log = Assert.Single(verifyContext.PaymentWebhookLogs);
        Assert.Equal(eventId, log.ProviderEventId);
        Assert.Equal(WebhookProcessingStatus.Skipped, log.ProcessingStatus);
    }

    [Fact]
    public async Task Handle_AuthorizedWebhook_FailsClosedWithStableAuthority()
    {
        var tenantId = Guid.NewGuid();
        const string referenceNumber = "523456789";
        const string eventId = "fawry-event-authorized";
        const string webhookSecret = "fawry-secret";

        await using (var seedContext = CreateDbContext(tenantId))
        {
            await SeedAsync(seedContext, tenantId, referenceNumber, webhookSecret);
        }

        var lifecycleService = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        var payload = BuildPayload(eventId, referenceNumber, "AUTHORIZED");
        await using var dbContext = CreateDbContext(tenantId);

        var result = await CreateController(
                dbContext,
                lifecycleService.Object,
                tenantId,
                payload,
                webhookSecret)
            .Handle(CancellationToken.None);

        var rejection = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, rejection.StatusCode);
        var response = Assert.IsType<WebhookResponseDto>(rejection.Value);
        Assert.False(response.Processed);
        Assert.Equal(FinancialAuthorityCode, response.ErrorCode);
        lifecycleService.Verify(
            service => service.HandlePaymentResultAsync(It.IsAny<HandlePaymentResultRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
        string currency = "EGP",
        string webhookSecret = "fawry-secret")
    {
        var amountText = amount.ToString("0.00", CultureInfo.InvariantCulture);
        var merchantReference = $"FWY-{referenceNumber}";
        const string paymentMethod = "PAYATFAWRY";
        var signaturePayload = string.Concat(
            referenceNumber,
            merchantReference,
            amountText,
            amountText,
            paymentStatus.ToUpperInvariant(),
            paymentMethod,
            referenceNumber);
        var messageSignature = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(signaturePayload + webhookSecret)))
            .ToLowerInvariant();

        return JsonSerializer.Serialize(new
        {
            requestId = eventId,
            fawryRefNumber = referenceNumber,
            merchantRefNumber = merchantReference,
            orderStatus = paymentStatus,
            paymentAmount = amount,
            orderAmount = amount,
            paymentMethod,
            paymentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            paymentReferenceNumber = referenceNumber,
            paymentCurrency = currency,
            messageSignature
        });
    }

    private static string ReadMessageSignature(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("messageSignature").GetString()!;
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
        httpContext.Request.Headers["X-Fawry-Signature"] = ReadMessageSignature(rawPayload);
        httpContext.Request.QueryString = new QueryString($"?tenant={tenantId:D}");

        var tenantResolver = new Mock<IFawryTenantConfigurationResolver>();
        tenantResolver
            .Setup(value => value.ResolveWebhookAuthorityAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FawryTenantWebhookAuthority(
                tenantId,
                Guid.NewGuid(),
                "merchant-code",
                webhookSecret,
                FawryEnvironment.Sandbox,
                true,
                new Uri("https://store.example.test/api/fawry/callback"),
                new Uri("https://store.example.test/api/webhooks/fawry")));
        var runtimeIntegration = new FawryWebhookRuntimeIntegrationService(
            dbContext,
            tenantResolver.Object,
            new FawryWebhookEvidenceAdapter(),
            new PaymentRuntimeOrchestrator(),
            NullLogger<FawryWebhookRuntimeIntegrationService>.Instance);

        return new FawryWebhookController(
            dbContext,
            new PaymentWebhookRequestPipeline(
                new PaymentWebhookTenantResolver(dbContext, NullLogger<PaymentWebhookTenantResolver>.Instance),
                NullLogger<PaymentWebhookRequestPipeline>.Instance),
            new FawrySensitiveArtifactProtector(_dataProtectionProvider),
            lifecycleService,
            runtimeIntegration,
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
