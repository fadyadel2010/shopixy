using System.Reflection;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.EventBus;
using PointOfSale.Infrastructure.Fulfillment;
using PointOfSale.Infrastructure.Jobs;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Tests.Infrastructure;
using Xunit.Abstractions;

namespace PointOfSale.Tests.Shipping;

/// <summary>Uniquely manifested nonfinancial rows; no fixture provisioning or real provider dispatch.</summary>
public sealed class ConfiguredShippingConcurrencyTests(ITestOutputHelper output)
{
    [ConfiguredSyntheticConcurrencyTheory]
    [InlineData("manual-duplicate")][InlineData("cancellation-first")]
    [InlineData("claim-first")][InlineData("competing")][InlineData("constraints-retention")][InlineData("durable-outbox")]
    [InlineData("delivery-outbox")][InlineData("delivery-rollback")][InlineData("delivery-recovery")]
    public async Task ActualConfiguredPostgres_LocksClaimsCancellationAndConstraints_ExactSyntheticCleanup(string scenario)
    {
        using var noApi = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var health = await Record.ExceptionAsync(() => noApi.GetAsync("http://localhost:5010/health/live"));
        Assert.NotNull(health); // Background workers must not discover these temporary intents.
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var cs = new NpgsqlConnectionStringBuilder(configured.Database.GetConnectionString());
        Assert.Equal("192.168.1.5", cs.Host); Assert.Equal("PosEcommerceDb_Baseline", cs.Database);
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var orderId = Guid.NewGuid(); var shipmentId = Guid.NewGuid();
        var delivery = scenario.StartsWith("delivery-", StringComparison.Ordinal);
        var method = Guid.NewGuid();
        var manual = delivery || scenario is "manual-duplicate" or "constraints-retention" or "durable-outbox";
        var evidence = Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!;
        await File.WriteAllTextAsync(Path.Combine(evidence, $"concurrency-{tenant:N}.json"), JsonSerializer.Serialize(new { tenant, actor, orderId, shipmentId, method, scenario }));
        ApplicationDbContext Db(Guid? scope = null) => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs.ConnectionString).Options, new TestTenantContext(scope ?? tenant));
        var events = new Mock<IEventPublisher>(); events.SetReturnsDefault(Task.CompletedTask);
        var registry = new Mock<IShippingCarrierRegistry>(MockBehavior.Strict);
        var integration = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        var encryption = new Mock<ICarrierCredentialEncryptionService>(MockBehavior.Strict);
        var endpoint = new Mock<IPublishEndpoint>(MockBehavior.Strict);
        async Task<ShipmentCancellationResult> Cancel()
        {
            await using var db = Db();
            var scope = new TestTenantContext(tenant);
            IEventPublisher publisher = scenario == "durable-outbox"
                ? new MassTransitEventPublisher(db, Mock.Of<IBus>(MockBehavior.Strict), endpoint.Object, NullLogger<MassTransitEventPublisher>.Instance, scope)
                : events.Object;
            return await new ShipmentCancellationService(db, registry.Object, integration.Object, publisher, scope)
                .CancelForAdminAsync(tenant, shipmentId, actor, Guid.NewGuid(), "Synthetic concurrency verification", "synthetic-configured-concurrency");
        }
        async Task<int> Claim()
        {
            await using var db = Db(); using var telemetry = new CommerceReliabilityTelemetry();
            var carrier = new CarrierShipmentService(db, registry.Object, encryption.Object, events.Object, Mock.Of<IOrderFulfillmentService>(),
                telemetry, new TestTenantContext(tenant), Mock.Of<ICustomsEvidenceAdmissionService>(), Mock.Of<ICarrierGeographicMappingService>(),
                NullLogger<CarrierShipmentService>.Instance, integration.Object);
            return await (Task<int>)typeof(CarrierShipmentService).GetMethod("ClaimShipmentForExternalCallAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(carrier, [shipmentId, tenant, 0, 1, DateTime.UtcNow, "standard", $"synthetic-{shipmentId:N}", CancellationToken.None])!;
        }
        try
        {
            await using (var db = Db())
            {
                db.Tenants.Add(new Tenant { Id = tenant, Name = "SYNTHETIC SHIPPING CONCURRENCY", Slug = $"shipping-{tenant:N}", Currency = "EGP", IsActive = true, Status = TenantStatus.Active });
                db.Users.Add(new User { Id = actor, TenantId = tenant, Username = $"shipping-{actor:N}", Email = $"{actor:N}@example.invalid", PasswordHash = "not-a-login", Role = UserRole.Warehouse, IsActive = true });
                if (delivery) db.ShippingMethods.Add(new ShippingMethod { Id = method, TenantId = tenant, Name = "SYNTHETIC inactive fixture method", IsActive = false });
                db.Orders.Add(new Order { Id = orderId, TenantId = tenant, OrderNumber = $"SYN-{orderId:N}", Total = 100, Currency = "EGP", PaymentMethodCode = "COD",
                    PaymentStatus = PaymentStatus.Pending, Status = delivery ? OrderStatus.Shipped : OrderStatus.Confirmed,
                    ShippingMethodId = delivery ? method : null, FulfillmentStatus = delivery ? FulfillmentStatus.OutForDelivery : FulfillmentStatus.Created,
                    FulfillmentMode = manual ? FulfillmentMode.Manual : FulfillmentMode.Carrier, FulfillmentCarrierCode = manual ? null : "bosta" });
                db.Shipments.Add(new Shipment { Id = shipmentId, TenantId = tenant, OrderId = orderId, ShipmentNumber = $"SYN-{shipmentId:N}", Currency = "EGP", CarrierCode = manual ? "manual" : "bosta",
                    Status = delivery ? ShipmentStatus.OutForDelivery : manual ? ShipmentStatus.Pending : ShipmentStatus.PendingExternalRegistration, ExternalRegistrationState = manual ? null : "PendingExternalCall",
                    ExternalRegistrationRequestedAtUtc = manual ? null : DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            if (delivery)
            {
                async Task Deliver()
                {
                    var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs.ConnectionString);
                    if (scenario == "delivery-rollback") options.AddInterceptors(new DeclineDeliveryCommit());
                    await using var db = new ApplicationDbContext(options.Options, new TestTenantContext(tenant));
                    var publisher = new MassTransitEventPublisher(db, Mock.Of<IBus>(MockBehavior.Strict), endpoint.Object, NullLogger<MassTransitEventPublisher>.Instance, new TestTenantContext(tenant));
                    var service = new OrderFulfillmentService(db, publisher, NullLogger<OrderFulfillmentService>.Instance);
                    Assert.True((await service.MarkDeliveredAsync(orderId, FulfillmentRequestSource.Manual, "SYNTHETIC", "https://example.invalid/proof")).Success);
                }
                if (scenario == "delivery-rollback") Assert.Equal("SYNTHETIC_DELIVERY_COMMIT_REFUSED", (await Assert.ThrowsAsync<InvalidOperationException>(Deliver)).Message);
                else { await Deliver(); await Deliver(); }
                await using var read = Db();
                Assert.Equal(scenario == "delivery-rollback" ? ShipmentStatus.OutForDelivery : ShipmentStatus.Delivered, (await read.Shipments.SingleAsync()).Status);
                var rows = await read.OutboxMessages.Where(m => m.TenantId == tenant).ToListAsync();
                Assert.Equal(scenario == "delivery-rollback" ? 0 : 3, rows.Count);
                Assert.All(rows, row => Assert.Null(row.ProcessedAtUtc));
                endpoint.VerifyNoOtherCalls();
                if (scenario == "delivery-recovery")
                {
                    // No global worker cycle/queue replay. Only this pre-written
                    // manifest of synthetic event IDs may enter the worker.
                    var ids = rows.OrderBy(r => r.EventVersion).ThenBy(r => r.CreatedAtUtc).ThenBy(r => r.Id).Select(r => r.Id).ToArray();
                    await File.WriteAllTextAsync(Path.Combine(evidence, $"delivery-replay-{tenant:N}.json"), JsonSerializer.Serialize(new { tenant, orderId, shipmentId, EventIds = ids }));
                    var broker = new Mock<IPublishEndpoint>(MockBehavior.Strict);
                    var attempts = 0;
                    broker.Setup(e => e.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<IPipe<PublishContext>>(), It.IsAny<CancellationToken>()))
                        .Returns(() => ++attempts == 1 ? Task.FromException(new InvalidOperationException("SYNTHETIC_TRANSPORT_REJECTION")) : Task.CompletedTask);
                    var services = new ServiceCollection(); services.AddScoped(_ => Db()); services.AddSingleton(broker.Object);
                    await using var provider = services.BuildServiceProvider();
                    var worker = new OutboxPublisherJob(provider, NullLogger<OutboxPublisherJob>.Instance);
                    var process = typeof(OutboxPublisherJob).GetMethod("ProcessMessageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    async Task Process(Guid id) { Assert.Contains(id, ids); await (Task)process.Invoke(worker, [id])!; }
                    await Process(ids[0]);
                    await using (var failed = Db())
                    {
                        var saved = await failed.OutboxMessages.SingleAsync(m => m.Id == ids[0] && m.TenantId == tenant);
                        Assert.Null(saved.ProcessedAtUtc); Assert.Equal(1, saved.RetryCount);
                        Assert.Contains("SYNTHETIC_TRANSPORT_REJECTION", saved.LastErrorMessage);
                    }
                    foreach (var id in ids) await Process(id);
                    var afterRecoveryAttempts = attempts;
                    foreach (var id in ids) await Process(id); // Processed identity never dispatches again.
                    Assert.Equal(afterRecoveryAttempts, attempts); Assert.Equal(4, attempts);
                    await using var recovered = Db();
                    Assert.All(await recovered.OutboxMessages.Where(m => m.TenantId == tenant).ToListAsync(), m => Assert.NotNull(m.ProcessedAtUtc));
                }
            }
            else if (scenario == "manual-duplicate")
            {
                var results = await Task.WhenAll(Cancel(), Cancel());
                Assert.All(results, r => { Assert.True(r.Confirmed); Assert.Equal("LocalOnly", r.ConfirmationBasis); });
                Assert.Equal(results[0].OperationId, results[1].OperationId);
                events.Verify(e => e.PublishAsync(It.IsAny<ShipmentCancelledEvent>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            else if (scenario == "durable-outbox")
            {
                Assert.True((await Cancel()).Confirmed);
                await using var observer = Db();
                Assert.Equal(ShipmentStatus.Cancelled, (await observer.Shipments.SingleAsync()).Status);
                var outbox = await observer.OutboxMessages.Where(m => m.TenantId == tenant).ToListAsync();
                Assert.Equal(2, outbox.Count); Assert.All(outbox, m => { Assert.Null(m.ProcessedAtUtc); Assert.Contains(m.AggregateId!.Value, new[] { orderId, shipmentId }); });
                Assert.Single(outbox.Where(m => m.EventType.StartsWith(typeof(ShipmentCancelledEvent).FullName!, StringComparison.Ordinal)));
                Assert.Single(outbox.Where(m => m.EventType.StartsWith(typeof(ShipmentStatusChangedEvent).FullName!, StringComparison.Ordinal)));
                endpoint.VerifyNoOtherCalls(); // A consume-scoped endpoint cannot escape the owned SQL transaction.
            }
            else if (scenario == "cancellation-first")
            { Assert.Equal("NoExternalShipment", (await Cancel()).ConfirmationBasis); Assert.Equal(0, await Claim()); }
            else if (scenario == "claim-first")
            { Assert.Equal(1, await Claim()); Assert.Equal("ReconciliationRequired", (await Cancel()).Outcome); }
            else if (scenario == "competing")
            {
                var claim = Claim(); var cancel = Cancel(); await Task.WhenAll(claim, cancel);
                Assert.True(claim.Result == 0 && cancel.Result.Confirmed || claim.Result == 1 && !cancel.Result.Confirmed);
                Assert.Equal(0, await Claim());
            }
            else
            {
                await using var db = Db();
                // Exact synthetic raw writes, each refusal isolated by its own savepoint/rollback.
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Shipments\" SET \"ParcelEvidenceJson\"='synthetic-original-parcel', \"CarrierRequestSnapshotJson\"='synthetic-original-request', \"CarrierRequestFingerprint\"={new string('A', 64)} WHERE \"Id\"={shipmentId} AND \"TenantId\"={tenant}");
                await tx.CreateSavepointAsync("mutation");
                var refused = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Shipments\" SET \"CarrierRequestSnapshotJson\"=NULL WHERE \"Id\"={shipmentId} AND \"TenantId\"={tenant}"));
                Assert.Equal("23514", refused.SqlState); await tx.RollbackToSavepointAsync("mutation");
                await tx.CreateSavepointAsync("foreignkey");
                refused = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Shipments\" SET \"OrderId\"={Guid.NewGuid()} WHERE \"Id\"={shipmentId} AND \"TenantId\"={tenant}"));
                Assert.Equal("23503", refused.SqlState); await tx.RollbackToSavepointAsync("foreignkey");
                Assert.Equal("synthetic-original-request", await db.Shipments.Where(s => s.Id == shipmentId).Select(s => s.CarrierRequestSnapshotJson).SingleAsync());
                await tx.RollbackAsync(); // First-capture evidence was never committed.
            }
            await using (var fresh = Db())
            {
                Assert.Equal(PaymentStatus.Pending, (await fresh.Orders.SingleAsync()).PaymentStatus);
                Assert.Empty(await fresh.JournalEntries.Where(j => j.TenantId == tenant).ToListAsync()); Assert.Empty(await fresh.AccountingOutboxMessages.Where(m => m.TenantId == tenant).ToListAsync());
                Assert.Empty(await fresh.StockMovements.Where(m => m.TenantId == tenant).ToListAsync());
                Assert.Equal(delivery || scenario == "constraints-retention" ? 0 : 1, await fresh.ShipmentCancellationOperations.CountAsync());
                await using var foreign = Db(Guid.NewGuid()); Assert.Empty(await foreign.Orders.ToListAsync()); Assert.Empty(await foreign.Shipments.ToListAsync());
            }
            registry.VerifyNoOtherCalls(); integration.VerifyNoOtherCalls(); encryption.VerifyNoOtherCalls();
        }
        finally
        {
            await using var cleanup = Db(); await using var tx = await cleanup.Database.BeginTransactionAsync();
            // Manifested identities and ownership on every delete. No carrier-money,
            // canonical retained admission, inventory or financial data was introduced.
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"ShipmentCancellationOperations\" WHERE \"TenantId\"={tenant} AND \"ShipmentId\"={shipmentId} AND \"OrderId\"={orderId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"OutboxMessages\" WHERE \"TenantId\"={tenant} AND (\"AggregateId\"={orderId} OR \"AggregateId\"={shipmentId})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Shipments\" WHERE \"Id\"={shipmentId} AND \"TenantId\"={tenant} AND \"OrderId\"={orderId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Orders\" WHERE \"Id\"={orderId} AND \"TenantId\"={tenant}");
            if (delivery) await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"ShippingMethods\" WHERE \"Id\"={method} AND \"TenantId\"={tenant} AND NOT \"IsActive\"");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"CanonicalIdentityLinks\" WHERE (\"SubjectId\"={actor} AND \"SubjectType\"=0 AND \"TenantId\"={tenant}) OR (\"SubjectId\"={tenant} AND \"SubjectType\"=6 AND \"TenantId\"={tenant})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Users\" WHERE \"Id\"={actor} AND \"TenantId\"={tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Tenants\" WHERE \"Id\"={tenant} AND \"Slug\"={$"shipping-{tenant:N}"}");
            await tx.CommitAsync();
            await using var verify = Db();
            Assert.False(await verify.Tenants.AnyAsync(t => t.Id == tenant));
            Assert.Empty(await verify.Orders.ToListAsync()); Assert.Empty(await verify.Shipments.ToListAsync()); Assert.Empty(await verify.ShipmentCancellationOperations.ToListAsync());
            output.WriteLine($"Scenario={scenario}; tenant={tenant}; actual PostgreSQL commits/locks; strict zero provider calls; exact synthetic cleanup verified independently.");
        }
    }

    private sealed class DeclineDeliveryCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData data,
            InterceptionResult result, CancellationToken ct = default) => throw new InvalidOperationException("SYNTHETIC_DELIVERY_COMMIT_REFUSED");
    }

    [ConfiguredSyntheticCleanupFact]
    public async Task CleanupOnlyManifestedSyntheticIdentitySideEffects_ThenVerifyExactZero()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("SHIPPING_FINAL_SYNTHETIC_CLEANUP"));
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var c = new NpgsqlConnection(configured.Database.GetConnectionString()); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        var evidence = Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!;
        var cleanupRows = new List<object>();
        foreach (var path in Directory.GetFiles(evidence, "concurrency-*.json"))
        {
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var tenant = manifest.RootElement.GetProperty("tenant").GetGuid(); var actor = manifest.RootElement.GetProperty("actor").GetGuid();
            await using var inspect = new NpgsqlCommand("SELECT \"Id\",\"SubjectId\",\"SubjectType\" FROM \"CanonicalIdentityLinks\" WHERE (\"SubjectId\"=@actor AND \"SubjectType\"=0 AND \"TenantId\"=@tenant) OR (\"SubjectId\"=@tenant AND \"SubjectType\"=6 AND \"TenantId\"=@tenant)", c, tx);
            inspect.Parameters.AddWithValue("actor", actor); inspect.Parameters.AddWithValue("tenant", tenant);
            await using (var r = await inspect.ExecuteReaderAsync())
                while (await r.ReadAsync()) cleanupRows.Add(new { Id = r.GetGuid(0), SubjectId = r.GetGuid(1), SubjectType = r.GetInt32(2), SyntheticTenant = tenant });
            await using var delete = new NpgsqlCommand("DELETE FROM \"CanonicalIdentityLinks\" WHERE (\"SubjectId\"=@actor AND \"SubjectType\"=0 AND \"TenantId\"=@tenant) OR (\"SubjectId\"=@tenant AND \"SubjectType\"=6 AND \"TenantId\"=@tenant)", c, tx);
            delete.Parameters.AddWithValue("actor", actor); delete.Parameters.AddWithValue("tenant", tenant); await delete.ExecuteNonQueryAsync();
        }
        await File.WriteAllTextAsync(Path.Combine(evidence, "manifested-identity-cleanup.json"), JsonSerializer.Serialize(cleanupRows));
        await tx.CommitAsync();
        await using var independent = new NpgsqlConnection(configured.Database.GetConnectionString()); await independent.OpenAsync();
        foreach (var path in Directory.GetFiles(evidence, "concurrency-*.json"))
        {
            using var m = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            await using var q = new NpgsqlCommand("SELECT count(*) FROM \"CanonicalIdentityLinks\" WHERE \"SubjectId\"=@tenant OR \"SubjectId\"=@actor", independent);
            q.Parameters.AddWithValue("tenant", m.RootElement.GetProperty("tenant").GetGuid()); q.Parameters.AddWithValue("actor", m.RootElement.GetProperty("actor").GetGuid());
            Assert.Equal(0L, (long)(await q.ExecuteScalarAsync())!);
        }
    }
}

public sealed class ConfiguredSyntheticConcurrencyTheoryAttribute : TheoryAttribute
{
    public ConfiguredSyntheticConcurrencyTheoryAttribute()
    { if (Environment.GetEnvironmentVariable("SHIPPING_CONFIGURED_SYNTHETIC_CONCURRENCY") != "1") Skip = "Requires authorized manifested synthetic rows and absent owned API workers; no provisioning/reset fallback."; }
}

public sealed class ConfiguredSyntheticCleanupFactAttribute : FactAttribute
{
    public ConfiguredSyntheticCleanupFactAttribute()
    { if (Environment.GetEnvironmentVariable("SHIPPING_FINAL_SYNTHETIC_CLEANUP") != "1") Skip = "Explicit cleanup of this execution's manifested synthetic identities only."; }
}
