using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

public sealed partial class DhlParcelEvidencePostgresTests : IDisposable
{
    private readonly TestDatabaseScope scope = ShippingIsolatedPostgres.OpenManagedTestScope();
    private readonly Guid tenant = Guid.NewGuid();
    private readonly Guid actor = Guid.NewGuid();
    private readonly Guid config = Guid.NewGuid();
    private readonly CommerceReliabilityTelemetry telemetry = new();
    private readonly Mock<ICarrierIntegrationAuthority> authority = new();
    private readonly Mock<IOrderFulfillmentService> fulfillment = new();
    private readonly Mock<IEventPublisher> events = new();
    private readonly Mock<ICustomsEvidenceAdmissionService> customs = new();
    private string account = "mock-account";
    private bool sandbox;
    private ParcelMeasurement Input => new(231, 145, 89, "mm", 1750, "g", 1, "PACKED-1");

    private CarrierCredentialsResolution Resolution() => new(true, "dhl", new CarrierCredentials { ApiKey = "mock", ApiSecret = "mock", AccountId = account, UseSandbox = sandbox },
        JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>($"{{\"Id\":\"{config}\",\"CarrierCode\":\"dhl\",\"DisplayName\":\"Mock DHL\"}}")!, []);
    public DhlParcelEvidencePostgresTests()
    {
        // Mocked admission only. Production LocalOnly readiness remains blocked.
        authority.Setup(x => x.ResolveCredentialsAsync(tenant, "dhl", It.IsAny<CarrierIntegrationPurpose>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Resolution());
        authority.Setup(x => x.GetServiceLevelReadinessAsync(tenant, "dhl", "express", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierServiceLevelReadiness("dhl", "express", true, true, true, true, []));
        events.SetReturnsDefault(Task.CompletedTask);
        fulfillment.Setup(x => x.SynchronizeFromShipmentAsync(It.IsAny<Guid>(), It.IsAny<FulfillmentStatus>(), It.IsAny<FulfillmentRequestSource>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, FulfillmentStatus status, FulfillmentRequestSource source, Guid? s, string? t, string? a, string? k, CancellationToken ct) => OrderFulfillmentResult.Succeeded(id, "FIXTURE", status, status, source, k));
    }
    private ShipmentParcelEvidenceService Evidence(ApplicationDbContext db) => new(db, new TestTenantContext(tenant));
    private CarrierShipmentService Carrier(ApplicationDbContext db, Handler handler)
    {
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(x => x.CreateClient("DHL")).Returns(new HttpClient(handler));
        var registry = new Mock<IShippingCarrierRegistry>(); registry.Setup(x => x.GetAdapter("dhl")).Returns(new DhlAdapter(factory.Object, NullLogger<DhlAdapter>.Instance));
        var encryption = new CarrierCredentialEncryptionService(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(), NullLogger<CarrierCredentialEncryptionService>.Instance);
        return new(db, registry.Object, encryption, events.Object, fulfillment.Object, telemetry,
            new TestTenantContext(tenant), customs.Object, Mock.Of<ICarrierGeographicMappingService>(), NullLogger<CarrierShipmentService>.Instance, authority.Object);
    }
    private async Task<Shipment> Seed(string destination = "EG", string carrier = "dhl", bool cod = false)
    {
        await using var db = scope.CreateDbContext(tenant);
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Isolated DHL parcel", Slug = $"parcel-{tenant:N}", IsActive = true, Status = TenantStatus.Active });
        db.Users.Add(new User { Id = actor, TenantId = tenant, Username = "synthetic-operator", Email = "operator@example.invalid", PasswordHash = "mock", Role = UserRole.Warehouse, IsActive = true });
        var category = new Category { Id = Guid.NewGuid(), TenantId = tenant, Name = "Fixture" }; db.Categories.Add(category);
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenant, Name = "Fixture product", CategoryId = category.Id }; db.Products.Add(product);
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = "PARCEL-ORDER", Status = OrderStatus.Confirmed, Currency = "EGP", Total = 100,
            PaymentMethodCode = cod ? "COD" : "PREPAID", PaymentStatus = cod ? PaymentStatus.Pending : PaymentStatus.Paid,
            FinancialPostingStatus = FinancialPostingStatus.NotRequired, FulfillmentMode = carrier == "manual" ? FulfillmentMode.Manual : FulfillmentMode.Carrier,
            FulfillmentStatus = FulfillmentStatus.Confirmed, FulfillmentCarrierCode = carrier, ShippingAddress = "Synthetic destination", ShippingCountry = "EG",
            Items = [new OrderItem { Id = Guid.NewGuid(), ProductId = product.Id, Quantity = 2, UnitPrice = 50, Total = 100 }] }; db.Orders.Add(order);
        var origin = new ShipmentOriginDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, null, null,
            "EG", "Egypt", "Cairo", "Cairo", null, null, "Synthetic sender", "02000000000", "Synthetic origin", DateTime.UtcNow, 1);
        var mapping = Guid.NewGuid(); var country = Guid.NewGuid();
        db.Set<GeographicCatalogRelease>().Add(new GeographicCatalogRelease { Id = origin.CatalogReleaseId, ReleaseKey = "synthetic-catalog", ReleaseNumber = 1, CreatedByUserId = actor, ChecksumSha256 = new string('A', 64), CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var s = new Shipment { Id = Guid.NewGuid(), TenantId = tenant, OrderId = order.Id, CarrierCode = carrier, ServiceLevel = carrier == "dhl" ? "express" : "standard", ShipmentNumber = "SYNTHETIC-PARCEL",
            Status = ShipmentStatus.PendingExternalRegistration, Currency = "EGP", WeightKg = 99, RecipientName = "Synthetic receiver", RecipientPhone = "01000000000", ShippingAddress = "Synthetic destination",
            ShippingCountry = destination, DestinationStreet = "Synthetic destination", DestinationLocalityDisplayName = "Giza", DestinationCountryPlaceId = country,
            DestinationAddressAdmissionEvidenceId = Guid.NewGuid(), DestinationGeographicCatalogReleaseId = origin.CatalogReleaseId, DestinationGeographicSnapshotVersion = 1, DestinationGeographicSnapshotCreatedAtUtc = DateTime.UtcNow,
            OriginWarehouseId = origin.WarehouseId, OriginAddressAdmissionEvidenceId = origin.AddressAdmissionEvidenceId, OriginGeographicCatalogReleaseId = origin.CatalogReleaseId,
            OriginCountryPlaceId = origin.CountryPlaceId, OriginCountryIso2 = "EG", OriginSnapshotJson = JsonSerializer.Serialize(origin),
            OriginCountryDisplayName = "Egypt", OriginContactName = origin.ContactName, OriginContactPhone = origin.ContactPhone, OriginStreet = origin.Street,
            OriginGeographicSnapshotVersion = 1, OriginGeographicSnapshotCreatedAtUtc = DateTime.UtcNow,
            CarrierGeographicMappingReleaseId = mapping, CarrierGeographicMappingVersion = 1,
            CarrierGeographicMappingReleaseKey = "synthetic-release", CarrierGeographicEnvironment = "production", CarrierGeographicAccountScope = "Tenant", CarrierGeographicResolvedAtUtc = DateTime.UtcNow,
            CarrierGeographicMappingSnapshotJson = JsonSerializer.Serialize(new { CarrierCode = carrier, MappingReleaseId = mapping, MappingVersion = 1, CountryPlaceId = country, CountryIso2 = destination,
                Items = new[] { new ResolvedProviderGeography("City", "mock-city", null, "Giza", null) } }) };
        db.Shipments.Add(s); await db.SaveChangesAsync(); return s;
    }

    [Fact]
    public async Task FullMigrationChain_AndMeasurementSave_ReadReplay_UsesActualPackedWeight()
    {
        var s = await Seed(); await using var db = scope.CreateDbContext(tenant);
        Assert.Contains("20261011121839_ShipmentParcelRequestEvidence", await db.Database.GetAppliedMigrationsAsync());
        var e = await Evidence(db).SaveAsync(tenant, s.Id, actor, Input);
        Assert.Equal(1.75m, e.GrossWeightKg); Assert.Equal(23.1m, e.LengthCm);
        var again = await Evidence(db).SaveAsync(tenant, s.Id, actor, Input);
        Assert.Equal(e.RecordedAtUtc, again.RecordedAtUtc);
        Assert.Equal(99, (await db.Shipments.SingleAsync()).WeightKg);
        Assert.Equal(e.Measurement, (await Evidence(db).ReadAsync(tenant, s.Id)).Evidence!.Measurement);
        Assert.Empty(await db.JournalEntries.ToListAsync()); Assert.Empty(await db.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task ForeignTenant_CannotReadOrMutateMeasurements()
    {
        var s = await Seed(); await using var db = scope.CreateDbContext(Guid.NewGuid());
        var foreign = new ShipmentParcelEvidenceService(db, new TestTenantContext(Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.ReadAsync(tenant, s.Id));
        await using var same = scope.CreateDbContext(tenant);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Evidence(same).ReadAsync(tenant, Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.SaveAsync(tenant, s.Id, actor, Input));
    }

    [Theory]
    [InlineData("inactive")][InlineData("role")][InlineData("foreign")]
    public async Task DhlMeasurement_RequiresActiveOwnedFulfillmentActor(string scenario)
    {
        var s = await Seed(); await using var db = scope.CreateDbContext(tenant);
        var user = await db.Users.SingleAsync();
        if (scenario == "inactive") user.IsActive = false;
        if (scenario == "role") user.Role = UserRole.Cashier;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Evidence(db).SaveAsync(tenant, s.Id, scenario == "foreign" ? Guid.NewGuid() : actor, Input));
        Assert.Null((await db.Shipments.AsNoTracking().SingleAsync()).ParcelEvidenceJson);
    }

    [Theory]
    [InlineData("inactive")][InlineData("role")][InlineData("service")]
    public async Task DhlFreshQuoteAndCreation_DoNotBorrowFormerMeasurementAuthority(string scenario)
    {
        var s = await Seed(); await using var db = scope.CreateDbContext(tenant);
        await Evidence(db).SaveAsync(tenant, s.Id, actor, Input);
        var user = await db.Users.SingleAsync();
        if (scenario == "inactive") user.IsActive = false;
        if (scenario == "role") user.Role = UserRole.Cashier;
        if (scenario == "service") authority.Setup(a => a.GetServiceLevelReadinessAsync(tenant, "dhl", "express", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierServiceLevelReadiness("dhl", "express", true, true, false, false, ["SERVICE_LEVEL_DISABLED"]));
        await db.SaveChangesAsync();
        var handler = new Handler(); var service = Carrier(db, handler);
        var quote = await service.QuoteExistingShipmentAsync(tenant, s.Id);
        Assert.Equal(scenario == "service" ? "SERVICE_LEVEL_DISABLED" : "PARCEL_MEASURED_ACTOR_NOT_OWNED", quote.ErrorCode);
        var create = await service.ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "express");
        Assert.False(create.Success); Assert.Equal(0, handler.Calls);
        Assert.Equal(0, (await db.Shipments.AsNoTracking().SingleAsync()).ExternalRegistrationAttemptCount);
    }

    [Fact]
    public async Task MissingEvidence_DoesNotClaimOrSend()
    {
        var s = await Seed(); await using var db = scope.CreateDbContext(tenant); var h = new Handler();
        var result = await Carrier(db, h).ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "express");
        Assert.Equal("PARCEL_MEASUREMENTS_REQUIRED", result.ErrorCode); Assert.Equal(0, h.Calls);
        Assert.Equal(0, (await db.Shipments.AsNoTracking().SingleAsync()).ExternalRegistrationAttemptCount);
    }

    [Fact]
    public async Task SavedParcel_IsImmutable_InServiceAndSql()
    {
        var s = await Seed(); await using var db = scope.CreateDbContext(tenant); await Evidence(db).SaveAsync(tenant, s.Id, actor, Input);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Evidence(db).SaveAsync(tenant, s.Id, actor, Input with { GrossWeight = 2000 }));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Shipments\" SET \"ParcelEvidenceJson\"='{{}}' WHERE \"Id\"={s.Id}"));
    }

    [Fact]
    public async Task QuoteAndCreate_UseSameMeasurements_IntentCommittedBeforeHttp_ValidLabelAdmission()
    {
        var s = await Seed(); await using var save = scope.CreateDbContext(tenant); await Evidence(save).SaveAsync(tenant, s.Id, actor, Input);
        await using var db = scope.CreateDbContext(tenant); var h = new Handler();
        h.BeforeSend = async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/rates")) return;
            await using var inspect = scope.CreateDbContext(tenant); var persisted = await inspect.Shipments.AsNoTracking().SingleAsync();
            Assert.NotNull(persisted.CarrierRequestSnapshotJson); Assert.Equal(64, persisted.CarrierRequestFingerprint!.Length); Assert.Equal(1, persisted.ExternalRegistrationAttemptCount);
            Assert.Contains("PACKED-1", persisted.CarrierRequestSnapshotJson);
        };
        var quote = await Carrier(db, h).QuoteExistingShipmentAsync(tenant, s.Id); Assert.True(quote.Success, quote.ErrorCode);
        Assert.Equal(123.45m, Assert.Single(quote.Quotes).Cost);
        var result = await Carrier(db, h).ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "caller-cannot-change-service");
        Assert.True(result.Success, result.ErrorCode + ":" + result.ErrorMessage); Assert.Equal(2, h.Calls);
        foreach (var body in h.Bodies) { using var json = JsonDocument.Parse(body); var root = json.RootElement; var package = root.TryGetProperty("content", out var content) ? content.GetProperty("packages")[0] : root.GetProperty("packages")[0];
            Assert.Equal(1.75m, package.GetProperty("weight").GetDecimal()); Assert.Equal(23.1m, package.GetProperty("dimensions").GetProperty("length").GetDecimal()); }
        var current = await db.Shipments.AsNoTracking().SingleAsync(); Assert.Equal("1234567890", current.TrackingNumber); Assert.Equal("PDF", current.LabelFormat);
        Assert.Empty(await db.PaymentIntents.ToListAsync()); Assert.Empty(await db.JournalEntries.ToListAsync());
    }

    [Theory]
    [InlineData("timeout")] [InlineData("label")] [InlineData("correlation")]
    public async Task AmbiguousCreate_ReplayNeverDuplicatesPost(string failure)
    {
        var s = await Seed(); await using var save = scope.CreateDbContext(tenant); await Evidence(save).SaveAsync(tenant, s.Id, actor, Input);
        var h = new Handler { Failure = failure };
        await using (var db = scope.CreateDbContext(tenant)) { var result = await Carrier(db, h).ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "express"); Assert.False(result.Success); }
        await using var replayDb = scope.CreateDbContext(tenant); var replay = await Carrier(replayDb, h).ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "express");
        Assert.False(replay.Success); Assert.Equal(1, h.Calls); var stored = await replayDb.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(1, stored.ExternalRegistrationAttemptCount); Assert.Null(stored.ExternalReferenceId); Assert.Equal("ReconciliationRequired", stored.ExternalRegistrationState);
    }

    [Theory]
    [InlineData("account")] [InlineData("environment")] [InlineData("destination")] [InlineData("currency")]
    public async Task ReplayChangedIdentity_IsRejectedWithoutNewHttp(string change)
    {
        var s = await Seed(); await using var save = scope.CreateDbContext(tenant); await Evidence(save).SaveAsync(tenant, s.Id, actor, Input);
        var h = new Handler { Failure = "timeout" }; await using (var db = scope.CreateDbContext(tenant)) await Carrier(db, h).ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "express");
        if (change == "account") account = "different"; if (change == "environment") sandbox = true;
        await using var replayDb = scope.CreateDbContext(tenant);
        if (change == "destination") { var ship = await replayDb.Shipments.SingleAsync(); ship.ShippingZipCode = "different"; await replayDb.SaveChangesAsync(); }
        if (change == "currency")
        {
            var order = await replayDb.Orders.SingleAsync(); order.Currency = "USD";
            await Assert.ThrowsAsync<InvalidOperationException>(() => replayDb.SaveChangesAsync());
            replayDb.ChangeTracker.Clear();
            Assert.Equal("EGP", (await replayDb.Orders.AsNoTracking().SingleAsync()).Currency);
        }
        var result = await Carrier(replayDb, h).ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "express");
        Assert.Equal(change == "currency" ? "EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED" : "DHL_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED", result.ErrorCode); Assert.Equal(1, h.Calls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task International_CustomsUsesMeasuredGrossWeight_AndUnprovenValueMeaningStaysBlocked(bool published)
    {
        var s = await Seed("US"); await using var save = scope.CreateDbContext(tenant); await Evidence(save).SaveAsync(tenant, s.Id, actor, Input);
        customs.Setup(x => x.CreateOrReuseShipmentSnapshotAsync(It.IsAny<ShipmentCustomsSnapshotRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShipmentCustomsSnapshotRequest req, CancellationToken ct) =>
            {
                Assert.Equal(1.75m, req.ShipmentGrossWeight); Assert.Equal("kg", req.ShipmentGrossWeightUnit); Assert.Equal("EG", req.OriginCountryIso2); Assert.Equal("US", req.DestinationCountryIso2);
                return published ? new ShipmentCustomsSnapshotResult(true, Guid.NewGuid(), new PointOfSale.Core.DTOs.Customs.CarrierNeutralCustomsDto(Guid.NewGuid(), s.Id, s.OrderId, 1, 1, "EG", "US", 1.75m, "kg", []), null, null)
                    : new ShipmentCustomsSnapshotResult(false, null, null, "CUSTOMS_EVIDENCE_NOT_ADMITTED", "Missing published evidence");
            });
        await using var db = scope.CreateDbContext(tenant); var h = new Handler(); var result = await Carrier(db, h).ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "express");
        Assert.Equal(published ? "DHL_CUSTOMS_VALUE_SEMANTICS_UNPROVEN" : "CUSTOMS_EVIDENCE_NOT_ADMITTED", result.ErrorCode);
        Assert.Equal(0, h.Calls); Assert.Equal(0, (await db.Shipments.AsNoTracking().SingleAsync()).ExternalRegistrationAttemptCount);
    }

    [Fact]
    public async Task ConcurrentMeasurementWriters_OnlyOneImmutableParcelWins()
    {
        var s = await Seed();
        async Task<bool> Save(ParcelMeasurement input)
        {
            await using var db = scope.CreateDbContext(tenant);
            try { await Evidence(db).SaveAsync(tenant, s.Id, actor, input); return true; }
            catch (InvalidOperationException ex) when (ex.Message == "PARCEL_EVIDENCE_IMMUTABLE") { return false; }
        }
        var outcomes = await Task.WhenAll(Save(Input), Save(Input with { GrossWeight = 2000 }));
        Assert.Single(outcomes.Where(x => x)); await using var inspect = scope.CreateDbContext(tenant);
        Assert.NotNull((await Evidence(inspect).ReadAsync(tenant, s.Id)).Evidence);
    }

    [Fact]
    public async Task ConcurrentRegistration_OneFrozenIntentAndOneHttpPost()
    {
        var s = await Seed(); await using var save = scope.CreateDbContext(tenant); await Evidence(save).SaveAsync(tenant, s.Id, actor, Input);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var h = new Handler { BeforeSend = async _ => { entered.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20)); } };
        await using var firstDb = scope.CreateDbContext(tenant); var first = Carrier(firstDb, h).ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "express");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await using var secondDb = scope.CreateDbContext(tenant);
        var second = await Carrier(secondDb, h).ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "express");
        release.SetResult(); await first;
        Assert.False(second.Success); Assert.Equal(1, h.Calls);
        var stored = await secondDb.Shipments.AsNoTracking().SingleAsync(); Assert.Equal(1, stored.ExternalRegistrationAttemptCount);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => secondDb.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Shipments\" SET \"CarrierRequestFingerprint\"={new string('B', 64)} WHERE \"Id\"={s.Id}"));
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Calls; public List<string> Bodies = []; public string? Failure; public Func<HttpRequestMessage, Task>? BeforeSend;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Bodies.Add(await request.Content!.ReadAsStringAsync(ct)); if (BeforeSend != null) await BeforeSend(request);
            if (Failure == "timeout") throw new TaskCanceledException("synthetic ambiguous timeout");
            if (request.RequestUri!.AbsolutePath.EndsWith("/rates")) return new(HttpStatusCode.OK) { Content = new StringContent("""{"products":[{"productCode":"N","totalPrice":[{"currencyType":"BILLC","priceCurrency":"EGP","price":123.45}]}]}""") };
            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(Failure == "label" ? """{"shipmentTrackingNumber":"1234567890","packages":[{"trackingNumber":"JD0123456789"}]}""" : """{"shipmentTrackingNumber":"1234567890","packages":[{"trackingNumber":"JD0123456789"}],"documents":[{"typeCode":"label","imageFormat":"PDF","content":"JVBERi0xLjcKJSVFT0Y="}]}""") };
            if (Failure != "correlation") response.Headers.Add("Message-Reference", request.Headers.GetValues("Message-Reference").Single()); return response;
        }
    }
    public void Dispose() { telemetry.Dispose(); scope.Dispose(); }
}
