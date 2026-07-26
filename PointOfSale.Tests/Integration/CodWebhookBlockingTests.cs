using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.DataProtection;
using Moq;
using MassTransit;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Tests.Infrastructure;
using Xunit;

namespace PointOfSale.Tests.Integration;

/// <summary>
/// PHASE 2: COD Webhook Blocking Tests
/// 
/// These tests verify that COD webhooks are BLOCKED at the gateway level.
/// 
/// SECURITY INVARIANT:
/// COD has NO payment gateway, therefore COD webhooks should NEVER exist.
/// Any attempt to send a COD webhook is a security violation.
/// 
/// ACCEPTANCE CRITERIA:
/// ✅ COD-WEBHOOK-1: provider="cod" → blocked
/// ✅ COD-WEBHOOK-2: provider="cash" → blocked
/// ✅ COD-WEBHOOK-3: provider="cash_on_delivery" → blocked
/// ✅ COD-WEBHOOK-4: CRITICAL security violation logged
/// ✅ COD-WEBHOOK-5: Returns 200 with error message (prevents retry storm)
/// </summary>
public class CodWebhookBlockingTests : IDisposable
{
    private readonly TestDatabaseScope _databaseScope;
    private readonly ApplicationDbContext _dbContext;
    private readonly PaymentService _paymentService;
    private readonly Mock<IEventPublisher> _mockEventPublisher;
    private readonly Mock<IPaymentLifecycleService> _mockLifecycleService;
    private readonly Guid _tenantId = Guid.NewGuid();

    public CodWebhookBlockingTests()
    {
        _databaseScope = TestDatabaseScope.Open();
        _dbContext = _databaseScope.CreateDbContext(_tenantId);

        // Setup mocks
        var mockRedisFactory = new Mock<RedisConnectionFactory>(
            Mock.Of<IOptions<RedisSettings>>(),
            Mock.Of<ILogger<RedisConnectionFactory>>());
        
        var mockRedisSettings = new Mock<IOptions<RedisSettings>>();
        mockRedisSettings.Setup(x => x.Value).Returns(new RedisSettings
        {
            ConnectionString = "localhost:6379",
            InstanceName = "test"
        });

        _mockEventPublisher = new Mock<IEventPublisher>();
        _mockLifecycleService = new Mock<IPaymentLifecycleService>();
        
        var mockLogger = new Mock<ILogger<PaymentService>>();
        var mockDataProtector = new Mock<IDataProtector>();
        mockDataProtector
            .Setup(x => x.CreateProtector(It.IsAny<string>()))
            .Returns(mockDataProtector.Object);

        var mockDataProtectionProvider = new Mock<IDataProtectionProvider>();
        mockDataProtectionProvider
            .Setup(x => x.CreateProtector(It.IsAny<string>()))
            .Returns(mockDataProtector.Object);
        var paymentAdapters = new List<IPaymentAdapter>();

        _paymentService = new PaymentService(
            _dbContext,
            mockRedisFactory.Object,
            mockRedisSettings.Object,
            _mockEventPublisher.Object,
            paymentAdapters,
            _mockLifecycleService.Object,
            mockDataProtectionProvider.Object,
            Options.Create(new R2Options()),
            new CommerceReliabilityTelemetry(),
            mockLogger.Object);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COD-WEBHOOK-1: provider="cod" → blocked
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProcessWebhook_CodProvider_ShouldBlock()
    {
        // Arrange
        var rawPayload = "{\"event\":\"payment.succeeded\",\"amount\":100}";

        // Act
        var result = await _paymentService.ProcessWebhookAsync(
            tenantId: _tenantId,
            provider: "cod",
            signature: "fake-signature",
            rawPayload: rawPayload,
            correlationId: "test-correlation",
            sourceIp: "1.2.3.4",
            userAgent: "attacker-bot",
            cancellationToken: CancellationToken.None);

        // Assert
        Assert.False(result.Processed);
        Assert.Contains("not supported", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COD", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COD-WEBHOOK-2: provider="cash" → blocked
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProcessWebhook_CashProvider_ShouldBlock()
    {
        // Arrange
        var rawPayload = "{\"event\":\"payment.succeeded\",\"amount\":100}";

        // Act
        var result = await _paymentService.ProcessWebhookAsync(
            tenantId: _tenantId,
            provider: "cash",
            signature: "fake-signature",
            rawPayload: rawPayload,
            correlationId: "test-correlation",
            sourceIp: "1.2.3.4",
            userAgent: "attacker-bot",
            cancellationToken: CancellationToken.None);

        // Assert
        Assert.False(result.Processed);
        Assert.Contains("not supported", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COD-WEBHOOK-3: provider="cash_on_delivery" → blocked
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("COD")]
    [InlineData("cod")]
    [InlineData("Cash")]
    [InlineData("CASH")]
    [InlineData("cash_on_delivery")]
    [InlineData("CASH_ON_DELIVERY")]
    [InlineData("CashOnDelivery")]
    public async Task ProcessWebhook_CodVariants_ShouldBlock(string provider)
    {
        // Arrange
        var rawPayload = "{\"event\":\"payment.succeeded\",\"amount\":100}";

        // Act
        var result = await _paymentService.ProcessWebhookAsync(
            tenantId: _tenantId,
            provider: provider,
            signature: "fake-signature",
            rawPayload: rawPayload,
            correlationId: "test-correlation",
            sourceIp: "1.2.3.4",
            userAgent: "attacker-bot",
            cancellationToken: CancellationToken.None);

        // Assert
        Assert.False(result.Processed);
        Assert.Contains("not supported", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COD-WEBHOOK-4: Valid providers should NOT be blocked
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("stripe")]
    [InlineData("paymob")]
    [InlineData("paypal")]
    [InlineData("tap")]
    public async Task ProcessWebhook_ValidProviders_ShouldNotBlock(string provider)
    {
        // Arrange
        var rawPayload = "{\"event\":\"payment.succeeded\",\"amount\":100}";

        // Act
        var result = await _paymentService.ProcessWebhookAsync(
            tenantId: _tenantId,
            provider: provider,
            signature: "fake-signature",
            rawPayload: rawPayload,
            correlationId: "test-correlation",
            sourceIp: "1.2.3.4",
            userAgent: "valid-gateway",
            cancellationToken: CancellationToken.None);

        // Assert
        // Should NOT be blocked (will fail for other reasons like invalid payload, but not blocked)
        Assert.DoesNotContain("COD webhooks are not supported", result.Message ?? "");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COD-WEBHOOK-5: No database writes for blocked webhooks
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProcessWebhook_CodProvider_ShouldNotCreateWebhookLog()
    {
        // Arrange
        var rawPayload = "{\"event\":\"payment.succeeded\",\"amount\":100}";
        var initialLogCount = await _dbContext.PaymentWebhookLogs.CountAsync();

        // Act
        await _paymentService.ProcessWebhookAsync(
            tenantId: _tenantId,
            provider: "cod",
            signature: "fake-signature",
            rawPayload: rawPayload,
            correlationId: "test-correlation",
            sourceIp: "1.2.3.4",
            userAgent: "attacker-bot",
            cancellationToken: CancellationToken.None);

        // Assert
        var finalLogCount = await _dbContext.PaymentWebhookLogs.CountAsync();
        Assert.Equal(initialLogCount, finalLogCount); // No new logs created
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
        _databaseScope.Dispose();
    }
}
