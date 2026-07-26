using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed class ShipmentExternalRegistrationRequestedConsumerTests : IDisposable
{
    private readonly CommerceReliabilityTelemetry _telemetry = new();

    [Fact]
    public async Task Consume_WhenCarrierReturnsProviderApiError_ThrowsForRetry()
    {
        var tenantId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var carrierShipmentService = new Mock<ICarrierShipmentService>(MockBehavior.Strict);
        carrierShipmentService
            .Setup(service => service.ExecuteCarrierForExistingShipmentAsync(
                tenantId,
                shipmentId,
                "standard",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierShipmentResult
            {
                Success = false,
                ErrorCode = "MYLERZ_API_ERROR",
                ErrorMessage = "503 Service Unavailable",
                ExternalCallAttempted = true
            });

        var context = new Mock<ConsumeContext<ShipmentExternalRegistrationRequestedEvent>>(MockBehavior.Strict);
        context.SetupGet(x => x.Message).Returns(new ShipmentExternalRegistrationRequestedEvent
        {
            TenantId = tenantId,
            ShipmentId = shipmentId,
            CarrierCode = "mylerz",
            ServiceLevel = "standard",
            IsCarrierManaged = true
        });
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        var consumer = new ShipmentExternalRegistrationRequestedConsumer(
            carrierShipmentService.Object,
            _telemetry,
            NullLogger<ShipmentExternalRegistrationRequestedConsumer>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(context.Object));

        carrierShipmentService.VerifyAll();
    }

    public void Dispose()
    {
        _telemetry.Dispose();
    }
}
