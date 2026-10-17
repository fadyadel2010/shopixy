using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

public sealed partial class ShipmentExternalRegistrationSafetyTests
{
    [Theory]
    [InlineData("bosta", "BOSTA_REMOTE_QUOTES_UNSUPPORTED")]
    [InlineData("fedex", "FEDEX_NOT_YET_CERTIFIED")]
    [InlineData("mylerz", "MYLERZ_CONTRACT_UNPROVEN")]
    [InlineData("manual", "MERCHANT_METHOD_PRICE")]
    public async Task UnsupportedQuote_ReadAndCommandCannotUseAnAccountOrAdapter(string carrier, string blocker)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, carrier);
        var authority = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        var view = await new ShipmentParcelEvidenceService(db, new TestTenantContext(tenant), authority.Object).ReadAsync(tenant, id);
        Assert.Equal(id, view.ShipmentId);
        Assert.False(view.CanQuote);
        Assert.Contains(blocker, view.QuoteBlockers);
        var result = await CreateCarrierService(db, tenant, new DeterministicCarrierAdapter(), authority.Object).QuoteExistingShipmentAsync(tenant, id);
        Assert.False(result.Success); Assert.Equal(blocker, result.ErrorCode);
        authority.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("aramex", true, true)]
    [InlineData("aramex", false, true)]
    [InlineData("dhl", true, false)]
    public async Task QuoteEligibility_RequiresCurrentAccountAndPurchasedServiceReadiness(string carrier, bool accountReady, bool serviceReady)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, carrier);
        var shipment = await db.Shipments.SingleAsync(); var order = await db.Orders.Include(o => o.Items).SingleAsync();
        db.OrderItems.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = Guid.NewGuid(), Quantity = 1 });
        shipment.ParcelEvidenceJson = JsonSerializer.Serialize(ParcelMeasurements.Admit(new(10, 20, 30, "cm", 1, "kg", 1, "SYNTHETIC"), shipment, order, Guid.NewGuid()));
        await db.SaveChangesAsync();
        var authority = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        authority.Setup(a => a.GetIntegrationReadinessAsync(tenant, carrier, shipment.ShippingMethodId, order.WarehouseId, CarrierIntegrationPurpose.Quote, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierIntegrationReadiness(carrier, "Offline fake", false, true, accountReady, "Sandbox", CarrierProductionCapability.Partial,
                accountReady ? [] : ["ACCOUNT_NOT_QUALIFIED"], true, accountReady, "Available"));
        authority.Setup(a => a.GetServiceLevelReadinessAsync(tenant, carrier, shipment.ServiceLevel, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierServiceLevelReadiness(carrier, shipment.ServiceLevel, true, true, serviceReady, serviceReady, serviceReady ? [] : ["SERVICE_LEVEL_DISABLED"]));
        var view = await new ShipmentParcelEvidenceService(db, new TestTenantContext(tenant), authority.Object).ReadAsync(tenant, id);
        Assert.Equal(accountReady && serviceReady, view.CanQuote);
        Assert.Equal(id, view.ShipmentId); Assert.NotNull(view.Evidence); Assert.False(view.CanMeasure);
        if (!accountReady) Assert.Contains("ACCOUNT_NOT_QUALIFIED", view.QuoteBlockers);
        if (!serviceReady) Assert.Contains("SERVICE_LEVEL_DISABLED", view.QuoteBlockers);
        Assert.Null(shipment.CarrierRequestSnapshotJson); Assert.Equal(0, shipment.ExternalRegistrationAttemptCount);
    }

    [Fact]
    public void SchedulingAlone_IsNotOutboundEvidence_WhileAnInvalidCounterIsConservativelyContained()
    {
        var shipment = new Shipment { CarrierRequestId = "local-stable-request", ExternalRegistrationRequestedAtUtc = DateTime.UtcNow, ExternalRegistrationState = "PendingExternalCall" };
        Assert.False(CarrierRegistrationEvidence.HasOutboundEvidence(shipment));
        shipment.ExternalRegistrationAttemptCount = -1;
        Assert.True(CarrierRegistrationEvidence.HasOutboundEvidence(shipment));
    }
}
