using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Consumers;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ShipmentCodCollectedConsumerTests
{
    [Fact]
    public async Task Consume_RoutesCarrierCodCollectionToPaymentLifecycleAuthority()
    {
        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var paymentLifecycle = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        paymentLifecycle
            .Setup(service => service.MarkOfflineCollectionDeliveredAsync(
                tenantId,
                orderId,
                "TRK-123",
                "Carrier One",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentTransitionResult.Succeeded(PaymentStatus.Collected));

        var consumer = new ShipmentCodCollectedConsumer(
            paymentLifecycle.Object,
            NullLogger<ShipmentCodCollectedConsumer>.Instance);
        var context = ConsumeContext(new ShipmentCodCollectedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            ShipmentId = Guid.NewGuid(),
            ShipmentNumber = "SHP-123",
            TrackingNumber = "TRK-123",
            CarrierCode = "carrier-one",
            CarrierName = "Carrier One",
            CodAmount = 250m,
            Currency = "EGP",
            CollectedAt = new DateTime(2026, 7, 21, 9, 0, 0, DateTimeKind.Utc)
        });

        await consumer.Consume(context.Object);

        paymentLifecycle.VerifyAll();
    }

    [Fact]
    public async Task Consume_ThrowsWhenPaymentLifecycleRejectsCodCollection()
    {
        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var paymentLifecycle = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        paymentLifecycle
            .Setup(service => service.MarkOfflineCollectionDeliveredAsync(
                tenantId,
                orderId,
                "SHP-456",
                "carrier-two",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentTransitionResult.Failed("Order is not a COD order", "NOT_COD"));

        var consumer = new ShipmentCodCollectedConsumer(
            paymentLifecycle.Object,
            NullLogger<ShipmentCodCollectedConsumer>.Instance);
        var context = ConsumeContext(new ShipmentCodCollectedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            ShipmentId = Guid.NewGuid(),
            ShipmentNumber = "SHP-456",
            CarrierCode = "carrier-two",
            CodAmount = 250m,
            Currency = "EGP",
            CollectedAt = new DateTime(2026, 7, 21, 9, 0, 0, DateTimeKind.Utc)
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(context.Object));
        paymentLifecycle.VerifyAll();
    }

    private static Mock<ConsumeContext<ShipmentCodCollectedEvent>> ConsumeContext(ShipmentCodCollectedEvent message)
    {
        var context = new Mock<ConsumeContext<ShipmentCodCollectedEvent>>();
        context.SetupGet(item => item.Message).Returns(message);
        context.SetupGet(item => item.CancellationToken).Returns(CancellationToken.None);
        return context;
    }
}
