using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Shipping;
using StackExchange.Redis;

namespace PointOfSale.Tests.Shipping;

public sealed partial class ShipmentExternalRegistrationSafetyTests
{
    [Theory]
    [InlineData("dhl")]
    [InlineData("fedex")]
    public async Task LegacyDhlFedExTracking_CannotUseTodaysAccountWithoutOriginalRequest(string carrier)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.ReadyForPickup, carrier,
            externalReferenceId: "synthetic-original-remote", trackingNumber: "synthetic-original-tracking",
            externalRegistrationAttemptCount: 1, externalRegistrationState: "Completed");
        var adapter = new DeterministicCarrierAdapter();
        var result = await CreateService(db, tenant, adapter).SyncTrackingAsync(tenant, id);
        Assert.False(result.Success);
        Assert.Equal("CARRIER_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED", result.ErrorCode);
        Assert.Equal(0, adapter.TrackingCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigurationDeletion_RetainsShipmentAccountAssociation(bool clearMarkers)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, "bosta",
            externalRegistrationAttemptCount: 1, externalRegistrationState: "ReconciliationRequired");
        var configuration = await db.CarrierConfigurations.SingleAsync(c => c.TenantId == tenant && c.CarrierCode == "bosta");
        var originalId = configuration.Id;
        if (clearMarkers)
        {
            configuration.TenantId = Guid.NewGuid(); configuration.CarrierCode = "manual";
            db.CarrierConfigurations.Remove(configuration);
        }
        var authority = new CarrierIntegrationAuthority(db, Mock.Of<IShippingCarrierRegistry>(), Mock.Of<ICarrierCapabilityRegistry>(),
            Mock.Of<ICarrierCredentialEncryptionService>(), NullLogger<CarrierIntegrationAuthority>.Instance);
        var error = await Record.ExceptionAsync(async () =>
        {
            if (clearMarkers) await db.SaveChangesAsync();
            else await authority.DeleteIntegrationAsync(tenant, "bosta");
        });
        db.ChangeTracker.Clear();
        Assert.IsType<InvalidOperationException>(error);
        var retained = await db.CarrierConfigurations.IgnoreQueryFilters().SingleAsync(c => c.Id == originalId);
        Assert.Equal(originalId, retained.Id); Assert.Equal(tenant, retained.TenantId); Assert.Equal("bosta", retained.CarrierCode);
    }

    [Theory]
    [InlineData(" leading-space")]
    [InlineData("remote\nidentity")]
    public async Task CreateResult_MalformedRemoteIdentityCannotBecomeCompletedEvidence(string identity)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, DeterministicCarrierCode);
        var adapter = new DeterministicCarrierAdapter { CreateResult = new CreateShipmentResult { Success = true,
            ExternalReferenceId = identity, TrackingNumber = "SYNTHETIC-TRACK", InitialStatus = ShipmentStatus.ReadyForPickup } };
        var result = await CreateService(db, tenant, adapter).ExecuteCarrierForExistingShipmentAsync(tenant, id, "standard");
        Assert.False(result.Success); Assert.Equal("CARRIER_EXTERNAL_REFERENCE_INVALID", result.ErrorCode);
        var shipment = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Null(shipment.ExternalRegistrationCompletedAtUtc); Assert.Null(shipment.ExternalReferenceId);
        Assert.True(CarrierRegistrationEvidence.HasOutboundEvidence(shipment));
    }
}

public sealed class CompatibilityQuoteOperationBoundaryTests
{
    [Theory]
    [InlineData("bosta")]
    [InlineData("fedex")]
    [InlineData("mylerz")]
    public async Task UnsupportedCompatibilityQuoteDoesNotResolveAccountsOrAdapters(string carrier)
    {
        var tenant = Guid.NewGuid();
        var authority = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        var configuration = JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>(
            $$"""{"Id":"{{Guid.NewGuid()}}","CarrierCode":"{{carrier}}","UseCarrierRates":true,"SupportsCod":true}""")!;
        authority.Setup(a => a.ListRuntimeConfigurationsAsync(tenant, CarrierIntegrationPurpose.Quote, It.IsAny<CancellationToken>()))
            .ReturnsAsync([configuration]);
        var registry = new Mock<IShippingCarrierRegistry>(MockBehavior.Strict);
        var service = new ShippingQuoteService(registry.Object, authority.Object, Mock.Of<IConnectionMultiplexer>(MockBehavior.Strict),
            NullLogger<ShippingQuoteService>.Instance);
        var result = await service.GetQuotesAsync(tenant, new GetShippingQuotesRequest { DestinationCountry = "EG", DestinationCity = "Synthetic" });
        Assert.True(result.Success); Assert.Empty(result.Options);
        registry.VerifyNoOtherCalls();
        authority.Verify(a => a.ListRuntimeConfigurationsAsync(tenant, CarrierIntegrationPurpose.Quote, It.IsAny<CancellationToken>()), Times.Once);
        authority.VerifyNoOtherCalls();
    }
}
