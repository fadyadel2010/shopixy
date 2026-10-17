using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

// Offline orchestration fixtures only: the synthetic authority/adapter cannot
// qualify an account, publish geography, or prove any real carrier entitlement.
public sealed partial class ShipmentExternalRegistrationSafetyTests
{
    [Theory]
    [InlineData("bosta")]
    [InlineData("aramex")]
    public async Task LegacyBoundCarrier_TrackingAndCancellationFailClosedWithoutOriginalAccountEvidence(string carrier)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid(); var actor = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Synthetic boundary", Slug = $"boundary-{tenant:N}", Status = TenantStatus.Active, IsActive = true });
        db.Users.Add(new User { Id = actor, TenantId = tenant, Username = $"actor-{actor:N}", Email = "synthetic@example.invalid", PasswordHash = "fixture", Role = UserRole.Warehouse, IsActive = true });
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.ReadyForPickup, carrier, trackingNumber: "SYNTHETIC-TRACK", externalReferenceId: "SYNTHETIC-DELIVERY",
            externalRegistrationState: "Completed", externalRegistrationAttemptCount: 1);
        (await db.Shipments.SingleAsync()).ExternalRegistrationCompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var adapter = new DeterministicCarrierAdapter();
        var authority = BoundAuthority(tenant, carrier, () => BoundResolution(Guid.NewGuid(), carrier, "synthetic-account"));
        var service = CreateCarrierService(db, tenant, adapter, authority.Object);
        var tracking = await service.SyncTrackingAsync(tenant, id);
        Assert.False(tracking.Success); Assert.Equal("CARRIER_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED", tracking.ErrorCode);
        Assert.Equal(0, adapter.TrackingCalls);
        var registry = new Mock<IShippingCarrierRegistry>(); registry.Setup(r => r.GetAdapter(carrier)).Returns(adapter);
        var tenantContext = new TenantContext(); tenantContext.SetTenantId(tenant);
        var cancellation = await new ShipmentCancellationService(db, registry.Object, authority.Object, Mock.Of<IEventPublisher>(), tenantContext)
            .CancelForAdminAsync(tenant, id, actor, null, "Synthetic legacy boundary", "synthetic-no-call");
        Assert.False(cancellation.Confirmed);
        Assert.Equal(carrier == "aramex" ? "CARRIER_CANCELLATION_UNSUPPORTED" : "CARRIER_ORIGINAL_ACCOUNT_REQUIRED_RECONCILIATION_REQUIRED", cancellation.Code);
        if (carrier == "aramex") Assert.Equal("Unsupported", cancellation.Outcome);
        Assert.Null((await db.ShipmentCancellationOperations.SingleAsync()).AttemptStartedAtUtc);
        Assert.Equal(ShipmentStatus.ReadyForPickup, (await db.Shipments.SingleAsync()).Status);
        Assert.False((await db.Shipments.SingleAsync()).CodCollected);
    }

    [Theory]
    [InlineData("bosta", "account")]
    [InlineData("aramex", "account")]
    [InlineData("bosta", "configuration")]
    [InlineData("aramex", "configuration")]
    [InlineData("bosta", "api-key")]
    [InlineData("aramex", "api-key")]
    [InlineData("bosta", "api-secret")]
    [InlineData("aramex", "api-secret")]
    [InlineData("bosta", "environment")]
    [InlineData("aramex", "environment")]
    [InlineData("bosta", "endpoint")]
    [InlineData("aramex", "endpoint")]
    [InlineData("bosta", "extra-config")]
    [InlineData("aramex", "extra-config")]
    public async Task BoundRegistration_UsesActualParcelAndEncryptedAccount_ThenRejectsChangedAccount(string carrier, string change)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Synthetic account binding", Slug = $"binding-{tenant:N}", Status = TenantStatus.Active, IsActive = true });
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, carrier);
        var s = await db.Shipments.SingleAsync();
        var o = await db.Orders.Include(o => o.Items).SingleAsync();
        var actor = Guid.NewGuid();
        db.Users.Add(new User { Id = actor, TenantId = tenant, Username = "synthetic-measurer", Email = "measurer@example.invalid", PasswordHash = "fixture", Role = UserRole.Warehouse, IsActive = true });
        var item = new OrderItem { Id = Guid.NewGuid(), OrderId = o.Id, ProductId = Guid.NewGuid(), Quantity = 2 };
        db.OrderItems.Add(item);
        Assert.Single(o.Items);
        var measurement = new ParcelMeasurement(30, 20, 10, "cm", 2750, "g", 1, "SYNTHETIC-PARCEL");
        if (carrier == "bosta") measurement = measurement with { Goods = new("Synthetic fixture goods", 75m, "EGP") };
        s.ParcelEvidenceJson = JsonSerializer.Serialize(ParcelMeasurements.Admit(measurement, s, o, actor));
        await db.SaveChangesAsync();
        var config = Guid.NewGuid(); var account = "synthetic-account-A";
        var resolved = BoundResolution(config, carrier, account);
        var authority = BoundAuthority(tenant, carrier, () => resolved);
        var encryption = new CarrierCredentialEncryptionService(new EphemeralDataProtectionProvider(), NullLogger<CarrierCredentialEncryptionService>.Instance);
        var adapter = new DeterministicCarrierAdapter();
        var service = CreateCarrierService(db, tenant, adapter, authority.Object, encryption);
        var result = await service.ExecuteCarrierForExistingShipmentAsync(tenant, id, "standard");
        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(1, adapter.CreateShipmentCallCount);
        Assert.Equal(2.750m, adapter.LastCreateRequest!.WeightKg);
        Assert.Equal(30m, adapter.LastCreateRequest.LengthCm);
        Assert.Equal("SYNTHETIC-PARCEL", adapter.LastCreateRequest.PieceReference);
        if (carrier == "bosta")
        {
            Assert.Equal(2, adapter.LastCreateRequest.PackageItemCount);
            Assert.Equal("Synthetic fixture goods", adapter.LastCreateRequest.PackageDescription);
            Assert.Equal(75m, adapter.LastCreateRequest.DeclaredValue);
        }
        db.ChangeTracker.Clear(); s = await db.Shipments.SingleAsync();
        var json = Assert.IsType<string>(s.CarrierRequestSnapshotJson);
        Assert.DoesNotContain("synthetic-api-secret", json);
        Assert.DoesNotContain(account, json);
        Assert.Equal(1, s.ExternalRegistrationAttemptCount);
        Assert.Equal(adapter.LastCreateRequest.CarrierRequestId, s.CarrierRequestId);
        // Deactivation may stop new registration but must not invent a new POST
        // to recover an already-owned shipment. Matching replay is read-only.
        authority.Setup(x => x.GetServiceLevelReadinessAsync(tenant, carrier, "standard", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierServiceLevelReadiness(carrier, "standard", true, true, false, false, ["SERVICE_LEVEL_DISABLED"]));
        var matchingReplay = await service.ExecuteCarrierForExistingShipmentAsync(tenant, id, "standard");
        Assert.True(matchingReplay.Success, matchingReplay.ErrorCode);
        Assert.Equal(1, adapter.CreateShipmentCallCount);
        switch (change)
        {
            case "account": resolved.Credentials!.AccountId = "synthetic-account-B"; break;
            case "configuration": resolved = resolved with { Configuration = resolved.Configuration! with { Id = Guid.NewGuid() } }; break;
            case "api-key": resolved.Credentials!.ApiKey = "synthetic-changed-key"; break;
            case "api-secret": resolved.Credentials!.ApiSecret = "synthetic-changed-secret"; break;
            case "environment": resolved.Credentials!.UseSandbox = false; resolved = resolved with { Configuration = resolved.Configuration! with { UseSandbox = false } }; break;
            case "endpoint": resolved.Credentials!.ApiUrl = "https://other.example.invalid"; break;
            case "extra-config": resolved.Credentials!.ExtraConfig["businessLocationId"] = "changed-synthetic-location"; break;
            default: throw new InvalidOperationException("Unknown fixture variant.");
        }
        var changed = await service.ExecuteCarrierForExistingShipmentAsync(tenant, id, "standard");
        Assert.False(changed.Success);
        Assert.True(changed.ExternalCallSkipped);
        Assert.Equal("CARRIER_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED", changed.ErrorCode);
        Assert.Equal(1, adapter.CreateShipmentCallCount);
        Assert.Equal(0, adapter.LookupByCarrierRequestIdCallCount);
        Assert.Equal(json, (await db.Shipments.SingleAsync()).CarrierRequestSnapshotJson);
        db.ChangeTracker.Clear();
        var tracking = await service.SyncTrackingAsync(tenant, id);
        Assert.False(tracking.Success); Assert.Equal("CARRIER_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED", tracking.ErrorCode);
        Assert.Equal(0, adapter.TrackingCalls);
        var registry = new Mock<IShippingCarrierRegistry>(MockBehavior.Strict); registry.Setup(r => r.GetAdapter(carrier)).Returns(adapter);
        var tenantContext = new TenantContext(); tenantContext.SetTenantId(tenant);
        var cancellation = await new ShipmentCancellationService(db, registry.Object, authority.Object, Mock.Of<IEventPublisher>(), tenantContext, encryption)
            .CancelForAdminAsync(tenant, id, actor, null, "Synthetic original-account change", "synthetic-changed-account");
        Assert.False(cancellation.Confirmed);
        Assert.Equal(carrier == "aramex" ? "CARRIER_CANCELLATION_UNSUPPORTED" : "CARRIER_ORIGINAL_ACCOUNT_REQUIRED_RECONCILIATION_REQUIRED", cancellation.Code);
        if (carrier == "aramex") Assert.Equal("Unsupported", cancellation.Outcome);
        Assert.Null((await db.ShipmentCancellationOperations.SingleAsync()).AttemptStartedAtUtc);
    }

    [Theory]
    [InlineData("bosta", "parcel")]
    [InlineData("aramex", "parcel")]
    [InlineData("bosta", "prior")]
    [InlineData("aramex", "prior")]
    [InlineData("bosta", "last-attempt")]
    [InlineData("aramex", "last-attempt")]
    public async Task BoundRegistration_RejectsMissingMeasurementsOrLegacyUncertainIntent_WithoutCarrierIo(string carrier, string scenario)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, carrier,
            externalRegistrationAttemptCount: scenario == "prior" ? 1 : 0);
        if (scenario == "last-attempt")
        {
            (await db.Shipments.SingleAsync()).ExternalRegistrationLastAttemptAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var adapter = new DeterministicCarrierAdapter();
        var authority = BoundAuthority(tenant, carrier, () => BoundResolution(Guid.NewGuid(), carrier, "synthetic-account"));
        var service = CreateCarrierService(db, tenant, adapter, authority.Object);
        var result = await service.ExecuteCarrierForExistingShipmentAsync(tenant, id, "standard");
        Assert.False(result.Success);
        Assert.Equal(scenario == "parcel" ? "PARCEL_MEASUREMENTS_REQUIRED" : "CARRIER_ORIGINAL_REQUEST_REQUIRED_RECONCILIATION_REQUIRED", result.ErrorCode);
        Assert.True(result.ExternalCallSkipped);
        Assert.Equal(0, adapter.CreateShipmentCallCount);
        Assert.Equal(0, adapter.LookupByCarrierRequestIdCallCount);
        Assert.Null((await db.Shipments.SingleAsync()).CarrierRequestSnapshotJson);
    }

    [Fact]
    public async Task Bosta_LegacyMeasurementsWithoutGoodsReturnActionableFailureBeforeClaimOrProviderIo()
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, "bosta");
        var shipment = await db.Shipments.SingleAsync(); var order = await db.Orders.Include(o => o.Items).SingleAsync();
        db.OrderItems.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = Guid.NewGuid(), Quantity = 1 });
        shipment.CarrierCode = "dhl";
        shipment.ParcelEvidenceJson = JsonSerializer.Serialize(ParcelMeasurements.Admit(new(20, 15, 10, "cm", 1, "kg", 1, "SYNTHETIC-LEGACY"), shipment, order, Guid.NewGuid()));
        shipment.CarrierCode = "bosta"; await db.SaveChangesAsync();
        var adapter = new DeterministicCarrierAdapter();
        var authority = BoundAuthority(tenant, "bosta", () => BoundResolution(Guid.NewGuid(), "bosta", "synthetic-account"));
        var result = await CreateCarrierService(db, tenant, adapter, authority.Object).ExecuteCarrierForExistingShipmentAsync(tenant, id, "standard");
        Assert.Equal("BOSTA_GOODS_DECLARATION_REQUIRED", result.ErrorCode); Assert.True(result.ExternalCallSkipped);
        Assert.Equal(0, adapter.CreateShipmentCallCount); Assert.Equal(0, shipment.ExternalRegistrationAttemptCount);
        Assert.Null(shipment.CarrierRequestSnapshotJson);
    }

    private static Mock<ICarrierIntegrationAuthority> BoundAuthority(Guid tenant, string carrier, Func<CarrierCredentialsResolution> resolution)
    {
        var authority = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        authority.Setup(x => x.GetServiceLevelReadinessAsync(tenant, carrier, "standard", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierServiceLevelReadiness(carrier, "standard", true, true, true, true, []));
        authority.Setup(x => x.ResolveCredentialsAsync(tenant, carrier, It.IsAny<CarrierIntegrationPurpose>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolution);
        return authority;
    }
    private static CarrierCredentialsResolution BoundResolution(Guid id, string carrier, string account) => new(true, carrier,
        new CarrierCredentials { ApiKey = "synthetic-api-key", ApiSecret = "synthetic-api-secret", AccountId = account, UseSandbox = true,
            ExtraConfig = new() {["businessLocationId"] = "synthetic-owned-pickup"} },
        JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>($$"""{"Id":"{{id}}","CarrierCode":"{{carrier}}","IsEnabled":true,"UseSandbox":true,"DisplayName":"Synthetic offline carrier"}""")!, []);
}
