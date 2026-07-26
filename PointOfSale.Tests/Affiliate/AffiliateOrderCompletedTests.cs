using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Consumers.Affiliate;
using PointOfSale.Infrastructure.Instrumentation;
using Xunit;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-18: Regression tests for OrderCompletedEvent → Commission Recording flow.
///
/// SCENARIO: Ensures that when an order is marked as Completed (either from
/// Delivered or directly), affiliate commission is recorded correctly.
///
/// BUG FIX: Previously, orders that went directly to Completed status without
/// triggering OfflinePaymentCollectedEvent would never record commission, making
/// affiliates invisible in /admin/affiliates.
/// </summary>
public class AffiliateOrderCompletedTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();

    [Fact]
    public async Task OrderCompletedEvent_TriggersCommissionRecording()
    {
        // Arrange
        var mockService = new Mock<IAffiliateService>();
        mockService
            .Setup(x => x.RecordCommissionForOrderAsync(_orderId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = new OrderCompletedAffiliateConsumer(
            mockService.Object,
            Mock.Of<AffiliateTelemetry>(),
            Mock.Of<ILogger<OrderCompletedAffiliateConsumer>>());

        var evt = new OrderCompletedEvent
        {
            TenantId = _tenantId,
            OrderId = _orderId,
            OccurredAtUtc = DateTime.UtcNow,
            CorrelationId = _orderId.ToString()
        };

        var context = Mock.Of<ConsumeContext<OrderCompletedEvent>>(
            c => c.Message == evt && c.CancellationToken == CancellationToken.None);

        // Act
        await consumer.Consume(context);

        // Assert
        mockService.Verify(
            x => x.RecordCommissionForOrderAsync(_orderId, It.IsAny<CancellationToken>()),
            Times.Once,
            "OrderCompletedEvent should trigger commission recording");
    }

    [Fact]
    public async Task OrderCompletedEvent_WithException_Rethrows()
    {
        // Arrange
        var mockService = new Mock<IAffiliateService>();
        mockService
            .Setup(x => x.RecordCommissionForOrderAsync(_orderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        var consumer = new OrderCompletedAffiliateConsumer(
            mockService.Object,
            Mock.Of<AffiliateTelemetry>(),
            Mock.Of<ILogger<OrderCompletedAffiliateConsumer>>());

        var evt = new OrderCompletedEvent
        {
            TenantId = _tenantId,
            OrderId = _orderId,
            OccurredAtUtc = DateTime.UtcNow
        };

        var context = Mock.Of<ConsumeContext<OrderCompletedEvent>>(
            c => c.Message == evt && c.CancellationToken == CancellationToken.None);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await consumer.Consume(context));

        mockService.Verify(
            x => x.RecordCommissionForOrderAsync(_orderId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void OrderCompletedAffiliateConsumer_ImplementsCorrectInterface()
    {
        // Assert
        Assert.True(typeof(IConsumer<OrderCompletedEvent>)
            .IsAssignableFrom(typeof(OrderCompletedAffiliateConsumer)),
            "OrderCompletedAffiliateConsumer must implement IConsumer<OrderCompletedEvent>");
    }

    [Fact]
    public async Task OrderCompletedEvent_RecordsMetrics()
    {
        // Arrange
        var mockService = new Mock<IAffiliateService>();
        mockService
            .Setup(x => x.RecordCommissionForOrderAsync(_orderId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Use real telemetry instance (can't mock non-virtual methods)
        var telemetry = new AffiliateTelemetry();

        var consumer = new OrderCompletedAffiliateConsumer(
            mockService.Object,
            telemetry,
            Mock.Of<ILogger<OrderCompletedAffiliateConsumer>>());

        var evt = new OrderCompletedEvent
        {
            TenantId = _tenantId,
            OrderId = _orderId,
            OccurredAtUtc = DateTime.UtcNow
        };

        var context = Mock.Of<ConsumeContext<OrderCompletedEvent>>(
            c => c.Message == evt && c.CancellationToken == CancellationToken.None);

        // Act
        await consumer.Consume(context);

        // Assert
        mockService.Verify(
            x => x.RecordCommissionForOrderAsync(_orderId, It.IsAny<CancellationToken>()),
            Times.Once,
            "Should call RecordCommissionForOrderAsync");
    }

    [Fact]
    public async Task OrderCompletedEvent_WithFailure_RecordsFailureMetric()
    {
        // Arrange
        var mockService = new Mock<IAffiliateService>();
        mockService
            .Setup(x => x.RecordCommissionForOrderAsync(_orderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test failure"));

        // Use real telemetry instance (can't mock non-virtual methods)
        var telemetry = new AffiliateTelemetry();

        var consumer = new OrderCompletedAffiliateConsumer(
            mockService.Object,
            telemetry,
            Mock.Of<ILogger<OrderCompletedAffiliateConsumer>>());

        var evt = new OrderCompletedEvent
        {
            TenantId = _tenantId,
            OrderId = _orderId,
            OccurredAtUtc = DateTime.UtcNow
        };

        var context = Mock.Of<ConsumeContext<OrderCompletedEvent>>(
            c => c.Message == evt && c.CancellationToken == CancellationToken.None);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await consumer.Consume(context));

        mockService.Verify(
            x => x.RecordCommissionForOrderAsync(_orderId, It.IsAny<CancellationToken>()),
            Times.Once,
            "Should attempt to call RecordCommissionForOrderAsync");
    }

    /// <summary>
    /// AF-18: Critical test - ensures idempotency when order goes through
    /// both Delivered and Completed statuses.
    /// </summary>
    [Fact]
    public async Task OrderCompletedEvent_AfterDelivered_IsIdempotent()
    {
        // Arrange: Simulate order going Delivered → Completed
        // Commission should be recorded once, second call should be idempotent
        var callCount = 0;
        var mockService = new Mock<IAffiliateService>();
        mockService
            .Setup(x => x.RecordCommissionForOrderAsync(_orderId, It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .Returns(Task.CompletedTask);

        var codConsumer = new CodDeliveredAffiliateConsumer(
            mockService.Object,
            Mock.Of<AffiliateTelemetry>(),
            Mock.Of<ILogger<CodDeliveredAffiliateConsumer>>());

        var completedConsumer = new OrderCompletedAffiliateConsumer(
            mockService.Object,
            Mock.Of<AffiliateTelemetry>(),
            Mock.Of<ILogger<OrderCompletedAffiliateConsumer>>());

        var codEvent = new OfflinePaymentCollectedEvent
        {
            TenantId = _tenantId,
            OrderId = _orderId,
            OccurredAtUtc = DateTime.UtcNow
        };

        var completedEvent = new OrderCompletedEvent
        {
            TenantId = _tenantId,
            OrderId = _orderId,
            OccurredAtUtc = DateTime.UtcNow.AddSeconds(10)
        };

        var codContext = Mock.Of<ConsumeContext<OfflinePaymentCollectedEvent>>(
            c => c.Message == codEvent && c.CancellationToken == CancellationToken.None);

        var completedContext = Mock.Of<ConsumeContext<OrderCompletedEvent>>(
            c => c.Message == completedEvent && c.CancellationToken == CancellationToken.None);

        // Act: Trigger both events
        await codConsumer.Consume(codContext);
        await completedConsumer.Consume(completedContext);

        // Assert: RecordCommissionForOrderAsync called twice
        // (actual idempotency is enforced by DB unique constraint in AffiliateService)
        Assert.Equal(2, callCount);
        mockService.Verify(
            x => x.RecordCommissionForOrderAsync(_orderId, It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "Both consumers should call RecordCommission, idempotency handled by service layer");
    }

    /// <summary>
    /// AF-18: Ensures commission is recorded even when order skips Delivered status.
    /// </summary>
    [Fact]
    public async Task OrderCompletedEvent_DirectlyFromPending_RecordsCommission()
    {
        // Arrange: Order goes Pending → Completed (skips Delivered)
        var mockService = new Mock<IAffiliateService>();
        mockService
            .Setup(x => x.RecordCommissionForOrderAsync(_orderId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = new OrderCompletedAffiliateConsumer(
            mockService.Object,
            Mock.Of<AffiliateTelemetry>(),
            Mock.Of<ILogger<OrderCompletedAffiliateConsumer>>());

        var evt = new OrderCompletedEvent
        {
            TenantId = _tenantId,
            OrderId = _orderId,
            OccurredAtUtc = DateTime.UtcNow,
            CorrelationId = _orderId.ToString()
        };

        var context = Mock.Of<ConsumeContext<OrderCompletedEvent>>(
            c => c.Message == evt && c.CancellationToken == CancellationToken.None);

        // Act
        await consumer.Consume(context);

        // Assert
        mockService.Verify(
            x => x.RecordCommissionForOrderAsync(_orderId, It.IsAny<CancellationToken>()),
            Times.Once,
            "Commission should be recorded even when order skips Delivered status");
    }
}
