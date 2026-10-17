using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Geographic;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Migrations;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

public sealed class FedExCanonicalEvidenceTests : IDisposable
{
    private readonly Guid tenant = Guid.NewGuid();
    private readonly Guid actor = Guid.NewGuid();
    private readonly Guid configuration = Guid.NewGuid();
    private readonly Guid country = Guid.NewGuid();
    private readonly Guid state = Guid.NewGuid();
    private readonly Guid locality = Guid.NewGuid();
    private readonly Guid catalog = Guid.NewGuid();
    private readonly DbContextOptions<ApplicationDbContext> options;
    private readonly string storeName = $"FedExEvidence-{Guid.NewGuid()}";
    // Share provider services, not records: each fixture retains its unique name.
    private static readonly InMemoryDatabaseRoot storeRoot = new();
    private readonly Mock<ICarrierIntegrationAuthority> authority = new();
    private readonly Mock<ICarrierGeographicMappingService> geography = new();
    private readonly Mock<ICustomsEvidenceAdmissionService> customs = new(MockBehavior.Strict);
    private readonly CommerceReliabilityTelemetry telemetry = new();
    private readonly Handler handler = new();
    private readonly ICarrierCredentialEncryptionService encryption = new CarrierCredentialEncryptionService(
        new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(), NullLogger<CarrierCredentialEncryptionService>.Instance);
    private string account = "510087000";
    private string apiSecret = "synthetic-secret";
    private bool sandbox = true;
    private bool configurationSandbox = true;
    private int resolutions;
    private readonly Guid shipmentId = Guid.NewGuid();
    private readonly Guid orderId = Guid.NewGuid();
    private readonly Guid warehouseId = Guid.NewGuid();
    private readonly Guid originEvidenceId = Guid.NewGuid();
    private readonly Guid destinationEvidenceId = Guid.NewGuid();

    public FedExCanonicalEvidenceTests()
    {
        options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(storeName, storeRoot)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        authority.Setup(a => a.GetServiceLevelReadinessAsync(tenant, "fedex", "fedex_ground", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierServiceLevelReadiness("fedex", "fedex_ground", true, true, true, true, []));
        // Synthetic admission allows testing the wiring; it does not change real NotCertified capability.
        authority.Setup(a => a.ResolveCredentialsAsync(tenant, "fedex", It.IsAny<CarrierIntegrationPurpose>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { resolutions++; return Resolution(); });
        geography.Setup(g => g.ResolvePublishedMappingAsync(It.IsAny<CarrierGeographicMappingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CarrierGeographicMappingRequest request, CancellationToken _) => new CarrierGeographicMappingResult(
                true, Guid.NewGuid(), 1, "synthetic-mapping", request.Environment, request.AccountScopeKind, request.AccountScopeId,
                DateTime.UtcNow.AddDays(1), "US", [new CarrierGeographicMappingItem(Guid.NewGuid(), null, state, country, "State", "TN", "TN", "Tennessee", null)], null));
        using var db = Db();
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Synthetic", Slug = $"fedex-{tenant:N}", IsActive = true, Status = TenantStatus.Active });
        db.Users.Add(new User { Id = actor, TenantId = tenant, Username = "synthetic", Email = "actor@example.invalid", PasswordHash = "mock", Role = UserRole.Warehouse, IsActive = true });
        db.Warehouses.Add(new Warehouse { Id = warehouseId, TenantId = tenant, Name = "Canonical warehouse", Address = "10 Canonical Street", Phone = "15555550100", IsActive = true });
        db.Set<GeographicCatalogRelease>().Add(new GeographicCatalogRelease { Id = catalog, ReleaseKey = "synthetic", ReleaseNumber = 1, Status = GeographicReleaseStatus.Published, CreatedByUserId = actor, ChecksumSha256 = new string('A', 64) });
        foreach (var (id, kind, code, name) in new[] {
            (country, GeographicPlaceKind.Country, "US", "United States"),
            (state, GeographicPlaceKind.AdministrativeArea, "TN", "Tennessee"),
            (locality, GeographicPlaceKind.Locality, "MEMPHIS", "Memphis") })
        {
            db.Set<GeographicPlace>().Add(new GeographicPlace { Id = id, CountryPlaceId = country, PlaceKind = kind, CanonicalCode = code,
                StableKey = $"synthetic:{id:N}", Status = GeographicLifecycleStatus.Active, IntroducedCatalogReleaseId = catalog });
            db.Set<GeographicPlaceName>().Add(new GeographicPlaceName { Id = Guid.NewGuid(), PlaceId = id, Name = name, IsPreferred = true,
                LanguageTag = "en", NameKind = GeographicNameKind.Display, IntroducedCatalogReleaseId = catalog });
        }
        db.Set<GeographicCountryProfile>().Add(new GeographicCountryProfile { Id = Guid.NewGuid(), CountryPlaceId = country, Iso2 = "US", Iso3 = "USA", IsActive = true, IntroducedCatalogReleaseId = catalog });
        db.Set<AddressAdmissionEvidence>().Add(new AddressAdmissionEvidence { Id = originEvidenceId, TenantId = tenant, OwnerDomain = AddressAdmissionOwnerDomain.Warehouse,
            OwnerId = warehouseId, AddressFingerprintSha256 = new string('A', 64), PostalCode = "38116", Status = AddressAdmissionStatus.Admitted,
            CatalogReleaseId = catalog, CountryPlaceId = country, AdministrativeAreaPlaceId = state, LocalityPlaceId = locality, AdmittedAtUtc = DateTime.UtcNow });
        var method = Guid.NewGuid();
        db.Orders.Add(new Order { Id = orderId, TenantId = tenant, OrderNumber = "PACKED-ORDER", WarehouseId = warehouseId,
            Status = OrderStatus.Confirmed, FulfillmentMode = FulfillmentMode.Carrier, FulfillmentStatus = FulfillmentStatus.Confirmed,
            FulfillmentCarrierCode = "fedex", ShippingServiceLevel = "fedex_ground", ShippingMethodId = method, ShippingMethodName = "Purchased Ground",
            PaymentMethodCode = "PREPAID", PaymentStatus = PaymentStatus.Paid, FinancialPostingStatus = FinancialPostingStatus.NotRequired,
            Currency = "USD", Total = 100, ShippingCost = 12, ShippingAddress = "20 Destination Street", ShippingStreet = "20 Destination Street", ShippingCountry = "US",
            ShippingZipCode = "38117", ShippingAddressAdmissionEvidenceId = destinationEvidenceId, ShippingGeographicCatalogReleaseId = catalog,
            ShippingGeographicSnapshotVersion = 1, ShippingGeographicSnapshotCreatedAtUtc = DateTime.UtcNow,
            ShippingCountryPlaceId = country, ShippingAdministrativeAreaPlaceId = state, ShippingLocalityPlaceId = locality,
            ShippingLocalityDisplayName = "Memphis", ShippingAdministrativeAreaDisplayName = "Tennessee", GuestName = "Receiver", GuestPhone = "15555550101",
            Items = [new OrderItem { Id = Guid.NewGuid(), OrderId = orderId, ProductId = Guid.NewGuid(), Quantity = 2, UnitPrice = 50, Total = 100 }] });
        db.Shipments.Add(new Shipment { Id = shipmentId, TenantId = tenant, OrderId = orderId, CarrierCode = "fedex", ServiceLevel = "fedex_ground",
            ShippingMethodId = method, ShippingMethodName = "Purchased Ground", Status = ShipmentStatus.PendingExternalRegistration, ShipmentNumber = "SYNTHETIC-FEDEX",
            ShippingAddress = "20 Destination Street", DestinationStreet = "20 Destination Street", ShippingCountry = "US", ShippingZipCode = "38117",
            DestinationCountryPlaceId = country, DestinationAdministrativeAreaPlaceId = state, DestinationLocalityPlaceId = locality,
            DestinationLocalityDisplayName = "Memphis", DestinationAdministrativeAreaDisplayName = "Tennessee", DestinationAddressAdmissionEvidenceId = destinationEvidenceId,
            DestinationGeographicCatalogReleaseId = catalog, DestinationGeographicSnapshotVersion = 1, DestinationGeographicSnapshotCreatedAtUtc = DateTime.UtcNow,
            RecipientName = "Receiver", RecipientPhone = "15555550101", Currency = "USD", ShippingCost = 12, WeightKg = 99,
            ExternalRegistrationRequestedAtUtc = DateTime.UtcNow, ExternalRegistrationState = "PendingExternalCall" });
        db.SaveChanges();
    }

    private ApplicationDbContext Db() => new(options, new TestTenantContext(tenant));
    private async Task ImportHistoricalShipmentAsync(Action<Shipment> corruption)
    {
        // Reader containment still covers malformed pre-existing history. This
        // unguarded import is confined to the explicit process-local fake store;
        // it cannot connect to PostgreSQL or alter configured business records.
        await using var model = Db();
        await using var import = new DbContext(new DbContextOptionsBuilder()
            .UseInMemoryDatabase(storeName, storeRoot).UseModel(model.Model).Options);
        var shipment = await import.Set<Shipment>().IgnoreQueryFilters().SingleAsync();
        corruption(shipment);
        await import.SaveChangesAsync();
    }
    private CarrierCredentialsResolution Resolution() => new(true, "fedex", new CarrierCredentials { ApiKey = "synthetic-client", ApiSecret = apiSecret, AccountId = account,
        UseSandbox = sandbox, ExtraConfig = new() { ["pickupType"] = "DROPOFF_AT_FEDEX_LOCATION" } },
        JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>($$"""{"Id":"{{configuration}}","CarrierCode":"fedex","IsEnabled":true,"UseSandbox":{{configurationSandbox.ToString().ToLowerInvariant()}},"DisplayName":"FedEx","PickupAddress":"Untrusted config origin"}""")!, []);
    private CarrierShipmentService Service(ApplicationDbContext db)
    {
        var oauth = new Mock<IFedExOAuthClient>(MockBehavior.Strict);
        oauth.Setup(x => x.GetAuthorizationHeaderAsync(It.IsAny<FedExOAuthRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FedExAuthorizationHeaderResult(true, new AuthenticationHeaderValue("Bearer", "synthetic-token"), DateTimeOffset.UtcNow.AddHours(1), false, null, null));
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(x => x.CreateClient("FedEx")).Returns(new HttpClient(handler));
        var transport = new FedExTransportClient(new FedExOptions(), factory.Object, oauth.Object, NullLogger<FedExTransportClient>.Instance);
        var adapter = new FedExAdapter(oauth.Object, new FedExOptions(), transport);
        var registry = new Mock<IShippingCarrierRegistry>(); registry.Setup(x => x.GetAdapter("fedex")).Returns(adapter);
        var events = new Mock<IEventPublisher>(); events.SetReturnsDefault(Task.CompletedTask);
        var fulfillment = new Mock<IOrderFulfillmentService>();
        fulfillment.Setup(x => x.SynchronizeFromShipmentAsync(It.IsAny<Guid>(), It.IsAny<FulfillmentStatus>(), It.IsAny<FulfillmentRequestSource>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, FulfillmentStatus status, FulfillmentRequestSource source, Guid? shipment, string? tracking, string? actorName, string? key, CancellationToken _) => OrderFulfillmentResult.Succeeded(id, "FIXTURE", status, status, source, key));
        return new(db, registry.Object, encryption, events.Object, fulfillment.Object, telemetry,
            new TestTenantContext(tenant), customs.Object, geography.Object, NullLogger<CarrierShipmentService>.Instance, authority.Object);
    }
    private Task<CarrierShipmentResult> Execute(ApplicationDbContext db) => Service(db).ExecuteCarrierForExistingShipmentAsync(tenant, shipmentId, "untrusted-caller-service");
    private async Task<ParcelEvidence> SaveParcel()
    {
        await using var db = Db();
        return await new ShipmentParcelEvidenceService(db, new TestTenantContext(tenant)).SaveAsync(tenant, shipmentId, actor,
            new ParcelMeasurement(300, 200, 100, "mm", 2300, "g", 1, "ACTUAL-BOX"));
    }

    [Fact]
    public async Task OriginalCredentialIdentity_ChangedSecretCannotAuthorizeReplayOrTracking()
    {
        await SaveParcel();
        await using (var db = Db()) Assert.True((await Execute(db)).Success);
        apiSecret = "different-project-secret";
        await using var replay = Db();
        var result = await Execute(replay);
        Assert.False(result.Success);
        Assert.Equal("FEDEX_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED", result.ErrorCode);
        Assert.Equal(1, handler.Calls);
        var tracking = await Service(replay).SyncTrackingAsync(tenant, shipmentId);
        Assert.False(tracking.Success);
        Assert.Equal("FEDEX_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED", tracking.ErrorCode);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("inactive")][InlineData("role")]
    public async Task FreshRegistration_RequiresCurrentActiveFulfillmentMeasurementActor(string scenario)
    {
        await SaveParcel(); await using var db = Db();
        var user = await db.Users.SingleAsync(u => u.Id == actor);
        if (scenario == "inactive") user.IsActive = false;
        else user.Role = UserRole.Cashier;
        await db.SaveChangesAsync();
        Assert.Equal("PARCEL_MEASURED_ACTOR_NOT_OWNED", (await Execute(db)).ErrorCode);
        Assert.Equal(0, handler.Calls);
        var shipment = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(0, shipment.ExternalRegistrationAttemptCount); Assert.Null(shipment.CarrierRequestSnapshotJson);
    }

    [Fact]
    public async Task HistoricalCredentialBindingAbsence_AllowsLocalReplayButCannotAuthorizeRemoteTracking()
    {
        await SaveParcel();
        await using (var first = Db()) Assert.True((await Execute(first)).Success);
        await ImportHistoricalShipmentAsync(s =>
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(s.CarrierRequestSnapshotJson!)!.AsObject();
            Assert.True(json.Remove("EncryptedCredentialIdentity"));
            s.CarrierRequestSnapshotJson = json.ToJsonString();
            s.CarrierRequestFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s.CarrierRequestSnapshotJson)));
        });
        await using var replay = Db();
        Assert.True((await Execute(replay)).Success); // SQL-only known-identity replay, no invented legacy binding.
        var tracking = await Service(replay).SyncTrackingAsync(tenant, shipmentId);
        Assert.False(tracking.Success);
        Assert.Equal("FEDEX_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED", tracking.ErrorCode);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(1)] [InlineData(99)]
    public async Task NominalOrLegacyWeight_WithoutMeasurements_CannotClaimOrSend(decimal weight)
    {
        await using var db = Db(); var s = await db.Shipments.SingleAsync(); s.WeightKg = weight; await db.SaveChangesAsync();
        Assert.Equal("PARCEL_MEASUREMENTS_REQUIRED", (await Execute(db)).ErrorCode);
        Assert.Equal(0, handler.Calls); Assert.Equal(0, (await db.Shipments.AsNoTracking().SingleAsync()).ExternalRegistrationAttemptCount);
        Assert.Null((await db.Shipments.AsNoTracking().SingleAsync()).CarrierRequestSnapshotJson);
    }

    [Fact]
    public async Task PersistedParcel_CanonicalWarehouseAndExactWireSnapshot_AreAdmittedBeforePost()
    {
        var evidence = await SaveParcel(); Assert.Equal(2.3m, evidence.GrossWeightKg); Assert.Equal(30, evidence.LengthCm);
        handler.BeforeSend = async request =>
        {
            await using var inspect = Db(); var stored = await inspect.Shipments.AsNoTracking().SingleAsync();
            Assert.Equal(1, stored.ExternalRegistrationAttemptCount); Assert.NotNull(stored.ExternalRegistrationLastAttemptAtUtc);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stored.CarrierRequestSnapshotJson!))), stored.CarrierRequestFingerprint);
            using var json = JsonDocument.Parse(stored.CarrierRequestSnapshotJson!);
            var normalized = json.RootElement.GetProperty("Request").Deserialize<FedExShipmentRequest>()!;
            Assert.Equal(FedExContractSerializer.SerializeJson(normalized), await request.Content!.ReadAsStringAsync());
            Assert.Equal(tenant, json.RootElement.GetProperty("TenantId").GetGuid());
            Assert.Equal(shipmentId, json.RootElement.GetProperty("ShipmentId").GetGuid());
            Assert.Equal(configuration, json.RootElement.GetProperty("ConfigurationId").GetGuid());
            Assert.Equal(stored.ExternalRegistrationAttemptCount, json.RootElement.GetProperty("AttemptNumber").GetInt32());
            Assert.Equal(stored.ExternalRegistrationLastAttemptAtUtc, json.RootElement.GetProperty("AttemptStartedAtUtc").GetDateTime());
            Assert.Equal(0, stored.ExternalRegistrationLastAttemptAtUtc!.Value.Ticks % 10);
            Assert.Equal("510087000", normalized.AccountNumber.Value);
            Assert.Equal("10 Canonical Street", normalized.RequestedShipment.Shipper.Address.StreetLines.Single());
            Assert.Equal("TN", normalized.RequestedShipment.Shipper.Address.StateOrProvinceCode);
            Assert.Equal("38116", normalized.RequestedShipment.Shipper.Address.PostalCode);
            Assert.Equal("TN", normalized.RequestedShipment.Recipients.Single().Address.StateOrProvinceCode);
            Assert.Equal(2.3m, normalized.RequestedShipment.RequestedPackageLineItems.Single().Weight.Value);
            Assert.DoesNotContain("synthetic-secret", stored.CarrierRequestSnapshotJson);
            Assert.DoesNotContain("synthetic-token", stored.CarrierRequestSnapshotJson);
            Assert.DoesNotContain("Untrusted config origin", stored.CarrierRequestSnapshotJson);
        };
        await using var db = Db(); var result = await Execute(db);
        Assert.True(result.Success, result.ErrorCode + ":" + result.ErrorMessage); Assert.Equal(1, handler.Calls);
        Assert.Equal(99, (await db.Shipments.AsNoTracking().SingleAsync()).WeightKg);
        Assert.Equal(12, (await db.Orders.AsNoTracking().SingleAsync()).ShippingCost);
        await using var replay = Db(); Assert.True((await Execute(replay)).Success); Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("tenant")] [InlineData("shipment")] [InlineData("order")] [InlineData("contents")]
    [InlineData("future")] [InlineData("actor")] [InlineData("fractional-dimension")] [InlineData("weight-precision")]
    public async Task WrongOrStaleParcelEvidence_FailsBeforeClaimAndHttp(string change)
    {
        var e = await SaveParcel();
        e = change switch {
            "tenant" => e with { TenantId = Guid.NewGuid() }, "shipment" => e with { ShipmentId = Guid.NewGuid() },
            "order" => e with { OrderId = Guid.NewGuid() }, "contents" => e with { Contents = [] },
            "future" => e with { RecordedAtUtc = DateTime.UtcNow.AddHours(1) }, "actor" => e with { MeasuredBy = Guid.NewGuid() },
            "fractional-dimension" => e with { Measurement = e.Measurement with { Length = 301 } },
            _ => e with { Measurement = e.Measurement with { GrossWeight = 2301 } } };
        await using var db = Db(); var s = await db.Shipments.SingleAsync();
        var original = s.ParcelEvidenceJson;
        s.ParcelEvidenceJson = JsonSerializer.Serialize(e);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(original, (await db.Shipments.AsNoTracking().SingleAsync()).ParcelEvidenceJson);
        await ImportHistoricalShipmentAsync(historical => historical.ParcelEvidenceJson = JsonSerializer.Serialize(e));
        Assert.False((await Execute(db)).Success); Assert.Equal(0, handler.Calls);
        Assert.Equal(0, (await db.Shipments.AsNoTracking().SingleAsync()).ExternalRegistrationAttemptCount);
    }

    [Theory]
    [InlineData("postal-missing")] [InlineData("postal-invalid")] [InlineData("state-missing")]
    [InlineData("state-display")] [InlineData("state-invalid")] [InlineData("foreign-warehouse")]
    public async Task MissingOrInvalidCanonicalOrigin_CannotReachHttp(string failure)
    {
        await SaveParcel(); await using var db = Db();
        if (failure is "postal-missing" or "postal-invalid" or "state-missing")
            db.Set<AddressAdmissionEvidence>().Add(new AddressAdmissionEvidence { Id = Guid.NewGuid(), TenantId = tenant,
                OwnerDomain = AddressAdmissionOwnerDomain.Warehouse, OwnerId = warehouseId, AddressFingerprintSha256 = new string('B', 64),
                PostalCode = failure == "postal-missing" ? null : failure == "postal-invalid" ? "invented" : "38116",
                Status = AddressAdmissionStatus.Admitted, CatalogReleaseId = catalog, CountryPlaceId = country,
                AdministrativeAreaPlaceId = failure == "state-missing" ? null : state, LocalityPlaceId = locality, AdmittedAtUtc = DateTime.UtcNow });
        if (failure == "state-display") (await db.Set<GeographicPlace>().SingleAsync(p => p.Id == state)).CanonicalCode = "Tennessee";
        if (failure == "state-invalid") (await db.Set<GeographicPlace>().SingleAsync(p => p.Id == state)).CanonicalCode = "ZZ";
        if (failure == "foreign-warehouse")
        {
            var foreign = Guid.NewGuid();
            db.Warehouses.Add(new Warehouse { Id = foreign, TenantId = Guid.NewGuid(), Name = "Foreign", Address = "Other", Phone = "15555550100", IsActive = true });
            var s = await db.Shipments.SingleAsync();
            var origin = new ShipmentOriginDto(foreign, originEvidenceId, catalog, country, state, locality, null, null,
                "US", "United States", "Tennessee", "Memphis", null, null, "Foreign", "15555550100", "Other", DateTime.UtcNow, 1)
                { PostalCode = "38116", AdministrativeAreaCode = "TN" };
            s.OriginWarehouseId = foreign; s.OriginAddressAdmissionEvidenceId = originEvidenceId; s.OriginGeographicCatalogReleaseId = catalog;
            s.OriginCountryPlaceId = country; s.OriginCountryIso2 = "US"; s.OriginCountryDisplayName = "United States";
            s.OriginContactName = origin.ContactName; s.OriginContactPhone = origin.ContactPhone; s.OriginStreet = origin.Street;
            s.OriginGeographicSnapshotVersion = 1; s.OriginGeographicSnapshotCreatedAtUtc = origin.SnapshottedAtUtc;
            s.OriginSnapshotJson = JsonSerializer.Serialize(origin);
        }
        await db.SaveChangesAsync();
        var result = await Execute(db);
        Assert.Equal(failure switch {
            "postal-missing" => "FEDEX_ORIGIN_POSTAL_EVIDENCE_REQUIRED", "state-missing" => "FEDEX_CANONICAL_GEOGRAPHY_REQUIRED",
            "foreign-warehouse" => "FEDEX_ORIGIN_WAREHOUSE_MISMATCH", _ => "FEDEX_ORIGIN_GEOGRAPHY_INVALID" }, result.ErrorCode);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(0, (await db.Shipments.AsNoTracking().SingleAsync()).ExternalRegistrationAttemptCount);
    }

    [Theory]
    [InlineData("account-invalid")] [InlineData("environment")] [InlineData("account-at-claim")]
    public async Task AccountEnvironmentBinding_CannotChangeBeforeMutation(string failure)
    {
        await SaveParcel();
        if (failure == "account-invalid") account = "untrusted";
        if (failure == "environment") configurationSandbox = false;
        if (failure == "account-at-claim") authority.Setup(a => a.ResolveCredentialsAsync(tenant, "fedex", It.IsAny<CarrierIntegrationPurpose>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { if (++resolutions > 1) account = "999999999"; return Resolution(); });
        await using var db = Db(); Assert.False((await Execute(db)).Success); Assert.Equal(0, handler.Calls);
        var s = await db.Shipments.AsNoTracking().SingleAsync(); Assert.Equal(0, s.ExternalRegistrationAttemptCount); Assert.Null(s.CarrierRequestSnapshotJson);
    }

    [Theory]
    [InlineData("timeout")] [InlineData("response-admission")]
    public async Task AmbiguousCreation_ValidSnapshotCannotAuthorizeASecondPost(string failure)
    {
        await SaveParcel(); handler.Failure = failure;
        await using (var db = Db()) Assert.False((await Execute(db)).Success);
        await using var replay = Db(); Assert.Equal("EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED", (await Execute(replay)).ErrorCode);
        var s = await replay.Shipments.AsNoTracking().SingleAsync(); Assert.Equal(1, s.ExternalRegistrationAttemptCount);
        Assert.Equal("ReconciliationRequired", s.ExternalRegistrationState); Assert.NotNull(s.CarrierRequestSnapshotJson); Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task HistoricalAttemptWithoutSnapshotOrMeasurements_RemainsReconciliationRequired()
    {
        await using var db = Db(); var s = await db.Shipments.SingleAsync(); s.ExternalRegistrationAttemptCount = 1; await db.SaveChangesAsync();
        authority.Setup(a => a.GetServiceLevelReadinessAsync(tenant, "fedex", "fedex_ground", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierServiceLevelReadiness("fedex", "fedex_ground", false, false, true, false, ["CARRIER_NOT_CERTIFIED"]));
        Assert.Equal("EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED", (await Execute(db)).ErrorCode);
        Assert.Equal(0, handler.Calls); Assert.Null((await db.Shipments.AsNoTracking().SingleAsync()).CarrierRequestSnapshotJson);
    }

    [Fact]
    public async Task NotCertifiedReadiness_CannotBeBypassedByCompleteLocalEvidence()
    {
        await SaveParcel(); authority.Setup(a => a.GetServiceLevelReadinessAsync(tenant, "fedex", "fedex_ground", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierServiceLevelReadiness("fedex", "fedex_ground", false, false, true, false, ["CARRIER_NOT_CERTIFIED"]));
        await using var db = Db(); Assert.Equal("CARRIER_NOT_CERTIFIED", (await Execute(db)).ErrorCode);
        Assert.Equal(0, handler.Calls); Assert.Equal(0, resolutions);
    }

    [Fact]
    public void Migration_AddsOnlyNullablePostalEvidence_AndRetainsPreviousTarget()
    {
        var migration = new FedExWarehousePostalEvidence();
        var operation = Assert.IsType<AddColumnOperation>(Assert.Single(migration.UpOperations));
        Assert.Equal("AddressAdmissionEvidence", operation.Table); Assert.Equal("PostalCode", operation.Name);
        Assert.True(operation.IsNullable); Assert.Equal(32, operation.MaxLength); Assert.Null(operation.DefaultValue);
        Assert.IsType<DropColumnOperation>(Assert.Single(migration.DownOperations));
        Assert.Equal(new ShipmentParcelRequestEvidence().TargetModel.GetEntityTypes().Count(), migration.TargetModel.GetEntityTypes().Count());
        Assert.Equal(32, migration.TargetModel.FindEntityType(typeof(AddressAdmissionEvidence).FullName!)!.FindProperty("PostalCode")!.GetMaxLength());
    }

    [Theory]
    [InlineData("account")] [InlineData("environment")] [InlineData("fingerprint")] [InlineData("request")]
    public async Task ChangedDurableIntentOrBinding_OnReplayNeverPosts(string change)
    {
        await SaveParcel(); handler.Failure = "timeout";
        await using (var first = Db()) Assert.False((await Execute(first)).Success);
        if (change == "account") account = "999999999";
        if (change == "environment") sandbox = configurationSandbox = false;
        await using var db = Db(); var stored = await db.Shipments.SingleAsync();
        if (change == "fingerprint") stored.CarrierRequestFingerprint = new string('0', 64);
        if (change == "request")
        {
            stored.CarrierRequestSnapshotJson = stored.CarrierRequestSnapshotJson!.Replace("38116", "99999", StringComparison.Ordinal);
            stored.CarrierRequestFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stored.CarrierRequestSnapshotJson)));
        }
        if (change is "fingerprint" or "request")
        {
            var corruptedJson = stored.CarrierRequestSnapshotJson;
            var corruptedFingerprint = stored.CarrierRequestFingerprint;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            var retained = await db.Shipments.AsNoTracking().SingleAsync();
            Assert.NotEqual(corruptedFingerprint, retained.CarrierRequestFingerprint);
            if (change == "request") Assert.NotEqual(corruptedJson, retained.CarrierRequestSnapshotJson);
            await ImportHistoricalShipmentAsync(historical =>
            {
                historical.CarrierRequestSnapshotJson = corruptedJson;
                historical.CarrierRequestFingerprint = corruptedFingerprint;
            });
        }
        else await db.SaveChangesAsync();
        Assert.Equal("FEDEX_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED", (await Execute(db)).ErrorCode);
        Assert.Equal(1, handler.Calls); Assert.Equal(1, (await db.Shipments.AsNoTracking().SingleAsync()).ExternalRegistrationAttemptCount);
    }

    [Fact]
    public void MigrationTargetAndSnapshot_MatchCurrentModelWithoutDatabaseAccess()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused_offline_model;Username=unused;Password=unused").Options);
        var current = db.GetService<IDesignTimeModel>().Model;
        var initializer = db.GetService<Microsoft.EntityFrameworkCore.Infrastructure.IModelRuntimeInitializer>();
        var predecessor = initializer.Initialize(new ShippingWebhookOwnerClaims().TargetModel, designTime: true);
        var migrated = initializer.Initialize(new CanonicalAddressEvidenceFoundation().TargetModel, designTime: true);
        var snapshot = initializer.Initialize(new ApplicationDbContextModelSnapshot().Model, designTime: true);
        var differ = db.GetService<IMigrationsModelDiffer>();
        Assert.Contains("20261011180000_FedExWarehousePostalEvidence", db.Database.GetMigrations());
        Assert.Contains("20261013210000_CanonicalAddressEvidenceFoundation", db.Database.GetMigrations());
        var addition = Assert.IsType<AddColumnOperation>(Assert.Single(differ.GetDifferences(predecessor.GetRelationalModel(), migrated.GetRelationalModel())));
        Assert.Equal("AddressAdmissionEvidence", addition.Table); Assert.Equal("CanonicalAddressEvidenceJson", addition.Name);
        Assert.True(addition.IsNullable); Assert.Equal("text", addition.ColumnType);
        Assert.Empty(differ.GetDifferences(migrated.GetRelationalModel(), current.GetRelationalModel()));
        Assert.Empty(differ.GetDifferences(snapshot.GetRelationalModel(), current.GetRelationalModel()));
    }

    [Theory]
    [InlineData("postal")] [InlineData("country")] [InlineData("locality")] [InlineData("order-binding")]
    public async Task IncompleteOrContradictoryDestination_FailsBeforeHttp(string change)
    {
        await SaveParcel(); await using var db = Db();
        // Test a contradictory in-memory candidate without rewriting the immutable
        // persisted canonical destination: the executor must reject it before saving.
        var s = await db.Shipments.SingleAsync();
        if (change == "postal") s.ShippingZipCode = "invalid";
        if (change == "country") s.ShippingCountry = "CA";
        if (change == "locality") s.DestinationLocalityDisplayName = "";
        if (change == "order-binding") s.ShippingMethodId = Guid.NewGuid();
        var result = await Execute(db);
        Assert.False(result.Success); Assert.Equal(0, handler.Calls);
        await using var inspect = Db(); Assert.Equal(0, (await inspect.Shipments.SingleAsync()).ExternalRegistrationAttemptCount);
    }

    [Fact]
    public async Task ForeignTenantOrUnauthorizedActor_CannotRecordMeasurements()
    {
        await using var db = Db();
        var foreign = new ShipmentParcelEvidenceService(db, new TestTenantContext(Guid.NewGuid()));
        var input = new ParcelMeasurement(30, 20, 10, "cm", 2.3m, "kg", 1, "BOX");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.SaveAsync(tenant, shipmentId, actor, input));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ShipmentParcelEvidenceService(db, new TestTenantContext(tenant))
            .SaveAsync(tenant, shipmentId, Guid.NewGuid(), input));
        Assert.Null((await db.Shipments.AsNoTracking().SingleAsync()).ParcelEvidenceJson);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task LegacyCreation_CommitsIntentButNeverSendsNominalFedExParcel()
    {
        await using var db = Db(); db.Shipments.Remove(await db.Shipments.SingleAsync()); await db.SaveChangesAsync();
        var result = await Service(db).CreateShipmentWithCarrierAsync(tenant, orderId, "fedex", "untrusted-service");
        Assert.Equal("PARCEL_MEASUREMENTS_REQUIRED", result.ErrorCode); Assert.Equal(0, handler.Calls);
        var s = await db.Shipments.AsNoTracking().SingleAsync(); Assert.Equal(ShipmentStatus.PendingExternalRegistration, s.Status);
        Assert.Equal("fedex_ground", s.ServiceLevel); Assert.Equal(0, s.ExternalRegistrationAttemptCount);
        Assert.Null(s.CarrierRequestSnapshotJson); Assert.Null(s.ParcelEvidenceJson);
    }

    [Fact]
    public async Task GeographicAdmission_PreservesExplicitWarehousePostalSource()
    {
        AddressAdmissionEvidence? admitted = null;
        var repository = new Mock<IGeographicRepository>();
        await using var db = Db(); repository.SetupGet(r => r.Places).Returns(db.Set<GeographicPlace>());
        var profile = await db.GeographicCountryProfiles.SingleAsync();
        profile.PostalCodePolicy = GeographicPostalCodePolicy.Required; profile.PostalCodeRegex = @"\A[0-9]{5}(-[0-9]{4})?\z";
        db.GeographicPlaceHierarchyEdges.AddRange(
            new GeographicPlaceHierarchyEdge { Id = Guid.NewGuid(), ParentPlaceId = country, ChildPlaceId = state, CountryPlaceId = country, IntroducedCatalogReleaseId = catalog },
            new GeographicPlaceHierarchyEdge { Id = Guid.NewGuid(), ParentPlaceId = state, ChildPlaceId = locality, CountryPlaceId = country, IntroducedCatalogReleaseId = catalog });
        await db.SaveChangesAsync();
        repository.SetupGet(r => r.CountryProfiles).Returns(db.GeographicCountryProfiles);
        repository.SetupGet(r => r.HierarchyEdges).Returns(db.GeographicPlaceHierarchyEdges);
        repository.Setup(r => r.AddAddressAdmissionEvidenceAsync(It.IsAny<AddressAdmissionEvidence>(), It.IsAny<CancellationToken>()))
            .Callback<AddressAdmissionEvidence, CancellationToken>((e, _) => admitted = e).Returns(Task.CompletedTask);
        var catalogService = new Mock<IGeographicCatalogService>();
        catalogService.Setup(c => c.GetActivePublishedCatalogReleaseAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(await db.Set<GeographicCatalogRelease>().SingleAsync());
        var result = await new GeographicResolver(catalogService.Object, repository.Object).ValidatePublishedLineageAsync(new GeographicAddressCandidate(
            tenant, AddressAdmissionOwnerDomain.Warehouse, warehouseId, new(country, state, locality, null, null), new string('B', 64), " 38116 ", null, null, null));
        Assert.True(result.Succeeded); Assert.Equal("38116", admitted!.PostalCode); Assert.Equal(warehouseId, admitted.OwnerId); Assert.Equal(tenant, admitted.TenantId);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public string? Failure;
        public Func<HttpRequestMessage, Task>? BeforeSend;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("/ship/v1/shipments", request.RequestUri!.AbsolutePath);
            if (BeforeSend != null) await BeforeSend(request);
            if (Failure == "timeout") throw new TaskCanceledException("Synthetic lost reply");
            return new(HttpStatusCode.OK) { Content = new StringContent(Failure == "response-admission" ? "{}" : FedExEndpointRuntimeIntegrationTests.ValidShipmentJson()) };
        }
    }
    public void Dispose() => telemetry.Dispose();
}
