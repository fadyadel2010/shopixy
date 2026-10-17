using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Shipping;

public sealed partial class ShipmentExternalRegistrationSafetyTests : IDisposable
{
    // Generic registration fixtures must not pretend to certify a blocked real provider.
    private const string DeterministicCarrierCode = "test-registration";
    private readonly CommerceReliabilityTelemetry _telemetry = new();

    [Fact]
    public async Task ExecuteCarrierForExistingShipmentAsync_RetryAfterSuccess_DoesNotCreateDuplicateCarrierShipment()
    {
        var tenantId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var adapter = new DeterministicCarrierAdapter();
        await using var db = CreateDbContext(tenantId);

        SeedOrderAndShipment(
            db,
            tenantId,
            shipmentId,
            ShipmentStatus.PendingExternalRegistration,
            DeterministicCarrierCode);

        var service = CreateService(db, tenantId, adapter);

        var firstResult = await service.ExecuteCarrierForExistingShipmentAsync(
            tenantId,
            shipmentId,
            "standard",
            CancellationToken.None);

        db.ChangeTracker.Clear();

        var secondResult = await service.ExecuteCarrierForExistingShipmentAsync(
            tenantId,
            shipmentId,
            "standard",
            CancellationToken.None);

        var shipment = await db.Shipments.AsNoTracking().SingleAsync(s => s.Id == shipmentId);

        Assert.True(firstResult.Success);
        Assert.True(firstResult.ExternalCallAttempted);
        Assert.False(firstResult.ExternalCallSkipped);
        Assert.True(secondResult.Success);
        Assert.True(secondResult.ExternalCallSkipped);
        Assert.True(secondResult.Reconciled);
        Assert.Equal(1, adapter.CreateShipmentCallCount);
        Assert.Equal("carrier-shipment-1", shipment.ExternalReferenceId);
        Assert.Equal("TRACK-1", shipment.TrackingNumber);
        Assert.Equal(ShipmentStatus.ReadyForPickup, shipment.Status);
        Assert.Equal(1, shipment.ExternalRegistrationAttemptCount);
    }

    [Fact]
    public async Task ExecuteCarrierForExistingShipmentAsync_WhenMethodWasChangedAndDeactivated_UsesPurchasedExpressSnapshot()
    {
        var tenantId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var adapter = new DeterministicCarrierAdapter();
        await using var db = CreateDbContext(tenantId);

        SeedOrderAndShipment(
            db,
            tenantId,
            shipmentId,
            ShipmentStatus.PendingExternalRegistration,
            CarrierCodes.Dhl,
            externalReferenceId: "carrier-existing-1",
            trackingNumber: "TRACK-EXISTING-1");

        var service = CreateService(db, tenantId, adapter);

        var result = await service.ExecuteCarrierForExistingShipmentAsync(
            tenantId,
            shipmentId,
            "express",
            CancellationToken.None);

        var shipment = await db.Shipments.AsNoTracking().SingleAsync(s => s.Id == shipmentId);
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == shipment.OrderId);

        Assert.True(result.Success);
        Assert.True(result.ExternalCallSkipped);
        Assert.True(result.Reconciled);
        Assert.Equal(0, adapter.CreateShipmentCallCount);
        Assert.Equal(ShipmentStatus.ReadyForPickup, shipment.Status);
        Assert.Equal("TRACK-EXISTING-1", order.TrackingNumber);
        Assert.Equal("TRACK-EXISTING-1", result.TrackingNumber);
        Assert.Equal("express", shipment.ServiceLevel);
        var currentMethod = await db.ShippingMethods.IgnoreQueryFilters().SingleAsync();
        Assert.False(currentMethod.IsActive);
        Assert.Equal("standard", currentMethod.ServiceLevel);
    }

    [Fact]
    public async Task ExecuteCarrierForExistingShipmentAsync_WhenExternalSuccessWasNotPersisted_ReconcilesByCarrierRequestIdWithoutDuplicateCreate()
    {
        var tenantId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var carrierRequestId = $"ship-{tenantId:N}-{shipmentId:N}";
        var adapter = new DeterministicCarrierAdapter();
        adapter.AddExistingShipment(
            carrierRequestId,
            externalReferenceId: "carrier-existing-by-key",
            trackingNumber: "TRACK-BY-KEY");

        await using var db = CreateDbContext(tenantId);

        SeedOrderAndShipment(
            db,
            tenantId,
            shipmentId,
            ShipmentStatus.PendingExternalRegistration,
            DeterministicCarrierCode,
            carrierRequestId: carrierRequestId,
            externalRegistrationAttemptCount: 1,
            externalRegistrationState: "PendingExternalCall");

        var service = CreateService(db, tenantId, adapter);

        var result = await service.ExecuteCarrierForExistingShipmentAsync(
            tenantId,
            shipmentId,
            "standard",
            CancellationToken.None);

        var shipment = await db.Shipments.AsNoTracking().SingleAsync(s => s.Id == shipmentId);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.Reconciled);
        Assert.Equal(0, adapter.CreateShipmentCallCount);
        Assert.Equal(1, adapter.LookupByCarrierRequestIdCallCount);
        Assert.Equal(carrierRequestId, shipment.CarrierRequestId);
        Assert.Equal("carrier-existing-by-key", shipment.ExternalReferenceId);
        Assert.Equal("TRACK-BY-KEY", shipment.TrackingNumber);
        Assert.Equal("Completed", shipment.ExternalRegistrationState);
        Assert.Equal(ShipmentStatus.ReadyForPickup, shipment.Status);
    }

    [Fact]
    public async Task Dhl_PriorAmbiguousAttemptUsesRealAdapterFailClosedLookupAndNeverReposts()
    {
        var tenantId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        SeedOrderAndShipment(db, tenantId, shipmentId, ShipmentStatus.PendingExternalRegistration,
            CarrierCodes.Dhl, carrierRequestId: $"ship-{tenantId:N}-{shipmentId:N}",
            externalRegistrationAttemptCount: 1, externalRegistrationState: "PendingExternalCall");
        var handler = new RejectDhlNetworkHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("DHL")).Returns(new HttpClient(handler));
        var adapter = new DhlAdapter(factory.Object, NullLogger<DhlAdapter>.Instance);
        var service = CreateService(db, tenantId, adapter);

        var result = await service.ExecuteCarrierForExistingShipmentAsync(tenantId, shipmentId, "express");

        Assert.False(result.Success);
        Assert.Equal("CARRIER_ORIGINAL_REQUEST_REQUIRED_RECONCILIATION_REQUIRED", result.ErrorCode);
        Assert.True(result.ExternalCallSkipped);
        Assert.Equal(0, handler.CallCount);
        var shipment = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(1, shipment.ExternalRegistrationAttemptCount);
        Assert.Equal("PendingExternalCall", shipment.ExternalRegistrationState); // No replacement/rewrite of missing original evidence.
        Assert.Null(shipment.ExternalRegistrationCompletedAtUtc);
        Assert.Equal(ShipmentStatus.PendingExternalRegistration, shipment.Status);
    }

    private sealed class RejectDhlNetworkHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Recovery must not invoke DHL without verified lookup authority.");
        }
    }

    [Theory]
    [InlineData("12345")]
    [InlineData(null)]
    public async Task Dhl_OriginAndRequestPreserveAdmittedPostcodeWithoutEnrichingRecovery(string? postalCode)
    {
        var tenantId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        SeedOrderAndShipment(db, tenantId, shipmentId, ShipmentStatus.PendingExternalRegistration,
            CarrierCodes.Dhl, originPostalCode: postalCode);
        var evidence = await db.Set<AddressAdmissionEvidence>().SingleAsync();
        var shipment = await db.Shipments.SingleAsync();
        var order = await db.Orders.SingleAsync();
        var adapter = new DeterministicCarrierAdapter();
        var service = CreateService(db, tenantId, adapter);
        // Exercise snapshot construction without bypassing the real DHL execution gate.
        var resolver = typeof(CarrierShipmentService).GetMethod("ResolveOrRestoreShipmentOriginAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var origin = await (Task<ShipmentOriginDto?>)resolver.Invoke(service, new object[] { shipment, order, CancellationToken.None })!;
        Assert.NotNull(origin);
        Assert.Equal(postalCode, origin.PostalCode);
        var snapshotJson = shipment.OriginSnapshotJson;

        db.Set<AddressAdmissionEvidence>().Add(new AddressAdmissionEvidence
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OwnerDomain = AddressAdmissionOwnerDomain.Warehouse,
            OwnerId = evidence.OwnerId, AddressFingerprintSha256 = "new-test-origin-address",
            Status = AddressAdmissionStatus.Admitted, CatalogReleaseId = evidence.CatalogReleaseId,
            CountryPlaceId = evidence.CountryPlaceId, PostalCode = "99999",
            AdmittedAtUtc = DateTime.UtcNow.AddMinutes(1), CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var restored = await (Task<ShipmentOriginDto?>)resolver.Invoke(service, new object[] { shipment, order, CancellationToken.None })!;
        Assert.Equal(postalCode, restored!.PostalCode);
        Assert.Equal(snapshotJson, shipment.OriginSnapshotJson);

        var builder = typeof(CarrierShipmentService).GetMethod("BuildDhlRequest",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var parcel = new ParcelEvidence(1, tenantId, shipmentId, order.Id,
            new ParcelMeasurement(10, 20, 30, "cm", 1, "kg", 1, "piece-1"),
            10, 20, 30, 1, Array.Empty<ParcelContent>(), Guid.NewGuid(), DateTime.UtcNow);
        var request = (CreateShipmentRequest)builder.Invoke(null,
            new object?[] { shipment, order, restored, "EG", Array.Empty<ResolvedProviderGeography>(), null, parcel, "test-request", null })!;
        Assert.Equal(postalCode, request.OriginZipCode);
        Assert.Equal(0, adapter.CreateShipmentCallCount);
    }

    [Fact]
    public async Task Dhl_NewAttemptCannotBypassLocalOnlyReadiness()
    {
        var tenantId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        SeedOrderAndShipment(db, tenantId, shipmentId, ShipmentStatus.PendingExternalRegistration, CarrierCodes.Dhl);
        var handler = new RejectDhlNetworkHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("DHL")).Returns(new HttpClient(handler));
        var service = CreateService(db, tenantId, new DhlAdapter(factory.Object, NullLogger<DhlAdapter>.Instance));

        var result = await service.ExecuteCarrierForExistingShipmentAsync(tenantId, shipmentId, "express");

        Assert.False(result.Success);
        Assert.Equal("CARRIER_NOT_CONFIGURED", result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
        var shipment = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(0, shipment.ExternalRegistrationAttemptCount);
        Assert.Null(shipment.ExternalRegistrationCompletedAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrackingCatchUp_RequiresActualHistoryAndDoesNotReenterProvider(bool includeHistory)
    {
        var tenantId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        await using var db = CreateDbContext(tenantId);
        SeedOrderAndShipment(db, tenantId, shipmentId, ShipmentStatus.ReadyForPickup, DeterministicCarrierCode, trackingNumber: "TRACK-1");
        var observedAt = DateTime.UtcNow.AddMinutes(-10);
        var adapter = new DeterministicCarrierAdapter
        {
            TrackingResult = new ShipmentTrackingResult
            {
                Success = true, CurrentStatus = ShipmentStatus.InTransit, RawResponse = "synthetic-tracking",
                CodCollected = true,
                Events = includeHistory ? [
                    new TrackingEvent { Status = ShipmentStatus.PickedUp, Timestamp = observedAt, Description = "Observed pickup" },
                    new TrackingEvent { Status = ShipmentStatus.InTransit, Timestamp = observedAt.AddMinutes(1), Description = "Observed transit" }
                ] : []
            }
        };
        var service = CreateService(db, tenantId, adapter);
        var result = await service.SyncTrackingAsync(tenantId, shipmentId);
        var persisted = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(includeHistory, result.Success);
        Assert.Equal(includeHistory, result.StatusChanged);
        Assert.Equal(includeHistory ? ShipmentStatus.InTransit : ShipmentStatus.ReadyForPickup, persisted.Status);
        Assert.Equal(1, adapter.TrackingCalls);
        Assert.Equal(0, adapter.CreateShipmentCallCount);
        Assert.False(persisted.CodCollected); // a non-COD shipment can never acquire money authority
        if (includeHistory)
        {
            Assert.Equal(observedAt, persisted.PickedUpAtUtc);
            Assert.Equal(observedAt.AddMinutes(1), persisted.ShippedAtUtc);
        }
        else
        {
            Assert.Equal("TRACKING_PROGRESSION_UNRESOLVED", result.ErrorCode);
            Assert.Null(persisted.PickedUpAtUtc);
            Assert.Null(persisted.ShippedAtUtc);
            Assert.Equal("synthetic-tracking", persisted.CarrierTrackingResponse);
        }
    }

    [Fact]
    public async Task TrackingCatchUp_UnresolvedCodObservationCannotPublishCollection()
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.ReadyForPickup, DeterministicCarrierCode, trackingNumber: "TRACK-1");
        var shipment = await db.Shipments.SingleAsync();
        shipment.IsCashOnDelivery = true; shipment.CodAmount = 150m;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var adapter = new DeterministicCarrierAdapter { TrackingResult = new() { Success = true, CurrentStatus = ShipmentStatus.Delivered, CodCollected = true } };
        var result = await CreateService(db, tenant, adapter).SyncTrackingAsync(tenant, id);
        Assert.False(result.Success);
        Assert.False((await db.Shipments.AsNoTracking().SingleAsync()).CodCollected);
    }

    private CarrierShipmentService CreateService(
        ApplicationDbContext db,
        Guid tenantId,
        IShippingCarrierAdapter adapter)
        => CreateCarrierService(db, tenantId, adapter);

    [Theory]
    [InlineData("manual")]
    [InlineData("bosta")]
    [InlineData("aramex")]
    [InlineData("dhl")]
    [InlineData("fedex")]
    [InlineData("mylerz")]
    public async Task AdminReadProjectionAndFiltersPreserveStoredEvidenceAndTenantIsolation(string carrier)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.ReadyForPickup, carrier, trackingNumber: "TRACK-1", externalRegistrationState: "ReconciliationRequired");
        var shipment = await db.Shipments.SingleAsync();
        shipment.IsCashOnDelivery = true; shipment.CodAmount = 150m;
        shipment.LabelData = "JVBERi0x"; shipment.LabelFormat = "pdf"; shipment.AwbNumber = "AWB-1";
        shipment.ExternalRegistrationError = "Synthetic unresolved outcome";
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new PointOfSale.API.Services.Admin.AdminShipmentService(db, new NoOpEventPublisher(),
            Mock.Of<IOrderFulfillmentService>(), Mock.Of<IMarketplaceEventOutboxService>(),
            NullLogger<PointOfSale.API.Services.Admin.AdminShipmentService>.Instance);
        var details = await service.GetShipmentDetailsAsync(tenant, id, "closure-test");
        Assert.NotNull(details);
        Assert.Equal(carrier, details.CarrierCode);
        Assert.Equal("AWB-1", details.AwbNumber);
        Assert.Equal("JVBERi0x", details.LabelData);
        Assert.Equal("pdf", details.LabelFormat);
        Assert.Equal("Customer", details.Recipient.Name);
        Assert.Equal("ReconciliationRequired", details.ExternalRegistrationState);
        Assert.Equal(shipment.CreatedAt, details.CreatedAtUtc);
        Assert.True(details.IsCashOnDelivery); Assert.Equal(150m, details.CodAmount); Assert.False(details.CodCollected);
        var filter = new PointOfSale.API.DTOs.Admin.AdminShipmentListQuery { Carrier = carrier, IsCod = true, Search = "ORD-SHIP-1",
            FromDate = DateTime.UtcNow.Date, ToDate = DateTime.UtcNow.Date };
        Assert.Single((await service.ListShipmentsAsync(tenant, filter, "closure-test")).Items);
        filter.IsCod = false;
        Assert.Empty((await service.ListShipmentsAsync(tenant, filter, "closure-test")).Items);
        filter.IsCod = true; filter.Carrier = carrier[..2];
        Assert.Empty((await service.ListShipmentsAsync(tenant, filter, "closure-test")).Items);
        Assert.Null(await service.GetShipmentDetailsAsync(Guid.NewGuid(), id, "closure-test"));
    }

    private CarrierShipmentService CreateCarrierService(
        ApplicationDbContext db,
        Guid tenantId,
        IShippingCarrierAdapter adapter,
        ICarrierIntegrationAuthority? authorityOverride = null,
        ICarrierCredentialEncryptionService? encryptionOverride = null,
        ICarrierGeographicMappingService? mappingOverride = null)
    {
        var registry = new Mock<IShippingCarrierRegistry>(MockBehavior.Strict);
        registry.Setup(x => x.GetAdapter(It.IsAny<string>())).Returns(adapter);
        registry.Setup(x => x.IsCarrierSupported(It.IsAny<string>())).Returns(true);
        registry.Setup(x => x.GetAllAdapters()).Returns([adapter]);
        registry.Setup(x => x.GetSupportedCarrierCodes()).Returns([adapter.CarrierCode]);

        var encryption = new Mock<ICarrierCredentialEncryptionService>(MockBehavior.Strict);
        encryption.Setup(x => x.Decrypt(It.IsAny<string?>())).Returns<string?>(value => value ?? string.Empty);
        var fulfillmentService = new Mock<IOrderFulfillmentService>(MockBehavior.Strict);
        fulfillmentService
            .Setup(x => x.SynchronizeFromShipmentAsync(
                It.IsAny<Guid>(),
                It.IsAny<FulfillmentStatus>(),
                It.IsAny<FulfillmentRequestSource>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid orderId, FulfillmentStatus targetStatus, FulfillmentRequestSource source, Guid? shipmentIdArg, string? trackingNumber, string? actor, string? idempotencyKey, CancellationToken ct) =>
                OrderFulfillmentResult.Succeeded(orderId, "TEST", targetStatus, targetStatus, source, idempotencyKey));

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        var geographicMapping = new Mock<ICarrierGeographicMappingService>();
        geographicMapping
            .Setup(service => service.ResolvePublishedMappingAsync(
                It.IsAny<CarrierGeographicMappingRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CarrierGeographicMappingRequest request, CancellationToken _) =>
                new CarrierGeographicMappingResult(
                    true,
                    Guid.NewGuid(),
                    1,
                    "test-release",
                    request.Environment,
                    request.AccountScopeKind,
                    request.AccountScopeId,
                    DateTime.UtcNow.AddDays(1),
                    "EG",
                    new[]
                    {
                        new CarrierGeographicMappingItem(
                            Guid.NewGuid(),
                            null,
                            request.Lineage.LocalityPlaceId ?? request.Lineage.CountryPlaceId,
                            request.Lineage.CountryPlaceId,
                            "City",
                            "provider-city",
                            null,
                            "Provider City",
                            null)
                    },
                    null));

        var capabilityRegistry = new Mock<ICarrierCapabilityRegistry>(MockBehavior.Strict);
        capabilityRegistry.Setup(service => service.GetCapability(It.IsAny<string>()))
            .Returns((string code) => new CarrierCapabilityDescriptor(
                CarrierCodes.Normalize(code),
                "Deterministic Carrier",
                CarrierProductionCapability.ProductionReady,
                new[] { new CarrierCredentialFieldDescriptor("apiKey", "API key", true, true, "ApiKey") },
                EnvironmentSupported: true,
                ConnectionTestSupported: false,
                code == CarrierCodes.Dhl ? CarrierConnectionTestMode.LocalOnly : CarrierConnectionTestMode.Unsupported,
                new[] { "standard", "express" },
                RequiresOriginReadiness: false,
                RequiresGeographyMapping: true,
                SupportsWebhooks: false,
                Array.Empty<string>()));
        var authority = new CarrierIntegrationAuthority(
            db,
            registry.Object,
            capabilityRegistry.Object,
            encryption.Object,
            NullLogger<CarrierIntegrationAuthority>.Instance);

        return new CarrierShipmentService(
            db,
            registry.Object,
            encryptionOverride ?? encryption.Object,
            new NoOpEventPublisher(),
            fulfillmentService.Object,
            _telemetry,
            tenantContext,
            Mock.Of<ICustomsEvidenceAdmissionService>(),
            mappingOverride ?? geographicMapping.Object,
            NullLogger<CarrierShipmentService>.Instance,
            authorityOverride ?? authority);
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId, string? databaseName = null,
        Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null,
        Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot? storeRoot = null)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"ShipmentExternalRegistrationSafety_{tenantId}_{Guid.NewGuid()}", storeRoot);
        if (interceptor != null) builder.AddInterceptors(interceptor);
        var options = builder.Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        return new ApplicationDbContext(options, tenantContext);
    }

    private static void SeedOrderAndShipment(
        ApplicationDbContext db,
        Guid tenantId,
        Guid shipmentId,
        ShipmentStatus shipmentStatus,
        string carrierCode,
        string? externalReferenceId = null,
        string? trackingNumber = null,
        string? carrierRequestId = null,
        int externalRegistrationAttemptCount = 0,
        string? externalRegistrationState = null,
        string? originPostalCode = null,
        Action<Shipment, Order>? beforeSave = null)
    {
        var orderId = Guid.NewGuid();
        var shippingMethodId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var catalogReleaseId = Guid.NewGuid();
        var countryPlaceId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.Set<GeographicPlace>().Add(new GeographicPlace
        {
            Id = countryPlaceId,
            StableKey = "country:eg",
            PlaceKind = GeographicPlaceKind.Country,
            CountryPlaceId = countryPlaceId,
            CanonicalCode = "EG",
            Status = GeographicLifecycleStatus.Active,
            ValidFromUtc = now.AddDays(-1),
            IntroducedCatalogReleaseId = catalogReleaseId,
            Source = "test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        db.Set<GeographicCountryProfile>().Add(new GeographicCountryProfile
        {
            Id = Guid.NewGuid(),
            CountryPlaceId = countryPlaceId,
            Iso2 = "EG",
            Iso3 = "EGY",
            DefaultLanguageTag = "en",
            SupportedLanguageTags = ["en"],
            PostalCodePolicy = GeographicPostalCodePolicy.Optional,
            RequiresStreetLine1 = true,
            IsActive = true,
            IntroducedCatalogReleaseId = catalogReleaseId,
            CreatedAtUtc = now
        });

        db.Set<GeographicPlaceName>().Add(new GeographicPlaceName
        {
            Id = Guid.NewGuid(),
            PlaceId = countryPlaceId,
            LanguageTag = "en",
            NameKind = GeographicNameKind.Display,
            Name = "Egypt",
            NormalizedName = "EGYPT",
            SearchName = "egypt",
            DisplayPriority = 1,
            IsPreferred = true,
            ValidFromUtc = now.AddDays(-1),
            IntroducedCatalogReleaseId = catalogReleaseId,
            CreatedAtUtc = now
        });

        db.Set<Warehouse>().Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Warehouse",
            Code = "WH-1",
            Address = "Warehouse Address",
            Phone = "02000000000",
            IsActive = true,
            CreatedAt = now
        });

        db.Set<AddressAdmissionEvidence>().Add(new AddressAdmissionEvidence
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerDomain = AddressAdmissionOwnerDomain.Warehouse,
            OwnerId = warehouseId,
            AddressFingerprintSha256 = "test-origin-address",
            Status = AddressAdmissionStatus.Admitted,
            CatalogReleaseId = catalogReleaseId,
            CountryPlaceId = countryPlaceId,
            PostalCode = originPostalCode,
            AdmittedAtUtc = now,
            CreatedAtUtc = now
        });

        db.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-SHIP-1",
            Currency = "EGP",
            WarehouseId = warehouseId,
            ShippingAddress = "123 Street",
            ShippingCity = "Cairo",
            ShippingCountry = "EG",
            GuestName = "Customer",
            GuestPhone = "01000000000",
            PaymentMethodCode = "COD",
            Total = 150m,
            ShippingCost = 25m,
            ShippingMethodId = shippingMethodId,
            ShippingMethodName = carrierCode == CarrierCodes.Dhl ? "Purchased Express" : "Purchased Standard",
            ShippingServiceLevel = carrierCode == CarrierCodes.Dhl ? "express" : "standard",
            FulfillmentMode = FulfillmentMode.Carrier,
            FulfillmentCarrierCode = carrierCode,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        db.Shipments.Add(new Shipment
        {
            Id = shipmentId,
            TenantId = tenantId,
            OrderId = orderId,
            ShippingMethodId = shippingMethodId,
            ShippingMethodName = carrierCode == CarrierCodes.Dhl ? "Purchased Express" : "Purchased Standard",
            CarrierCode = carrierCode,
            ServiceLevel = carrierCode == CarrierCodes.Dhl ? "express" : "standard",
            ShipmentNumber = "SHP-1",
            Status = shipmentStatus,
            Currency = "EGP",
            ShippingCost = 25m,
            RecipientName = "Customer",
            RecipientPhone = "01000000000",
            ShippingAddress = "123 Street",
            ShippingCity = "Cairo",
            ShippingCountry = "EG",
            DestinationAddressAdmissionEvidenceId = Guid.NewGuid(),
            DestinationGeographicCatalogReleaseId = Guid.NewGuid(),
            DestinationCountryPlaceId = Guid.NewGuid(),
            DestinationLocalityPlaceId = Guid.NewGuid(),
            DestinationGeographicSnapshotCreatedAtUtc = DateTime.UtcNow,
            DestinationGeographicSnapshotVersion = 1,
            ExternalReferenceId = externalReferenceId,
            CarrierRequestId = carrierRequestId,
            TrackingNumber = trackingNumber,
            ExternalRegistrationRequestedAtUtc = DateTime.UtcNow,
            ExternalRegistrationAttemptCount = externalRegistrationAttemptCount,
            ExternalRegistrationState = externalRegistrationState,
            CreatedAt = DateTime.UtcNow
        });

        if (externalRegistrationAttemptCount > 0)
        {
            // A prior attempted call must carry its original retained v1 origin.
            // Recovery may not construct it from the current Warehouse fixture.
            var shipment = db.ChangeTracker.Entries<Shipment>().Single().Entity;
            var admission = db.ChangeTracker.Entries<AddressAdmissionEvidence>().Single().Entity;
            var original = new ShipmentOriginDto(warehouseId, admission.Id, catalogReleaseId, countryPlaceId, null, null, null, null,
                "EG", "Egypt", "", "", "", "", "Warehouse", "02000000000", "Warehouse Address", now, 1) { PostalCode = originPostalCode };
            shipment.OriginWarehouseId = warehouseId; shipment.OriginAddressAdmissionEvidenceId = admission.Id;
            shipment.OriginGeographicCatalogReleaseId = catalogReleaseId; shipment.OriginCountryPlaceId = countryPlaceId;
            shipment.OriginCountryIso2 = "EG"; shipment.OriginCountryDisplayName = "Egypt";
            shipment.OriginContactName = original.ContactName; shipment.OriginContactPhone = original.ContactPhone; shipment.OriginStreet = original.Street;
            shipment.OriginGeographicSnapshotVersion = 1; shipment.OriginGeographicSnapshotCreatedAtUtc = now;
            shipment.OriginSnapshotJson = JsonSerializer.Serialize(original);
        }

        // Current method state deliberately differs from the immutable order/shipment
        // snapshot and is inactive. Historical execution must not reload it.
        db.ShippingMethods.Add(new ShippingMethod
        {
            Id = shippingMethodId,
            TenantId = tenantId,
            Name = "Admin-mutated method",
            CarrierCode = carrierCode,
            ServiceLevel = "standard",
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        });

        db.Set<CarrierConfiguration>().Add(new CarrierConfiguration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CarrierCode = carrierCode,
            DisplayName = "Test Carrier",
            IsEnabled = true,
            ApiKey = "api-key",
            ApiSecret = "api-secret",
            PickupContactName = "Warehouse",
            PickupAddress = "Warehouse Address",
            PickupCity = "Cairo",
            PickupCountry = "EG",
            PickupPhone = "02000000000",
            CreatedAt = DateTime.UtcNow
        });

        beforeSave?.Invoke(db.ChangeTracker.Entries<Shipment>().Single().Entity, db.ChangeTracker.Entries<Order>().Single().Entity);
        db.SaveChanges();
        db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _telemetry.Dispose();
    }

    private sealed class DeterministicCarrierAdapter : IShippingCarrierAdapter, IBostaPickupLocationDiscovery
    {
        public string CarrierCode => DeterministicCarrierCode;
        public string DisplayName => "Deterministic Carrier";
        public int CreateShipmentCallCount { get; private set; }
        public int LookupByCarrierRequestIdCallCount { get; private set; }
        public int TrackingCalls { get; private set; }
        public CreateShipmentRequest? LastCreateRequest { get; private set; }
        public ShipmentTrackingResult TrackingResult { get; init; } = new() { Success = true, CurrentStatus = ShipmentStatus.ReadyForPickup };
        public Func<Task>? BeforeLookup { get; init; }
        public Func<Task>? BeforeCreate { get; init; }
        public CreateShipmentResult? CreateResult { get; init; }
        private readonly Dictionary<string, CreateShipmentResult> _shipmentsByCarrierRequestId = new(StringComparer.Ordinal);

        public Task<BostaPickupLocationResult> GetPickupLocationsAsync(CarrierCredentials credentials, CancellationToken ct = default) =>
            Task.FromResult(new BostaPickupLocationResult(true, [new("synthetic-owned-pickup", true, "EG", "synthetic-city", "Synthetic city", "synthetic-district", "Synthetic district") {CountryId = "synthetic-country"}], null));

        public void AddExistingShipment(
            string carrierRequestId,
            string externalReferenceId,
            string trackingNumber)
        {
            _shipmentsByCarrierRequestId[carrierRequestId] = new CreateShipmentResult
            {
                Success = true,
                ExternalReferenceId = externalReferenceId,
                TrackingNumber = trackingNumber,
                TrackingUrl = $"https://carrier.test/{trackingNumber}",
                AwbNumber = $"AWB-{trackingNumber}",
                InitialStatus = ShipmentStatus.ReadyForPickup,
                RawResponse = "{\"ok\":true,\"reconciled\":true}"
            };
        }

        public Task<ShippingQuoteResult> GetQuoteAsync(ShippingQuoteRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ShippingQuoteResult { Success = false });

        public async Task<CreateShipmentResult> CreateShipmentAsync(CreateShipmentRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default)
        {
            CreateShipmentCallCount++;
            LastCreateRequest = request;
            if (BeforeCreate != null) await BeforeCreate();

            var result = new CreateShipmentResult
            {
                Success = true,
                ExternalReferenceId = $"carrier-shipment-{CreateShipmentCallCount}",
                TrackingNumber = $"TRACK-{CreateShipmentCallCount}",
                TrackingUrl = $"https://carrier.test/TRACK-{CreateShipmentCallCount}",
                AwbNumber = $"AWB-{CreateShipmentCallCount}",
                InitialStatus = ShipmentStatus.ReadyForPickup,
                RawResponse = "{\"ok\":true}"
            };

            if (!string.IsNullOrWhiteSpace(request.CarrierRequestId))
            {
                _shipmentsByCarrierRequestId[request.CarrierRequestId] = result;
            }

            return CreateResult ?? result;
        }

        public async Task<CreateShipmentResult?> GetShipmentByCarrierRequestIdAsync(
            string carrierRequestId,
            CarrierCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            LookupByCarrierRequestIdCallCount++;
            if (BeforeLookup != null) await BeforeLookup();
            return
                _shipmentsByCarrierRequestId.TryGetValue(carrierRequestId, out var result)
                    ? result
                    : null;
        }

        public Task<CancelShipmentResult> CancelShipmentAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CancelShipmentResult { Success = true });

        public Task<ShipmentTrackingResult> GetTrackingAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default)
        {
            TrackingCalls++;
            return Task.FromResult(TrackingResult);
        }

        public Task<CarrierValidationResult> ValidateCredentialsAsync(CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CarrierValidationResult { Success = true });

        public IReadOnlyList<ServiceLevelInfo> GetSupportedServiceLevels() =>
        [
            new() { Code = "standard", Name = "Standard" },
            new() { Code = "express", Name = "Express" }
        ];

        public WebhookParseResult? ParseWebhook(string payload, string? signature, string? webhookSecret) => null;
    }

    private sealed class NoOpEventPublisher : IEventPublisher
    {
        public Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
            where T : IntegrationEvent =>
            Task.CompletedTask;

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent =>
            Task.CompletedTask;

        public Task PublishSensitiveAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent =>
            Task.CompletedTask;
    }
}
