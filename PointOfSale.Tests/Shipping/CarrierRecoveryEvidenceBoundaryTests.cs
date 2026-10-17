using Microsoft.EntityFrameworkCore;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

public sealed partial class ShipmentExternalRegistrationSafetyTests
{
    [Fact]
    public async Task LateNegativeLookup_DoesNotOverwriteAnIndependentlyCompletedRegistration()
    {
        // Two EF contexts sharing only a generated process-local fake store.
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid(); var store = $"OfflineRecoveryRace_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(tenant, store);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, DeterministicCarrierCode,
            carrierRequestId: "retained-request", externalRegistrationAttemptCount: 1, externalRegistrationState: "PendingExternalCall");
        var adapter = new DeterministicCarrierAdapter { BeforeLookup = async () =>
        {
            await using var winner = CreateDbContext(tenant, store);
            var shipment = await winner.Shipments.SingleAsync();
            shipment.Status = ShipmentStatus.ReadyForPickup;
            shipment.ExternalRegistrationState = "Completed";
            shipment.ExternalRegistrationCompletedAtUtc = DateTime.UtcNow;
            shipment.ExternalReferenceId = "independently-confirmed-identity";
            shipment.TrackingNumber = "independently-confirmed-tracking";
            shipment.CarrierCreateResponse = "{\"retained\":true}";
            await winner.SaveChangesAsync();
        } };
        var result = await CreateService(db, tenant, adapter).ExecuteCarrierForExistingShipmentAsync(tenant, id, "standard");
        Assert.True(result.Success);
        Assert.True(result.Reconciled); Assert.True(result.ExternalCallSkipped);
        Assert.Equal("independently-confirmed-tracking", result.TrackingNumber);
        await using var inspect = CreateDbContext(tenant, store);
        var retained = await inspect.Shipments.SingleAsync();
        Assert.Equal("Completed", retained.ExternalRegistrationState);
        Assert.Null(retained.ExternalRegistrationError);
        Assert.Equal("{\"retained\":true}", retained.CarrierCreateResponse);
        Assert.Equal(1, retained.ExternalRegistrationAttemptCount);
        Assert.Equal(1, adapter.LookupByCarrierRequestIdCallCount); Assert.Equal(0, adapter.CreateShipmentCallCount);
    }

    [Theory]
    [InlineData("bosta")]
    [InlineData("aramex")]
    public async Task CompatibilityCreate_RequiresMeasuredParcelBeforeClaimOrProviderCall(string carrier)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, carrier);
        // Remove only the generated process-local fake intent to exercise the
        // compatibility create entry point, never a configured SQL record.
        db.Shipments.Remove(await db.Shipments.SingleAsync());
        await db.SaveChangesAsync();
        var order = await db.Orders.SingleAsync();
        var authority = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        authority.Setup(a => a.GetServiceLevelReadinessAsync(tenant, carrier, "standard", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierServiceLevelReadiness(carrier, "standard", true, true, true, true, []));
        authority.Setup(a => a.ResolveCredentialsAsync(tenant, carrier, It.IsAny<CarrierIntegrationPurpose>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierCredentialsResolution(true, carrier, new CarrierCredentials { ApiKey = "synthetic-account" },
                System.Text.Json.JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>($$"""{"Id":"{{Guid.NewGuid()}}","CarrierCode":"{{carrier}}","DisplayName":"Offline fake","IsEnabled":true}""")!, []));
        var adapter = new DeterministicCarrierAdapter();
        var result = await CreateCarrierService(db, tenant, adapter, authority.Object)
            .CreateShipmentWithCarrierAsync(tenant, order.Id, carrier, "standard");
        Assert.False(result.Success);
        Assert.Equal("PARCEL_MEASUREMENTS_REQUIRED", result.ErrorCode);
        Assert.Equal(0, adapter.CreateShipmentCallCount);
        var intent = await db.Shipments.SingleAsync();
        Assert.Equal(ShipmentStatus.PendingExternalRegistration, intent.Status);
        Assert.Equal(0, intent.ExternalRegistrationAttemptCount);
        Assert.Null(intent.ExternalRegistrationLastAttemptAtUtc);
        Assert.Null(intent.CarrierRequestSnapshotJson);
    }

    [Theory]
    [InlineData("dhl", "timestamp")]
    [InlineData("dhl", "fingerprint")]
    [InlineData("dhl", "response")]
    [InlineData("dhl", "reconciliation")]
    [InlineData("bosta", "timestamp")]
    [InlineData("bosta", "response")]
    [InlineData("aramex", "fingerprint")]
    [InlineData("fedex", "fingerprint")]
    public async Task InconsistentZeroCounterEvidence_DoesNotAuthorizeFreshCreateOrParcelReplacement(string carrier, string evidence)
    {
        // EF's process-local fake store only; no SQL connection or provider call.
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, carrier);
        var shipment = await db.Shipments.SingleAsync();
        switch (evidence)
        {
            case "timestamp": shipment.ExternalRegistrationLastAttemptAtUtc = DateTime.UtcNow.AddMinutes(-2); break;
            case "fingerprint": shipment.CarrierRequestFingerprint = "retained-original-fingerprint"; break;
            case "response": shipment.CarrierCreateResponse = "{\"unverified\":true}"; break;
            case "reconciliation": shipment.ExternalRegistrationState = "ReconciliationRequired"; break;
        }
        await db.SaveChangesAsync();
        var originalResponse = shipment.CarrierCreateResponse;
        var adapter = new DeterministicCarrierAdapter();
        var result = await CreateService(db, tenant, adapter).ExecuteCarrierForExistingShipmentAsync(tenant, id, "standard");
        Assert.False(result.Success);
        Assert.Equal(carrier == "fedex" ? "EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED" :
            "CARRIER_ORIGINAL_REQUEST_REQUIRED_RECONCILIATION_REQUIRED", result.ErrorCode);
        Assert.True(result.ExternalCallSkipped);
        Assert.True(result.Reconciled);
        Assert.Equal(0, adapter.CreateShipmentCallCount);
        var view = await new ShipmentParcelEvidenceService(db, new TestTenantContext(tenant)).ReadAsync(tenant, id);
        Assert.False(view.CanMeasure);
        Assert.Equal(0, shipment.ExternalRegistrationAttemptCount);
        Assert.Equal(originalResponse, shipment.CarrierCreateResponse);
        Assert.Null(shipment.CarrierRequestSnapshotJson);
        var projection = await new PointOfSale.API.Services.Admin.AdminShipmentService(db, new NoOpEventPublisher(),
            Moq.Mock.Of<PointOfSale.Core.Interfaces.Fulfillment.IOrderFulfillmentService>(),
            Moq.Mock.Of<PointOfSale.Core.Interfaces.Marketplace.IMarketplaceEventOutboxService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PointOfSale.API.Services.Admin.AdminShipmentService>.Instance)
            .GetShipmentDetailsAsync(tenant, id, "offline-recovery-boundary");
        Assert.NotNull(projection);
        Assert.True(projection.ExternalRegistrationRecoveryRequired);
        Assert.Equal(shipment.ExternalRegistrationState, projection.ExternalRegistrationState);
        Assert.Equal(0, projection.ExternalRegistrationAttemptCount);
    }
}
