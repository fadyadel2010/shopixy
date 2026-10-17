using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.BackgroundJobs;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

/// <summary>
/// Runs the real job, scope factory, tenant directory, EF filters, carrier service,
/// configuration authority and tracking persistence. Only external providers and
/// unrelated domain authorities are mocked; all business fixtures are isolated SQL.
/// </summary>
public sealed class TrackingSyncTenantContextPostgresTests : IDisposable
{
    // Generic scheduled-job persistence, not Bosta original-account qualification.
    private const string DeterministicCarrierCode = "test-scheduled-tracking";
    private readonly TestDatabaseScope database = TestDatabaseScope.Open();
    private readonly ConcurrentQueue<Guid> dbResolutions = new();
    private readonly ConcurrentQueue<Guid> serviceResolutions = new();
    private readonly ConcurrentQueue<(Guid TenantId, Guid ShipmentId, string AccountId)> calls = new();
    private readonly ConcurrentQueue<(Guid? TenantId, string Sql)> queries = new();
    private readonly ConcurrentDictionary<Guid, int> resolutionCounts = new();
    private readonly ConcurrentQueue<Exception> trackingFailures = new();
    private readonly Mock<IEventPublisher> events = new(MockBehavior.Strict);
    private Guid? failDbTenant;
    private Guid? failProviderTenant;
    private bool rejectTracking;
    private Func<Guid, CancellationToken, Task>? onTracking;
    private Action<Guid, int>? beforeDbResolution;

    private DbContextOptions<ApplicationDbContext> Options()
    {
        using var db = database.CreateDbContext();
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(db.Database.GetConnectionString()).Options;
    }

    private async Task<Guid> SeedTenant(TenantStatus status = TenantStatus.Active, bool active = true, bool deleted = false)
    {
        await using var db = database.CreateDbContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Name = "Isolated scheduled tracking", Slug = $"tracking-{Guid.NewGuid():N}",
            Status = status, IsActive = active, DeletedAtUtc = deleted ? DateTime.UtcNow : null, Currency = "EGP"
        };
        db.Tenants.Add(tenant);
        db.CarrierConfigurations.Add(new CarrierConfiguration
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, CarrierCode = DeterministicCarrierCode, DisplayName = "Mock tracking",
            ApiKey = $"fixture-key-{tenant.Id:N}", AccountId = $"fixture-account-{tenant.Id:N}",
            UseSandbox = true, IsEnabled = true, SupportsTracking = true
        });
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private async Task<Shipment> SeedShipment(Guid tenant, Action<Shipment>? configure = null)
    {
        await using var db = database.CreateDbContext(tenant);
        var order = new Order
        {
            Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = $"TRACK-{Guid.NewGuid():N}",
            Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Pending, Currency = "EGP",
            ShippingAddress = "Isolated street", FulfillmentMode = FulfillmentMode.Carrier,
            FulfillmentCarrierCode = DeterministicCarrierCode
        };
        var shipment = new Shipment
        {
            Id = Guid.NewGuid(), TenantId = tenant, OrderId = order.Id, CarrierCode = DeterministicCarrierCode,
            ShipmentNumber = $"SHP-{Guid.NewGuid():N}", TrackingNumber = $"TRACK-{Guid.NewGuid():N}",
            Status = ShipmentStatus.InTransit, RecipientName = "Fixture", ShippingAddress = "Isolated street",
            Currency = "EGP", LastCarrierSyncAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        configure?.Invoke(shipment);
        db.Orders.Add(order);
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();
        // Preserve the actual PostgreSQL baseline (microsecond precision), not
        // a pre-save DateTime with sub-microsecond ticks that SQL cannot retain.
        await db.Entry(shipment).ReloadAsync();
        return shipment;
    }

    private ServiceProvider BuildProvider(Action<IServiceCollection>? customize = null)
    {
        var options = Options();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILogger<CarrierShipmentService>>(new FailureLogger(trackingFailures));
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITrackingTenantDirectory>(_ => new TrackingTenantDirectory(
            new DbContextOptionsBuilder<ApplicationDbContext>(options)
                .AddInterceptors(new SqlObserver(null, queries)).Options));
        services.AddScoped<ApplicationDbContext>(sp =>
        {
            var context = sp.GetRequiredService<ITenantContext>();
            var tenant = context.RequiredTenantId;
            Assert.NotEqual(Guid.Empty, tenant);
            Assert.Same(sp.GetRequiredService<TenantContext>(), context);
            dbResolutions.Enqueue(tenant);
            beforeDbResolution?.Invoke(tenant, resolutionCounts.AddOrUpdate(tenant, 1, (_, count) => count + 1));
            if (tenant == failDbTenant) throw new InvalidOperationException("Injected tenant-local construction failure");
            return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(options)
                .AddInterceptors(new SqlObserver(tenant, queries)).Options, context);
        });
        var encryption = new Mock<ICarrierCredentialEncryptionService>();
        encryption.Setup(x => x.Decrypt(It.IsAny<string?>())).Returns((string? value) => value ?? string.Empty);
        services.AddSingleton(encryption.Object);
        services.AddSingleton(events.Object);
        services.AddSingleton<CommerceReliabilityTelemetry>();
        services.AddSingleton(Mock.Of<IOrderFulfillmentService>(MockBehavior.Strict));
        services.AddSingleton(Mock.Of<ICustomsEvidenceAdmissionService>(MockBehavior.Strict));
        services.AddSingleton(Mock.Of<ICarrierGeographicMappingService>(MockBehavior.Strict));
        services.AddScoped<IShippingCarrierRegistry>(sp =>
        {
            var tenantContext = sp.GetRequiredService<ITenantContext>();
            var tenant = tenantContext.RequiredTenantId;
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var adapter = new Mock<IShippingCarrierAdapter>(MockBehavior.Strict);
            adapter.SetupGet(x => x.DisplayName).Returns("Mock tracking");
            adapter.Setup(x => x.GetSupportedServiceLevels()).Returns(Array.Empty<ServiceLevelInfo>());
            adapter.Setup(x => x.GetTrackingAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
                .Returns(async (string tracking, CarrierCredentials credentials, CancellationToken ct) =>
                {
                    // Unqualified ordinary queries demonstrate the global filters,
                    // not just explicit TenantId predicates in the job.
                    var visible = await db.Shipments.AsNoTracking().ToListAsync(ct);
                    Assert.All(visible, shipment => Assert.Equal(tenant, shipment.TenantId));
                    var shipment = Assert.Single(visible.Where(shipment => shipment.TrackingNumber == tracking));
                    var configuration = await db.CarrierConfigurations.AsNoTracking().SingleAsync(ct);
                    Assert.Equal(tenant, configuration.TenantId);
                    Assert.Equal(configuration.ApiKey, credentials.ApiKey);
                    Assert.Equal(configuration.AccountId, credentials.AccountId);
                    Assert.True(credentials.UseSandbox);
                    Assert.Equal(tenant, tenantContext.TenantId);
                    calls.Enqueue((tenant, shipment.Id, credentials.AccountId!));
                    if (onTracking != null) await onTracking(tenant, ct);
                    if (tenant == failProviderTenant) throw new InvalidOperationException("Injected mock provider failure");
                    if (rejectTracking)
                        return new ShipmentTrackingResult { Success = false, ErrorCode = "ISOLATED_PROVIDER_REJECTION" };
                    return new ShipmentTrackingResult
                    {
                        Success = true, CurrentStatus = shipment.Status,
                        RawResponse = $"isolated-tracking:{tenant:N}:{shipment.Id:N}", CodCollected = false
                    };
                });
            var registry = new Mock<IShippingCarrierRegistry>(MockBehavior.Strict);
            registry.Setup(x => x.GetAdapter(DeterministicCarrierCode)).Returns(adapter.Object);
            return registry.Object;
        });
        services.AddScoped<ICarrierCapabilityRegistry, CarrierCapabilityRegistry>();
        services.AddScoped<ICarrierIntegrationAuthority, CarrierIntegrationAuthority>();
        services.AddScoped<ICarrierShipmentService>(sp =>
        {
            var tenant = sp.GetRequiredService<ITenantContext>().RequiredTenantId;
            serviceResolutions.Enqueue(tenant);
            return ActivatorUtilities.CreateInstance<CarrierShipmentService>(sp);
        });
        services.AddScoped<ITrackingSyncJob, TrackingSyncJob>();
        customize?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static TrackingSyncJob Job(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<TrackingSyncJob>.Instance);

    private async Task AssertTracking(Guid tenant, Shipment shipment, bool updated)
    {
        await using var db = database.CreateDbContext(tenant);
        var row = await db.Shipments.AsNoTracking().SingleAsync(x => x.Id == shipment.Id);
        Assert.Equal(shipment.Status, row.Status);
        Assert.Equal(updated ? $"isolated-tracking:{tenant:N}:{shipment.Id:N}" : null, row.CarrierTrackingResponse);
        if (updated) Assert.True(row.LastCarrierSyncAtUtc > shipment.LastCarrierSyncAtUtc);
        else Assert.Equal(shipment.LastCarrierSyncAtUtc, row.LastCarrierSyncAtUtc);
    }

    [Fact]
    public async Task SyncAll_RealScopesDiscoverAndPersistOnlyTheirOwnShipmentsAndCredentials_WithoutRepeatedWork()
    {
        var a = await SeedTenant();
        var b = await SeedTenant();
        // One successful write per tenant makes this tenant-boundary proof
        // deterministic without assuming shared Serializable catch-up recovery.
        var shipments = new[] { await SeedShipment(a), await SeedShipment(b) };
        await using var provider = BuildProvider();
        await Job(provider).SyncAllAsync();
        Assert.True(trackingFailures.IsEmpty, string.Join(Environment.NewLine, trackingFailures.Select(error => error.ToString())));
        Assert.Equal(2, calls.Count);
        Assert.Equal(2, calls.Select(call => call.ShipmentId).Distinct().Count());
        foreach (var shipment in shipments)
        {
            Assert.Contains(calls, call => call.TenantId == shipment.TenantId && call.ShipmentId == shipment.Id &&
                                          call.AccountId == $"fixture-account-{shipment.TenantId:N}");
            await AssertTracking(shipment.TenantId, shipment, true);
        }
        Assert.Equal(4, dbResolutions.Count); // one selection + one independent worker per tenant
        Assert.Equal(2, serviceResolutions.Count);
        Assert.All(queries.Where(query => query.TenantId == null), query =>
        {
            Assert.Contains("FROM \"Tenants\"", query.Sql, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Shipments\"", query.Sql, StringComparison.Ordinal);
            Assert.DoesNotContain("\"CarrierConfigurations\"", query.Sql, StringComparison.Ordinal);
        });
        Assert.Single(queries.Where(query => query.TenantId == null));
        await Job(provider).SyncAllAsync();
        Assert.Equal(2, calls.Count); // the existing fifteen-minute cutoff still applies
        events.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConcurrentWorkerScopes_KeepTheirOwnTenantAndAccount_AndDoNotRepeatRejectedCallsWithinCycle()
    {
        var a = await SeedTenant();
        var b = await SeedTenant();
        var shipments = new List<Shipment>();
        foreach (var tenant in new[] { a, b })
            for (var i = 0; i < 5; i++) shipments.Add(await SeedShipment(tenant));
        var arrivals = new ConcurrentDictionary<Guid, int>();
        var gates = new ConcurrentDictionary<Guid, TaskCompletionSource>();
        rejectTracking = true;
        onTracking = async (tenant, ct) =>
        {
            var gate = gates.GetOrAdd(tenant, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            if (arrivals.AddOrUpdate(tenant, 1, (_, count) => count + 1) == 5) gate.TrySetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        };
        await using var provider = BuildProvider();
        await Job(provider).SyncAllAsync();
        Assert.True(trackingFailures.IsEmpty, string.Join(Environment.NewLine, trackingFailures.Select(error => error.ToString())));
        Assert.Equal(10, calls.Count);
        Assert.Equal(10, calls.Select(call => call.ShipmentId).Distinct().Count());
        Assert.Equal(5, arrivals[a]);
        Assert.Equal(5, arrivals[b]);
        Assert.Equal(12, dbResolutions.Count);
        Assert.Equal(10, serviceResolutions.Count);
        foreach (var shipment in shipments)
        {
            Assert.Contains(calls, call => call.TenantId == shipment.TenantId && call.ShipmentId == shipment.Id &&
                                          call.AccountId == $"fixture-account-{shipment.TenantId:N}");
            await AssertTracking(shipment.TenantId, shipment, false);
        }
        events.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TenantWithNoEligibleShipments_NeverResolvesCarrierServiceOrTouchesOtherTenant()
    {
        var empty = await SeedTenant();
        var other = await SeedTenant();
        var shipment = await SeedShipment(other);
        await using var provider = BuildProvider();
        await Job(provider).SyncTenantAsync(empty);
        Assert.Equal(new[] { empty }, dbResolutions);
        Assert.Empty(serviceResolutions);
        Assert.Empty(calls);
        await AssertTracking(other, shipment, false);
    }

    [Fact]
    public async Task DbConstructionFailure_IsIsolatedAndOtherEligibleTenantStillPersistsUnderItsOwnIdentity()
    {
        var a = await SeedTenant();
        var b = await SeedTenant();
        var first = await SeedShipment(a);
        var second = await SeedShipment(b);
        failDbTenant = a;
        await using var provider = BuildProvider();
        await Job(provider).SyncAllAsync();
        Assert.Equal(b, Assert.Single(calls).TenantId);
        Assert.Equal(new[] { b }, serviceResolutions);
        await AssertTracking(a, first, false);
        await AssertTracking(b, second, true);
    }

    [Fact]
    public async Task ProviderFailure_DoesNotLeakContextOrBlockAnotherTenant()
    {
        var a = await SeedTenant();
        var b = await SeedTenant();
        var first = await SeedShipment(a);
        var second = await SeedShipment(b);
        failProviderTenant = a;
        await using var provider = BuildProvider();
        await Job(provider).SyncAllAsync();
        Assert.Equal(2, calls.Count);
        await AssertTracking(a, first, false);
        await AssertTracking(b, second, true);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("suspended")]
    [InlineData("deleted")]
    public async Task SuppliedTenantLocator_MustBeNonemptyAndCurrentlyEligibleBeforeShipmentReads(string scenario)
    {
        var tenant = scenario switch
        {
            "empty" => Guid.Empty,
            "missing" => Guid.NewGuid(),
            "inactive" => await SeedTenant(active: false),
            "suspended" => await SeedTenant(TenantStatus.Suspended),
            _ => await SeedTenant(deleted: true)
        };
        var other = await SeedTenant();
        var shipment = await SeedShipment(other);
        await using var provider = BuildProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Job(provider).SyncTenantAsync(tenant));
        Assert.DoesNotContain(queries, query => query.Sql.Contains("\"Shipments\"", StringComparison.Ordinal));
        Assert.Empty(serviceResolutions);
        Assert.Empty(calls);
        await AssertTracking(other, shipment, false);
    }

    [Fact]
    public async Task Directory_ExcludesInactiveSuspendedDeletedTenants_EvenIfTheyHaveDueShipments()
    {
        var active = await SeedTenant();
        var skipped = new[] { await SeedTenant(active: false), await SeedTenant(TenantStatus.Suspended), await SeedTenant(deleted: true) };
        foreach (var tenant in skipped) await SeedShipment(tenant);
        var shipment = await SeedShipment(active);
        await using var provider = BuildProvider();
        await Job(provider).SyncAllAsync();
        Assert.Equal(active, Assert.Single(calls).TenantId);
        Assert.All(dbResolutions, tenant => Assert.Equal(active, tenant));
        foreach (var tenant in skipped)
        {
            await using var db = database.CreateDbContext(tenant);
            Assert.Null((await db.Shipments.SingleAsync()).CarrierTrackingResponse);
        }
        await AssertTracking(active, shipment, true);
    }

    [Fact]
    public async Task TenantSuspendedBetweenSelectionAndWorker_IsRevalidatedBeforeProviderResolution()
    {
        var a = await SeedTenant();
        var b = await SeedTenant();
        var first = await SeedShipment(a);
        var second = await SeedShipment(b);
        beforeDbResolution = (tenant, count) =>
        {
            if (tenant != a || count != 2) return;
            using var db = database.CreateDbContext();
            var row = db.Tenants.Single(x => x.Id == a);
            row.SetLifecycleStatus(TenantStatus.Suspended, "Isolated fixture suspension");
            db.SaveChanges();
        };
        await using var provider = BuildProvider();
        await Job(provider).SyncAllAsync();
        Assert.Equal(b, Assert.Single(calls).TenantId);
        Assert.DoesNotContain(a, serviceResolutions);
        await AssertTracking(a, first, false);
        await AssertTracking(b, second, true);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign-alias")]
    [InlineData("prepopulated")]
    public async Task InvalidScopeTenantRegistration_FailsClosedBeforeDbOrProviderResolution(string mode)
    {
        var tenant = await SeedTenant();
        await SeedShipment(tenant);
        await using var provider = BuildProvider(services =>
        {
            if (mode == "missing")
            {
                foreach (var descriptor in services.Where(x => x.ServiceType == typeof(TenantContext)).ToArray())
                    services.Remove(descriptor);
            }
            else if (mode == "foreign-alias")
                services.AddScoped<ITenantContext>(_ => new TestTenantContext(Guid.NewGuid()));
            else
                services.AddScoped(_ =>
                {
                    var context = new TenantContext();
                    context.SetTenantId(Guid.NewGuid());
                    return context;
                });
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Job(provider).SyncTenantAsync(tenant));
        Assert.Empty(dbResolutions);
        Assert.Empty(queries);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task OrdinaryRequestScopeAndMissingContextFilters_RemainUnchangedWhileJobUsesIndependentScopes()
    {
        var a = await SeedTenant();
        var b = await SeedTenant();
        await SeedShipment(a);
        var shipment = await SeedShipment(b);
        await using var provider = BuildProvider();
        await using var request = provider.CreateAsyncScope();
        request.ServiceProvider.GetRequiredService<TenantContext>().SetTenantId(b);
        var requestDb = request.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(shipment.Id, (await requestDb.Shipments.AsNoTracking().SingleAsync()).Id);
        await request.ServiceProvider.GetRequiredService<ITrackingSyncJob>().SyncAllAsync();
        Assert.Equal(b, request.ServiceProvider.GetRequiredService<ITenantContext>().TenantId);
        Assert.Equal(shipment.Id, (await requestDb.Shipments.AsNoTracking().SingleAsync()).Id);
        Assert.Single(await requestDb.CarrierConfigurations.AsNoTracking().ToListAsync());
        await using var unbound = new ApplicationDbContext(Options(), new TenantContext());
        Assert.Empty(await unbound.Shipments.ToListAsync());
        Assert.Empty(await unbound.Orders.ToListAsync());
        Assert.Empty(await unbound.CarrierConfigurations.ToListAsync());
        foreach (var entity in new[] { typeof(Shipment), typeof(Order), typeof(CarrierConfiguration) })
            Assert.NotNull(unbound.Model.FindEntityType(entity)!.GetQueryFilter());
    }

    [Fact]
    public async Task EmptyDirectory_PerformsNoTenantOwnedDiscoveryOrServiceResolution()
    {
        await using var provider = BuildProvider();
        await Job(provider).SyncAllAsync();
        Assert.Empty(dbResolutions);
        Assert.Empty(serviceResolutions);
        Assert.Empty(calls);
        Assert.All(queries, query => Assert.Contains("FROM \"Tenants\"", query.Sql, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeOrDuringTracking_PropagatesAndStopsFurtherChunks(bool duringProvider)
    {
        var tenant = await SeedTenant();
        for (var i = 0; i < 6; i++) await SeedShipment(tenant);
        using var cancellation = new CancellationTokenSource();
        if (duringProvider)
            onTracking = (_, ct) => { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        else cancellation.Cancel();
        await using var provider = BuildProvider();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Job(provider).SyncAllAsync(cancellation.Token));
        Assert.InRange(calls.Count, duringProvider ? 1 : 0, duringProvider ? 5 : 0);
        await using var db = database.CreateDbContext(tenant);
        Assert.All(await db.Shipments.ToListAsync(), shipment => Assert.Null(shipment.CarrierTrackingResponse));
    }

    [Fact]
    public async Task DueAndDeliveredRemainOpenToReturnEvidence_WhileOtherTerminalsAreExcluded()
    {
        var tenant = await SeedTenant();
        var due = await SeedShipment(tenant);
        var recent = await SeedShipment(tenant, shipment => shipment.LastCarrierSyncAtUtc = DateTime.UtcNow);
        var missingTracking = await SeedShipment(tenant, shipment => shipment.TrackingNumber = null);
        var delivered = await SeedShipment(tenant, shipment => shipment.Status = ShipmentStatus.Delivered);
        var returned = await SeedShipment(tenant, shipment => shipment.Status = ShipmentStatus.Returned);
        var failed = await SeedShipment(tenant, shipment => shipment.Status = ShipmentStatus.Failed);
        await SeedShipment(tenant, shipment => shipment.IsDeleted = true);
        await using var provider = BuildProvider();
        await Job(provider).SyncAllAsync();
        Assert.Equal(new[] { due.Id, delivered.Id }.OrderBy(id => id), calls.Select(call => call.ShipmentId).OrderBy(id => id));
        await AssertTracking(tenant, due, true);
        await AssertTracking(tenant, delivered, true);
        foreach (var row in new[] { recent, missingTracking, returned, failed }) await AssertTracking(tenant, row, false);
        // Delivered remains observable for actual return evidence. Failed,
        // cancelled and returned are terminal selection exclusions.
        Assert.Equal(2, serviceResolutions.Count);
    }

    public void Dispose() => database.Dispose();

    private sealed class FailureLogger(ConcurrentQueue<Exception> failures) : ILogger<CarrierShipmentService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception != null) failures.Enqueue(exception);
        }
    }

    private sealed class SqlObserver(Guid? tenant, ConcurrentQueue<(Guid? TenantId, string Sql)> queries) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            queries.Enqueue((tenant, command.CommandText));
            return ValueTask.FromResult(result);
        }
    }
}
