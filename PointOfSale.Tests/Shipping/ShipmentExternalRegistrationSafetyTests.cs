using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed class ShipmentExternalRegistrationSafetyTests : IDisposable
{
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
            CarrierCodes.Mylerz);

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
            CarrierCodes.Mylerz,
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

    private CarrierShipmentService CreateService(
        ApplicationDbContext db,
        Guid tenantId,
        DeterministicCarrierAdapter adapter)
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
                CarrierConnectionTestMode.Unsupported,
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
            encryption.Object,
            new NoOpEventPublisher(),
            fulfillmentService.Object,
            _telemetry,
            tenantContext,
            Mock.Of<ICustomsEvidenceAdmissionService>(),
            geographicMapping.Object,
            NullLogger<CarrierShipmentService>.Instance,
            authority);
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ShipmentExternalRegistrationSafety_{tenantId}_{Guid.NewGuid()}")
            .Options;

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
        string? externalRegistrationState = null)
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

        db.SaveChanges();
        db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _telemetry.Dispose();
    }

    private sealed class DeterministicCarrierAdapter : IShippingCarrierAdapter
    {
        public string CarrierCode => CarrierCodes.Mylerz;
        public string DisplayName => "Deterministic Carrier";
        public int CreateShipmentCallCount { get; private set; }
        public int LookupByCarrierRequestIdCallCount { get; private set; }
        private readonly Dictionary<string, CreateShipmentResult> _shipmentsByCarrierRequestId = new(StringComparer.Ordinal);

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

        public Task<CreateShipmentResult> CreateShipmentAsync(CreateShipmentRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default)
        {
            CreateShipmentCallCount++;

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

            return Task.FromResult(result);
        }

        public Task<CreateShipmentResult?> GetShipmentByCarrierRequestIdAsync(
            string carrierRequestId,
            CarrierCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            LookupByCarrierRequestIdCallCount++;
            return Task.FromResult(
                _shipmentsByCarrierRequestId.TryGetValue(carrierRequestId, out var result)
                    ? result
                    : null);
        }

        public Task<CancelShipmentResult> CancelShipmentAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CancelShipmentResult { Success = true });

        public Task<ShipmentTrackingResult> GetTrackingAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ShipmentTrackingResult { Success = true, CurrentStatus = ShipmentStatus.ReadyForPickup });

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
