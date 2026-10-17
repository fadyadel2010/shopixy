using System.Security.Cryptography;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

// Offline relational orchestration only, sharing the migrated parcel fixture.
// Synthetic adapters/accounts are not contract, catalog or entitlement authority.
public sealed partial class DhlParcelEvidencePostgresTests
{
    private CarrierShipmentService BoundCarrier(ApplicationDbContext db, string code, Mock<IShippingCarrierAdapter> adapter,
        CarrierCredentialsResolution resolution, ICarrierCredentialEncryptionService encryption)
    {
        var registry = new Mock<IShippingCarrierRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetAdapter(code)).Returns(adapter.Object);
        var binding = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        binding.Setup(a => a.ResolveCredentialsAsync(tenant, code, It.IsAny<CarrierIntegrationPurpose>(), It.IsAny<CancellationToken>())).ReturnsAsync(resolution);
        binding.Setup(a => a.GetServiceLevelReadinessAsync(tenant, code, "standard", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierServiceLevelReadiness(code, "standard", true, true, true, true, []));
        return new(db, registry.Object, encryption, events.Object, fulfillment.Object, telemetry, new TestTenantContext(tenant),
            Mock.Of<ICustomsEvidenceAdmissionService>(MockBehavior.Strict), Mock.Of<ICarrierGeographicMappingService>(MockBehavior.Strict),
            NullLogger<CarrierShipmentService>.Instance, binding.Object);
    }

    private CarrierCredentialsResolution BoundResolution(string code) => new(true, code,
        new CarrierCredentials { ApiKey = "synthetic-key", ApiSecret = "synthetic-secret", AccountId = "synthetic-original-account" },
        JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>($$"""{"Id":"{{config}}","CarrierCode":"{{code}}","DisplayName":"Synthetic offline binding","IsEnabled":true,"SupportsTracking":true}""")!, []);

    private static ICarrierCredentialEncryptionService BindingEncryption() => new CarrierCredentialEncryptionService(
        new EphemeralDataProtectionProvider(), NullLogger<CarrierCredentialEncryptionService>.Instance);

    private static CreateShipmentResult BoundCreated() => new() { Success = true, ExternalReferenceId = "SYNTHETIC-REMOTE-ONLY",
        TrackingNumber = "SYNTHETIC-TRACK-ONLY", InitialStatus = ShipmentStatus.ReadyForPickup };

    private ParcelMeasurement BoundInput(string code) => code == "bosta"
        ? Input with { Goods = new("Synthetic fixture goods", 75m, "EGP") } : Input;

    [Theory]
    [InlineData("bosta")][InlineData("aramex")]
    public async Task BoundPostgres_IntentWriteFailureRollsBackAttemptWithoutAdapterCall(string code)
    {
        var s = await Seed(carrier: code); await using var save = scope.CreateDbContext(tenant);
        await Evidence(save).SaveAsync(tenant, s.Id, actor, BoundInput(code));
        var adapter = new Mock<IShippingCarrierAdapter>(MockBehavior.Strict);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(save.Database.GetConnectionString())
            .AddInterceptors(new RejectIntentWrite()).Options;
        await using var db = new ApplicationDbContext(options, new TestTenantContext(tenant));
        var result = await BoundCarrier(db, code, adapter, BoundResolution(code), BindingEncryption())
            .ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "standard");
        Assert.False(result.Success);
        await using var verify = scope.CreateDbContext(tenant);
        var retained = await verify.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(0, retained.ExternalRegistrationAttemptCount); Assert.Null(retained.ExternalRegistrationLastAttemptAtUtc);
        Assert.Null(retained.CarrierRequestSnapshotJson); Assert.Null(retained.CarrierRequestFingerprint);
        adapter.VerifyNoOtherCalls();
    }

    private sealed class RejectIntentWrite : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"Shipments\" SET \"CarrierRequestSnapshotJson\"", StringComparison.Ordinal))
                throw new InvalidOperationException("Synthetic atomic-intent write failure");
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData("bosta")][InlineData("aramex")]
    public async Task BoundPostgres_AccountChangeDuringTrackingObservationRejectsPersistence(string code)
    {
        var s = await Seed(carrier: code); await using var save = scope.CreateDbContext(tenant);
        await Evidence(save).SaveAsync(tenant, s.Id, actor, BoundInput(code));
        var resolution = BoundResolution(code); var encryption = BindingEncryption();
        var adapter = new Mock<IShippingCarrierAdapter>(MockBehavior.Strict);
        adapter.Setup(a => a.CreateShipmentAsync(It.IsAny<CreateShipmentRequest>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>())).ReturnsAsync(BoundCreated());
        adapter.Setup(a => a.GetTrackingAsync("SYNTHETIC-TRACK-ONLY", It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .Returns(() => { resolution.Credentials!.ApiSecret = "synthetic-changed-during-observation";
                return Task.FromResult(new ShipmentTrackingResult { Success = true, CurrentStatus = ShipmentStatus.OutForDelivery }); });
        await using var db = scope.CreateDbContext(tenant);
        var carrier = BoundCarrier(db, code, adapter, resolution, encryption);
        Assert.True((await carrier.ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "standard")).Success);
        var previousSync = (await db.Shipments.AsNoTracking().SingleAsync()).LastCarrierSyncAtUtc;
        db.ChangeTracker.Clear();
        var tracking = await carrier.SyncTrackingAsync(tenant, s.Id);
        Assert.False(tracking.Success); Assert.Equal("CARRIER_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED", tracking.ErrorCode);
        var retained = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentStatus.ReadyForPickup, retained.Status); Assert.Equal(previousSync, retained.LastCarrierSyncAtUtc); Assert.False(retained.CodCollected);
        adapter.Verify(a => a.GetTrackingAsync("SYNTHETIC-TRACK-ONLY", It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("bosta")][InlineData("aramex")]
    public async Task BoundPostgres_IntentAndAttemptCommitBeforeAdapter_RequestAndParcelRejectSqlTampering(string code)
    {
        var s = await Seed(carrier: code); await using var save = scope.CreateDbContext(tenant);
        await Evidence(save).SaveAsync(tenant, s.Id, actor, BoundInput(code));
        var resolution = BoundResolution(code); var encryption = BindingEncryption();
        var adapter = new Mock<IShippingCarrierAdapter>(MockBehavior.Strict);
        adapter.Setup(a => a.CreateShipmentAsync(It.IsAny<CreateShipmentRequest>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .Returns(async (CreateShipmentRequest request, CarrierCredentials credentials, CancellationToken _) =>
            {
                await using var inspect = scope.CreateDbContext(tenant);
                var retained = await inspect.Shipments.AsNoTracking().SingleAsync();
                Assert.Null(inspect.Database.CurrentTransaction);
                Assert.Equal(1, retained.ExternalRegistrationAttemptCount); Assert.NotNull(retained.ExternalRegistrationLastAttemptAtUtc);
                Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(retained.CarrierRequestSnapshotJson!))), retained.CarrierRequestFingerprint);
                Assert.DoesNotContain("synthetic-secret", retained.CarrierRequestSnapshotJson);
                Assert.DoesNotContain("synthetic-original-account", retained.CarrierRequestSnapshotJson);
                Assert.Equal(1.75m, request.WeightKg); Assert.Equal(23.1m, request.LengthCm);
                Assert.Equal(retained.CarrierRequestId, request.CarrierRequestId);
                return BoundCreated();
            });
        await using var db = scope.CreateDbContext(tenant);
        var carrier = BoundCarrier(db, code, adapter, resolution, encryption);
        var created = await carrier.ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "untrusted-service");
        Assert.True(created.Success, created.ErrorCode);
        var original = (await db.Shipments.AsNoTracking().SingleAsync()).CarrierRequestSnapshotJson;
        foreach (var column in new[] { "CarrierRequestSnapshotJson", "CarrierRequestFingerprint", "ParcelEvidenceJson" })
        {
            var sql = $"UPDATE \"Shipments\" SET \"{column}\"='substitution' WHERE \"Id\"=@id";
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, new NpgsqlParameter("id", s.Id)));
            Assert.Equal("23514", error.SqlState);
        }
        var replay = await carrier.ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "standard");
        Assert.True(replay.Success, replay.ErrorCode);
        resolution.Credentials!.ApiSecret = "synthetic-substituted-secret";
        db.ChangeTracker.Clear();
        var changed = await carrier.SyncTrackingAsync(tenant, s.Id);
        Assert.False(changed.Success); Assert.Equal("CARRIER_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED", changed.ErrorCode);
        Assert.Equal(original, (await db.Shipments.AsNoTracking().SingleAsync()).CarrierRequestSnapshotJson);
        adapter.Verify(a => a.CreateShipmentAsync(It.IsAny<CreateShipmentRequest>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()), Times.Once);
        adapter.VerifyNoOtherCalls();
        Assert.Empty(await db.JournalEntries.ToListAsync()); Assert.Empty(await db.AccountingOutboxMessages.ToListAsync());
    }

    [Theory]
    [InlineData("bosta")][InlineData("aramex")]
    public async Task BoundPostgres_ConcurrentClaimOwnsExactlyOneFrozenIntentAndCall(string code)
    {
        var s = await Seed(carrier: code); await using var save = scope.CreateDbContext(tenant);
        await Evidence(save).SaveAsync(tenant, s.Id, actor, BoundInput(code));
        var resolution = BoundResolution(code); var encryption = BindingEncryption();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new Mock<IShippingCarrierAdapter>(MockBehavior.Strict);
        adapter.Setup(a => a.CreateShipmentAsync(It.IsAny<CreateShipmentRequest>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .Returns(async () => { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20)); return BoundCreated(); });
        await using var firstDb = scope.CreateDbContext(tenant);
        var first = BoundCarrier(firstDb, code, adapter, resolution, encryption).ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "standard");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var secondDb = scope.CreateDbContext(tenant);
            var second = await BoundCarrier(secondDb, code, adapter, resolution, encryption).ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "standard");
            Assert.False(second.Success);
        }
        finally { release.TrySetResult(); }
        Assert.True((await first).Success);
        await using var inspect = scope.CreateDbContext(tenant);
        var retained = await inspect.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(1, retained.ExternalRegistrationAttemptCount); Assert.NotNull(retained.CarrierRequestSnapshotJson);
        adapter.Verify(a => a.CreateShipmentAsync(It.IsAny<CreateShipmentRequest>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("bosta")][InlineData("aramex")]
    public async Task BoundPostgres_UncertainCreateAndNullLookupNeverAuthorizeRepostOrAccountReplacement(string code)
    {
        var s = await Seed(carrier: code); await using var save = scope.CreateDbContext(tenant);
        await Evidence(save).SaveAsync(tenant, s.Id, actor, BoundInput(code));
        var resolution = BoundResolution(code); var encryption = BindingEncryption();
        var adapter = new Mock<IShippingCarrierAdapter>(MockBehavior.Strict);
        adapter.Setup(a => a.CreateShipmentAsync(It.IsAny<CreateShipmentRequest>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateShipmentResult { Success = false, ErrorCode = "HTTP_ERROR", ErrorMessage = "Synthetic uncertain response" });
        adapter.Setup(a => a.GetShipmentByCarrierRequestIdAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateShipmentResult?)null);
        await using var db = scope.CreateDbContext(tenant);
        var carrier = BoundCarrier(db, code, adapter, resolution, encryption);
        Assert.False((await carrier.ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "standard")).Success);
        var original = (await db.Shipments.AsNoTracking().SingleAsync()).CarrierRequestSnapshotJson;
        Assert.False((await carrier.ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "standard")).Success);
        resolution.Credentials!.AccountId = "synthetic-replacement-account";
        var replaced = await carrier.ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "standard");
        Assert.Equal("CARRIER_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED", replaced.ErrorCode);
        var retained = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(1, retained.ExternalRegistrationAttemptCount); Assert.Equal(original, retained.CarrierRequestSnapshotJson);
        Assert.Null(retained.ExternalReferenceId); Assert.False(retained.CodCollected);
        adapter.Verify(a => a.CreateShipmentAsync(It.IsAny<CreateShipmentRequest>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()), Times.Once);
        adapter.Verify(a => a.GetShipmentByCarrierRequestIdAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()), Times.Once);
        adapter.VerifyNoOtherCalls();
    }
}
