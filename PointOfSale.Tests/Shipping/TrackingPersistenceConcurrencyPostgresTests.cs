using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.EventBus;
using PointOfSale.Infrastructure.Fulfillment;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Tests.Infrastructure;
using Xunit.Abstractions;
using BusMessage = MassTransit.EntityFrameworkCoreIntegration.OutboxMessage;

namespace PointOfSale.Tests.Shipping;

public sealed class TrackingPersistenceConcurrencyPostgresTests(ITestOutputHelper output) : IDisposable
{
    // These are generic transaction/consumer tests, not qualification of a
    // real Bosta account or the original Bosta create request.
    private const string DeterministicCarrierCode = "test-tracking-concurrency";
    private readonly TestDatabaseScope database = TestDatabaseScope.Open();
    private readonly ConcurrentQueue<(Guid Tenant, string Tracking)> calls = new();
    private readonly ConcurrentQueue<Exception> failures = new();
    private readonly ConcurrentDictionary<Guid, long> initialOrderVersions = new();
    private Func<Shipment, CancellationToken, Task<ShipmentTrackingResult>> observe = (s, _) => Task.FromResult(new ShipmentTrackingResult
    {
        Success = true, CurrentStatus = s.Status, RawResponse = $"observation:{s.Id:N}"
    });

    private string ConnectionString()
    {
        using var db = database.CreateDbContext();
        return db.Database.GetConnectionString()!;
    }

    private async Task<Guid> SeedTenant()
    {
        var tenant = Guid.NewGuid();
        await using var db = database.CreateDbContext(tenant);
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Isolated concurrency", Slug = $"concurrency-{tenant:N}", IsActive = true, Status = TenantStatus.Active });
        db.CarrierConfigurations.Add(new CarrierConfiguration
        {
            Id = Guid.NewGuid(), TenantId = tenant, CarrierCode = DeterministicCarrierCode, DisplayName = "Mock only",
            ApiKey = $"fixture-key-{tenant:N}", AccountId = $"fixture-account-{tenant:N}", UseSandbox = true, IsEnabled = true, SupportsTracking = true
        });
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task<Shipment> SeedShipment(Guid tenant, bool packed = false)
    {
        await using var db = database.CreateDbContext(tenant);
        var order = new Order
        {
            Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = $"CONCURRENT-{Guid.NewGuid():N}", Currency = "EGP",
            Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Pending, FulfillmentStatus = packed ? FulfillmentStatus.Packed : FulfillmentStatus.OutForDelivery,
            ShippingAddress = "Fixture", FulfillmentMode = FulfillmentMode.Carrier, FulfillmentCarrierCode = DeterministicCarrierCode
        };
        var shipment = new Shipment
        {
            Id = Guid.NewGuid(), TenantId = tenant, OrderId = order.Id, ShipmentNumber = $"SHP-{Guid.NewGuid():N}",
            CarrierCode = DeterministicCarrierCode, TrackingNumber = $"TRACK-{Guid.NewGuid():N}", Status = ShipmentStatus.InTransit,
            RecipientName = "Fixture", ShippingAddress = "Fixture", Currency = "EGP", LastCarrierSyncAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        db.Orders.Add(order);
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();
        await db.Entry(shipment).ReloadAsync();
        await db.Entry(order).ReloadAsync();
        initialOrderVersions[order.Id] = order.ProjectionMutationVersion;
        return shipment;
    }

    private ServiceProvider Provider(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString()).AddInterceptors(interceptors).Options;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped(sp => new ApplicationDbContext(options, sp.GetRequiredService<ITenantContext>()));
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
        services.AddScoped<IOrderFulfillmentService, OrderFulfillmentService>();
        services.AddSingleton<ILogger<CarrierShipmentService>>(new FailureLogger(failures));
        services.AddSingleton<CommerceReliabilityTelemetry>();
        services.AddSingleton(Mock.Of<ICustomsEvidenceAdmissionService>(MockBehavior.Strict));
        services.AddSingleton(Mock.Of<ICarrierGeographicMappingService>(MockBehavior.Strict));
        var encryption = new Mock<ICarrierCredentialEncryptionService>();
        encryption.Setup(x => x.Decrypt(It.IsAny<string?>())).Returns((string? value) => value ?? "");
        services.AddSingleton(encryption.Object);
        services.AddScoped<IShippingCarrierRegistry>(sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var tenant = sp.GetRequiredService<ITenantContext>().RequiredTenantId;
            var adapter = new Mock<IShippingCarrierAdapter>(MockBehavior.Strict);
            adapter.SetupGet(x => x.DisplayName).Returns("Mock only");
            adapter.Setup(x => x.GetSupportedServiceLevels()).Returns(Array.Empty<ServiceLevelInfo>());
            adapter.Setup(x => x.GetTrackingAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
                .Returns(async (string tracking, CarrierCredentials credentials, CancellationToken ct) =>
                {
                    Assert.Equal($"fixture-key-{tenant:N}", credentials.ApiKey);
                    Assert.Equal($"fixture-account-{tenant:N}", credentials.AccountId);
                    Assert.True(credentials.UseSandbox);
                    var visible = await db.Shipments.AsNoTracking().ToListAsync(ct);
                    Assert.All(visible, s => Assert.Equal(tenant, s.TenantId));
                    var shipment = Assert.Single(visible.Where(s => s.TrackingNumber == tracking));
                    calls.Enqueue((tenant, tracking));
                    return await observe(shipment, ct);
                });
            var registry = new Mock<IShippingCarrierRegistry>(MockBehavior.Strict);
            registry.Setup(x => x.GetAdapter(DeterministicCarrierCode)).Returns(adapter.Object);
            return registry.Object;
        });
        services.AddScoped<ICarrierCapabilityRegistry, CarrierCapabilityRegistry>();
        services.AddScoped<ICarrierIntegrationAuthority, CarrierIntegrationAuthority>();
        services.AddScoped<ICarrierShipmentService, CarrierShipmentService>();
        // Real EF bus outbox, never started: no network dispatch or live provider.
        services.AddMassTransitTestHarness(config => config.AddEntityFrameworkOutbox<ApplicationDbContext>(outbox =>
        {
            outbox.UsePostgres();
            outbox.UseBusOutbox();
        }));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<CarrierSyncResult> Sync(ServiceProvider provider, Shipment shipment)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenantId(shipment.TenantId);
        return await scope.ServiceProvider.GetRequiredService<ICarrierShipmentService>().SyncTrackingAsync(shipment.TenantId, shipment.Id);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DistinctShipments_OverlappingSqlSnapshots_PersistWithoutRepeatingProvider(bool changeStatus, bool separateTenants)
    {
        var tenant = await SeedTenant();
        var secondTenant = separateTenants ? await SeedTenant() : tenant;
        var shipments = new[] { await SeedShipment(tenant), await SeedShipment(secondTenant) };
        var eta = DateTime.UtcNow.AddDays(1);
        if (changeStatus) observe = (s, _) => Task.FromResult(new ShipmentTrackingResult
        {
            Success = true, CurrentStatus = ShipmentStatus.OutForDelivery, RawResponse = $"observation:{s.Id:N}", EstimatedDeliveryDate = eta
        });
        var overlap = new SnapshotOverlap(ConnectionString(), 2, output);
        await using var provider = Provider(new SequentialScan(), overlap);
        var results = await Task.WhenAll(shipments.Select(s => Sync(provider, s)));
        foreach (var error in failures) output.WriteLine(error.ToString());
        Assert.Equal(2, overlap.Arrivals);
        Assert.All(overlap.Isolations, isolation => Assert.Equal(IsolationLevel.ReadCommitted, isolation));
        Assert.Equal(0, overlap.PredicateLocks);
        Assert.Empty(failures);
        Assert.All(results, result => Assert.True(result.Success, string.Join("\n", failures.Select(e => e.ToString()))));
        Assert.Equal(2, calls.Count);
        foreach (var shipment in shipments)
        {
            Assert.Single(calls.Where(c => c.Tracking == shipment.TrackingNumber));
            await using var db = database.CreateDbContext(shipment.TenantId);
            var persisted = await db.Shipments.AsNoTracking().SingleAsync(s => s.Id == shipment.Id);
            Assert.Equal(changeStatus ? ShipmentStatus.OutForDelivery : shipment.Status, persisted.Status);
            Assert.Equal($"observation:{shipment.Id:N}", persisted.CarrierTrackingResponse);
            Assert.True(persisted.LastCarrierSyncAtUtc > shipment.LastCarrierSyncAtUtc);
            if (changeStatus)
            {
                Assert.NotNull(persisted.OutForDeliveryAtUtc);
                Assert.InRange((persisted.EstimatedDeliveryDate!.Value - eta).Duration(), TimeSpan.Zero, TimeSpan.FromMicroseconds(1));
            }
            await AssertOutbox(db, shipment, changeStatus ? 2 : 0);
            await AssertFinancialBoundaries(db);
        }
    }

    private async Task AssertOutbox(ApplicationDbContext db, Shipment shipment, int expected)
    {
        var messages = await db.Set<BusMessage>().AsNoTracking().ToListAsync();
        var owned = messages.Where(m => m.Body.Contains(shipment.Id.ToString(), StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(expected, owned.Length);
        Assert.Equal(expected, owned.Select(m => m.MessageId).Distinct().Count());
        if (expected == 2)
        {
            Assert.Single(owned.Where(m => m.MessageType.Contains(":ShipmentStatusChangedEvent", StringComparison.Ordinal)));
            Assert.Single(owned.Where(m => m.MessageType.Contains(":ShipmentOutForDeliveryEvent", StringComparison.Ordinal)));
            Assert.All(owned, m => Assert.Contains(shipment.TenantId.ToString(), m.Body, StringComparison.OrdinalIgnoreCase));
        }
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == shipment.OrderId);
        Assert.Equal(initialOrderVersions[order.Id] + (expected > 0 ? 1L : 0L), order.ProjectionMutationVersion);
    }

    private static async Task AssertFinancialBoundaries(ApplicationDbContext db)
    {
        Assert.Empty(await db.PaymentIntents.ToListAsync());
        Assert.Empty(await db.OrderRefunds.ToListAsync());
        Assert.Empty(await db.AccountingOutboxMessages.ToListAsync());
        Assert.Empty(await db.PaymentCancellationOutboxMessages.ToListAsync());
        Assert.Empty(await db.JournalEntries.ToListAsync());
        Assert.Empty(await db.StockMovements.ToListAsync());
        Assert.Empty(await db.ShipmentCancellationOperations.ToListAsync());
    }

    [Fact]
    public async Task SameShipment_RowLocksSerializeAuthority_AndOnlyOneObservationCommitsEvents()
    {
        var tenant = await SeedTenant();
        var first = await SeedShipment(tenant);
        observe = (s, _) => Task.FromResult(new ShipmentTrackingResult { Success = true, CurrentStatus = ShipmentStatus.OutForDelivery, RawResponse = $"observation:{s.Id:N}" });
        var overlap = new BeforeOrderLocks(2);
        await using var provider = Provider(overlap);
        var results = await Task.WhenAll(Sync(provider, first), Sync(provider, first));
        Assert.Equal(2, overlap.Arrivals);
        Assert.Single(results.Where(r => r.Success));
        Assert.Equal("TRACKING_OBSERVATION_STALE", Assert.Single(results.Where(r => !r.Success)).ErrorCode);
        Assert.Empty(failures);
        Assert.Equal(2, calls.Count);
        await using var verification = database.CreateDbContext(tenant);
        var rows = await verification.Shipments.AsNoTracking().ToListAsync();
        Assert.All(rows, s => Assert.Equal(ShipmentStatus.OutForDelivery, s.Status));
        Assert.All(rows, s => Assert.Equal($"observation:{s.Id:N}", s.CarrierTrackingResponse));
        Assert.All(rows, s => Assert.True(s.LastCarrierSyncAtUtc > first.LastCarrierSyncAtUtc));
        Assert.Equal(initialOrderVersions[first.OrderId] + 1L, (await verification.Orders.SingleAsync(o => o.Id == first.OrderId)).ProjectionMutationVersion);
        var messages = await verification.Set<BusMessage>().ToListAsync();
        Assert.Equal(2, messages.Count);
        foreach (var shipment in rows) Assert.Equal(2, messages.Count(m => m.Body.Contains(shipment.Id.ToString(), StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, messages.Select(m => m.MessageId).Distinct().Count());
        await AssertFinancialBoundaries(verification);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("tracking")]
    [InlineData("awb")]
    [InlineData("external-reference")]
    [InlineData("request-id")]
    [InlineData("carrier")]
    [InlineData("order")]
    [InlineData("newer-observation")]
    [InlineData("updated-at")]
    [InlineData("deleted")]
    public async Task ChangedDuringProviderCall_RejectsStaleObservation_AndPreservesSqlEvidence(string change)
    {
        var tenant = await SeedTenant();
        var shipment = await SeedShipment(tenant);
        var otherOrderId = Guid.NewGuid();
        if (change == "order")
        {
            await using var seed = database.CreateDbContext(tenant);
            var otherOrder = new Order { Id = otherOrderId, TenantId = tenant, OrderNumber = $"REPLACEMENT-{otherOrderId:N}", Currency = "EGP", ShippingAddress = "Fixture" };
            seed.Orders.Add(otherOrder);
            await seed.SaveChangesAsync();
            await seed.Entry(otherOrder).ReloadAsync();
            initialOrderVersions[otherOrderId] = otherOrder.ProjectionMutationVersion;
        }
        Shipment? changed = null;
        observe = async (s, ct) =>
        {
            await using var writer = database.CreateDbContext(tenant);
            var row = await writer.Shipments.SingleAsync(x => x.Id == s.Id, ct);
            switch (change)
            {
                case "status": row.Status = ShipmentStatus.OutForDelivery; break;
                case "tracking": row.TrackingNumber = "new-target"; break;
                case "awb": row.AwbNumber = "new-awb"; break;
                case "external-reference": row.ExternalReferenceId = "new-external-reference"; break;
                case "request-id": row.CarrierRequestId = "new-registration"; break;
                case "carrier": row.CarrierCode = "dhl"; break;
                case "order": row.OrderId = otherOrderId; break;
                case "newer-observation": row.LastCarrierSyncAtUtc = DateTime.UtcNow; row.CarrierTrackingResponse = "newer-evidence"; break;
                case "updated-at": row.UpdatedAt = DateTime.UtcNow; break;
                case "deleted": row.IsDeleted = true; break;
            }
            await writer.SaveChangesAsync(ct);
            // Keep the deleted row attached; the global filter cannot return it.
            changed = row;
            return new ShipmentTrackingResult { Success = true, CurrentStatus = ShipmentStatus.OutForDelivery, RawResponse = "stale-observation" };
        };
        await using var provider = Provider();
        var result = await Sync(provider, shipment);
        Assert.False(result.Success);
        Assert.Equal("TRACKING_OBSERVATION_STALE", result.ErrorCode);
        Assert.Single(calls);
        await using var db = database.CreateDbContext(tenant);
        if (change == "deleted") Assert.Empty(await db.Shipments.Where(s => s.Id == shipment.Id).ToListAsync());
        else
        {
            var row = await db.Shipments.AsNoTracking().SingleAsync(s => s.Id == shipment.Id);
            Assert.Equal(changed!.Status, row.Status);
            Assert.Equal(changed.TrackingNumber, row.TrackingNumber);
            Assert.Equal(changed.OrderId, row.OrderId);
            Assert.Equal(changed.CarrierCode, row.CarrierCode);
            Assert.Equal(changed.AwbNumber, row.AwbNumber);
            Assert.Equal(changed.ExternalReferenceId, row.ExternalReferenceId);
            Assert.Equal(changed.CarrierRequestId, row.CarrierRequestId);
            Assert.Equal(changed.CarrierTrackingResponse, row.CarrierTrackingResponse);
            Assert.Equal(change == "newer-observation" ? "newer-evidence" : null, row.CarrierTrackingResponse);
            Assert.InRange((row.LastCarrierSyncAtUtc!.Value - changed.LastCarrierSyncAtUtc!.Value).Duration(), TimeSpan.Zero, TimeSpan.FromMicroseconds(1));
        }
        Assert.Empty(await db.Set<BusMessage>().ToListAsync());
        Assert.All(await db.Orders.ToListAsync(), o => Assert.Equal(initialOrderVersions[o.Id], o.ProjectionMutationVersion));
        await AssertFinancialBoundaries(db);
    }

    [Fact]
    public async Task OlderInFlightObservation_CannotOverwriteNewerSuccessfulExecution()
    {
        var tenant = await SeedTenant();
        var shipment = await SeedShipment(tenant);
        var oldEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observationNumber = 0;
        observe = async (_, ct) =>
        {
            var number = Interlocked.Increment(ref observationNumber);
            if (number == 1) { oldEntered.SetResult(); await releaseOld.Task.WaitAsync(TimeSpan.FromSeconds(15), ct); }
            return new ShipmentTrackingResult { Success = true, CurrentStatus = ShipmentStatus.OutForDelivery, RawResponse = $"observation-{number}" };
        };
        await using var provider = Provider();
        var old = Sync(provider, shipment);
        await oldEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var newer = await Sync(provider, shipment);
        Assert.True(newer.Success);
        releaseOld.SetResult();
        Assert.Equal("TRACKING_OBSERVATION_STALE", (await old).ErrorCode);
        Assert.Equal(2, calls.Count); // two executions, exactly one HTTP per execution
        await using var db = database.CreateDbContext(tenant);
        var row = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentStatus.OutForDelivery, row.Status);
        Assert.Equal("observation-2", row.CarrierTrackingResponse);
        Assert.True(row.LastCarrierSyncAtUtc > shipment.LastCarrierSyncAtUtc);
        await AssertOutbox(db, shipment, 2);
        await AssertFinancialBoundaries(db);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsuccessfulOrThrowingProvider_NeverPersistsTrackingOrOutbox(bool throws)
    {
        var tenant = await SeedTenant();
        var shipment = await SeedShipment(tenant);
        observe = (_, _) => throws ? throw new InvalidOperationException("Isolated provider failure") :
            Task.FromResult(new ShipmentTrackingResult { Success = false, CurrentStatus = ShipmentStatus.OutForDelivery, RawResponse = "rejected-evidence", ErrorCode = "REJECTED" });
        await using var provider = Provider();
        Assert.False((await Sync(provider, shipment)).Success);
        Assert.Single(calls);
        await using var db = database.CreateDbContext(tenant);
        var row = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(shipment.Status, row.Status);
        Assert.Null(row.CarrierTrackingResponse);
        Assert.Equal(shipment.LastCarrierSyncAtUtc, row.LastCarrierSyncAtUtc);
        await AssertOutbox(db, shipment, 0);
        await AssertFinancialBoundaries(db);
    }

    [Fact]
    public async Task PostgreSqlCommitRejection_RollsBackShipmentOrderAndRealBusOutbox_WithoutHttpRetry()
    {
        var tenant = await SeedTenant();
        var shipment = await SeedShipment(tenant);
        observe = (_, _) => Task.FromResult(new ShipmentTrackingResult { Success = true, CurrentStatus = ShipmentStatus.OutForDelivery, RawResponse = "uncommitted" });
        await using var provider = Provider(new CommitRejection());
        Assert.False((await Sync(provider, shipment)).Success);
        Assert.Single(calls);
        var error = Assert.Single(failures);
        output.WriteLine(error.ToString());
        Assert.Equal("40001", Assert.IsType<PostgresException>(error).SqlState);
        await using var db = database.CreateDbContext(tenant);
        var row = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(shipment.Status, row.Status);
        Assert.Null(row.CarrierTrackingResponse);
        Assert.Null(row.OutForDeliveryAtUtc);
        Assert.Equal(shipment.LastCarrierSyncAtUtc, row.LastCarrierSyncAtUtc);
        await AssertOutbox(db, shipment, 0);
        Assert.Empty(await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxState>().ToListAsync());
        await AssertFinancialBoundaries(db);
    }

    [Fact]
    public async Task ChangedCarrierAccount_RejectsObservationWithoutFinancialOrTrackingMutation()
    {
        var tenant = await SeedTenant();
        var shipment = await SeedShipment(tenant);
        observe = async (_, ct) =>
        {
            await using var writer = database.CreateDbContext(tenant);
            (await writer.CarrierConfigurations.SingleAsync(ct)).AccountId = "replacement-account";
            await writer.SaveChangesAsync(ct);
            return new ShipmentTrackingResult { Success = true, CurrentStatus = ShipmentStatus.OutForDelivery, RawResponse = "old-account" };
        };
        await using var provider = Provider();
        Assert.Equal("TRACKING_CONFIGURATION_CHANGED", (await Sync(provider, shipment)).ErrorCode);
        Assert.Single(calls);
        await using var db = database.CreateDbContext(tenant);
        var row = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(shipment.Status, row.Status);
        Assert.Null(row.CarrierTrackingResponse);
        Assert.Equal(shipment.LastCarrierSyncAtUtc, row.LastCarrierSyncAtUtc);
        await AssertOutbox(db, shipment, 0);
        await AssertFinancialBoundaries(db);
    }

    [Fact]
    public async Task WaitingShipmentLock_ReloadsCommittedIdentity_AndRejectsPreviouslyObtainedObservation()
    {
        var tenant = await SeedTenant();
        var shipment = await SeedShipment(tenant);
        await using var writer = database.CreateDbContext(tenant);
        await using var writeTransaction = await writer.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        (await writer.Shipments.SingleAsync()).TrackingNumber = "new-locked-target";
        await writer.SaveChangesAsync();
        var waiting = new WaitingShipmentLock();
        await using var provider = Provider(waiting);
        var task = Sync(provider, shipment);
        var pid = await waiting.Executing.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await using var monitor = new NpgsqlConnection(ConnectionString());
        await monitor.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        var blocked = false;
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand("SELECT wait_event_type FROM pg_stat_activity WHERE pid = @pid", monitor);
            command.Parameters.AddWithValue("pid", pid);
            if ((await command.ExecuteScalarAsync()) as string == "Lock") { blocked = true; break; }
            await Task.Delay(25);
        }
        Assert.True(blocked, "Tracking must actually be waiting on the writer's PostgreSQL row lock.");
        output.WriteLine($"Tracking PID={pid} wait_event_type=Lock while the target identity update is uncommitted.");
        Assert.False(task.IsCompleted);
        await writeTransaction.CommitAsync();
        var result = await task;
        Assert.False(result.Success);
        Assert.Equal("TRACKING_OBSERVATION_STALE", result.ErrorCode);
        Assert.Single(calls);
        Assert.Empty(failures);
        await using var db = database.CreateDbContext(tenant);
        var row = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal("new-locked-target", row.TrackingNumber);
        Assert.Equal(shipment.Status, row.Status);
        Assert.Null(row.CarrierTrackingResponse);
        Assert.Equal(shipment.LastCarrierSyncAtUtc, row.LastCarrierSyncAtUtc);
        await AssertOutbox(db, shipment, 0);
        await AssertFinancialBoundaries(db);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignShipmentOrMissingTenantContext_CannotReachProviderOrWrite(bool missingContext)
    {
        var tenant = await SeedTenant();
        var foreign = await SeedTenant();
        var shipment = await SeedShipment(foreign);
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        if (!missingContext) scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenantId(tenant);
        // Exercise the service's own missing-context guard without first invoking
        // the fixture registry factory, which itself correctly requires a tenant.
        var service = missingContext
            ? ActivatorUtilities.CreateInstance<CarrierShipmentService>(scope.ServiceProvider,
                Mock.Of<IShippingCarrierRegistry>(MockBehavior.Strict), Mock.Of<ICarrierIntegrationAuthority>(MockBehavior.Strict))
            : scope.ServiceProvider.GetRequiredService<ICarrierShipmentService>();
        var result = await service.SyncTrackingAsync(tenant, shipment.Id);
        Assert.False(result.Success);
        Assert.Equal(missingContext ? "TRACKING_TENANT_INVALID" : "SHIPMENT_NOT_FOUND", result.ErrorCode);
        Assert.Empty(calls);
        await using var db = database.CreateDbContext(foreign);
        var row = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Null(row.CarrierTrackingResponse);
        Assert.Equal(shipment.LastCarrierSyncAtUtc, row.LastCarrierSyncAtUtc);
        await AssertOutbox(db, shipment, 0);
        await AssertFinancialBoundaries(db);
    }

    [Fact]
    public async Task ExistingCallerTransaction_FailsBeforeHttp_WithoutRollingBackCallerWork()
    {
        var tenant = await SeedTenant();
        var shipment = await SeedShipment(tenant);
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenantId(tenant);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await scope.ServiceProvider.GetRequiredService<ICarrierShipmentService>().SyncTrackingAsync(tenant, shipment.Id);
        Assert.Equal("TRACKING_PERSISTENCE_CONTEXT_BUSY", result.ErrorCode);
        Assert.Empty(calls);
        Assert.Same(transaction, db.Database.CurrentTransaction);
        Assert.Null((await db.Shipments.SingleAsync()).CarrierTrackingResponse);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task UnresolvedLifecycleTransition_RetainsDiagnosticMetadata_WithoutClaimingSuccessOrEvents()
    {
        var tenant = await SeedTenant();
        var shipment = await SeedShipment(tenant);
        observe = (_, _) => Task.FromResult(new ShipmentTrackingResult { Success = true, CurrentStatus = ShipmentStatus.PickedUp, RawResponse = "regressive-observation" });
        await using var provider = Provider();
        var result = await Sync(provider, shipment);
        Assert.False(result.Success);
        Assert.Equal("TRACKING_PROGRESSION_UNRESOLVED", result.ErrorCode);
        Assert.False(result.StatusChanged);
        Assert.Single(calls);
        await using var db = database.CreateDbContext(tenant);
        var row = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(shipment.Status, row.Status);
        Assert.Equal("regressive-observation", row.CarrierTrackingResponse);
        Assert.True(row.LastCarrierSyncAtUtc > shipment.LastCarrierSyncAtUtc);
        await AssertOutbox(db, shipment, 0);
        await AssertFinancialBoundaries(db);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealFulfillmentBridge_IntermediateSaveAndEvents_AreAtomicWithTrackingCommit(bool rejectCommit)
    {
        var tenant = await SeedTenant();
        var shipment = await SeedShipment(tenant, packed: true);
        observe = (_, _) => Task.FromResult(new ShipmentTrackingResult { Success = true, CurrentStatus = ShipmentStatus.OutForDelivery, RawResponse = "fulfillment-observation" });
        await using var provider = rejectCommit ? Provider(new CommitRejection()) : Provider();
        var result = await Sync(provider, shipment);
        Assert.Equal(!rejectCommit, result.Success);
        foreach (var error in failures) output.WriteLine(error.ToString());
        Assert.Single(calls);
        await using var db = database.CreateDbContext(tenant);
        var row = await db.Shipments.AsNoTracking().SingleAsync();
        var order = await db.Orders.AsNoTracking().SingleAsync();
        var messages = await db.Set<BusMessage>().AsNoTracking().ToListAsync();
        if (rejectCommit)
        {
            Assert.Equal(ShipmentStatus.InTransit, row.Status);
            Assert.Null(row.CarrierTrackingResponse);
            Assert.Equal(shipment.LastCarrierSyncAtUtc, row.LastCarrierSyncAtUtc);
            Assert.Equal(FulfillmentStatus.Packed, order.FulfillmentStatus);
            Assert.Equal(OrderStatus.Confirmed, order.Status);
            Assert.Equal(initialOrderVersions[order.Id], order.ProjectionMutationVersion);
            Assert.Empty(messages);
        }
        else
        {
            Assert.Equal(ShipmentStatus.OutForDelivery, row.Status);
            Assert.Equal("fulfillment-observation", row.CarrierTrackingResponse);
            Assert.True(row.LastCarrierSyncAtUtc > shipment.LastCarrierSyncAtUtc);
            Assert.Equal(FulfillmentStatus.OutForDelivery, order.FulfillmentStatus);
            Assert.Equal(OrderStatus.Shipped, order.Status);
            Assert.Equal(4, messages.Count);
            foreach (var type in new[] { "ShipmentStatusChangedEvent", "ShipmentOutForDeliveryEvent", "OrderOutForDeliveryEvent", "OrderStatusChangedEvent" })
                Assert.Single(messages.Where(m => m.MessageType.Contains(":" + type, StringComparison.Ordinal)));
            Assert.Equal(4, messages.Select(m => m.MessageId).Distinct().Count());
        }
        await AssertFinancialBoundaries(db);
    }

    public void Dispose() => database.Dispose();

    private sealed class FailureLogger(ConcurrentQueue<Exception> errors) : ILogger<CarrierShipmentService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        { if (error != null) errors.Enqueue(error); }
    }

    // Small production tables can choose this plan naturally. Force that same
    // legal plan in the fixture so SIREAD relation-level conflict is repeatable.
    private sealed class SequentialScan : DbTransactionInterceptor
    {
        public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData data,
            DbTransaction result, CancellationToken ct = default)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = result;
            command.CommandText = "SET LOCAL enable_indexscan = off; SET LOCAL enable_bitmapscan = off";
            await command.ExecuteNonQueryAsync(ct);
            return result;
        }
    }

    private sealed class SnapshotOverlap(string connectionString, int participants, ITestOutputHelper output) : DbCommandInterceptor
    {
        private readonly ConcurrentDictionary<Guid, int> contexts = new();
        private readonly ConcurrentQueue<int> pids = new();
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public int Arrivals => arrivals;
        public int PredicateLocks { get; private set; }
        public ConcurrentQueue<IsolationLevel> Isolations { get; } = new();
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken ct = default)
        {
            if (command.Transaction == null || !command.CommandText.Contains("FROM \"Shipments\"", StringComparison.Ordinal) ||
                !contexts.TryAdd(data.Context!.ContextId.InstanceId, 1)) return result;
            var pid = ((NpgsqlConnection)command.Connection!).ProcessID;
            pids.Enqueue(pid);
            Isolations.Enqueue(command.Transaction.IsolationLevel);
            output.WriteLine($"Snapshot PID={pid}, isolation={command.Transaction.IsolationLevel}, SQL={command.CommandText}");
            if (Interlocked.Increment(ref arrivals) == participants)
            {
                await using var monitor = new NpgsqlConnection(connectionString);
                await monitor.OpenAsync(ct);
                await using var locks = new NpgsqlCommand("SELECT pid, locktype, mode, relation::regclass::text FROM pg_locks WHERE pid = ANY(@pids) AND mode = 'SIReadLock' ORDER BY pid, relation", monitor);
                locks.Parameters.AddWithValue("pids", pids.ToArray());
                await using var reader = await locks.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    PredicateLocks++;
                    output.WriteLine($"Predicate lock PID={reader.GetInt32(0)}, {reader.GetString(1)}, {reader.GetString(2)}, {reader.GetString(3)}");
                }
                gate.TrySetResult();
            }
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            return result;
        }
    }

    private sealed class BeforeOrderLocks(int participants) : DbCommandInterceptor
    {
        private readonly ConcurrentDictionary<Guid, int> contexts = new();
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public int Arrivals => arrivals;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.Transaction == null || !command.CommandText.Contains("FROM \"Orders\"", StringComparison.Ordinal) ||
                !contexts.TryAdd(data.Context!.ContextId.InstanceId, 1)) return result;
            if (Interlocked.Increment(ref arrivals) == participants) gate.TrySetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            return result;
        }
    }

    private sealed class CommitRejection : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData data,
            InterceptionResult result, CancellationToken ct = default)
        {
            await using var command = transaction.Connection!.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DO $$ BEGIN RAISE EXCEPTION USING ERRCODE = '40001', MESSAGE = 'isolated forced commit rejection'; END $$";
            await command.ExecuteNonQueryAsync(ct);
            return result;
        }
    }

    private sealed class WaitingShipmentLock : DbCommandInterceptor
    {
        public TaskCompletionSource<int> Executing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.Transaction != null && command.CommandText.Contains("FROM \"Shipments\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
                Executing.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            return ValueTask.FromResult(result);
        }
    }
}
