using System.Data;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Core.Entities;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;
using Xunit.Abstractions;

namespace PointOfSale.Tests.Payments;

public sealed class SharedCheckoutOutboxConfiguredTests(ITestOutputHelper output)
{
    public sealed record RetryProbe(Guid Id);

    [SharedCheckoutConfiguredFact]
    public async Task TransferredAggregate_MustReloadShadowXmin_NotAttachWithZeroVersion()
    {
        await using var provider = BuildProvider();
        await using var first = provider.CreateAsyncScope();
        var source = first.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var detached = await source.Orders.AsNoTracking().FirstAsync(o => o.CustomerNotes.StartsWith("SHQA20261019-"));
        await using var second = provider.CreateAsyncScope();
        var target = second.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        target.Attach(detached);
        Assert.Equal(0u, target.Entry(detached).Property<uint>("xmin").OriginalValue);
        target.ChangeTracker.Clear();
        var authoritative = await target.Orders.SingleAsync(o => o.Id == detached.Id);
        Assert.True(target.Entry(authoritative).Property<uint>("xmin").OriginalValue > 0u);
        Assert.Equal(detached.Total, authoritative.Total);
        Assert.False(target.ChangeTracker.HasChanges());
    }

    [SharedCheckoutConfiguredFact]
    public async Task RollbackBeforeFirstOutboxFlush_CannotBeRecoveredByClearingOnlyEfTracking()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await publisher.Publish(new RetryProbe(Guid.NewGuid()));
            // The checkout publishes multiple events. A serialization failure in
            // subsequent authority enrichment / the first outbox flush leaves
            // the scoped publisher holding an Added (not committed) state entry.
            Assert.Equal(EntityState.Added, db.ChangeTracker.Entries<OutboxState>().Single().State);
            await transaction.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        await using var retry = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await publisher.Publish(new RetryProbe(Guid.NewGuid()));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgres.SqlState);
        Assert.Equal("FK_OutboxMessage_OutboxState_OutboxId", postgres.ConstraintName);
        output.WriteLine(postgres.ToString());
        await retry.RollbackAsync();
    }

    [SharedCheckoutConfiguredFact]
    public async Task FreshScopeAfterRollback_HasIndependentContextAndOutboxIdentity()
    {
        await using var provider = BuildProvider();
        var ids = new List<Guid>();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await scope.ServiceProvider.GetRequiredService<IPublishEndpoint>().Publish(new RetryProbe(Guid.NewGuid()));
            if (attempt > 0) await db.SaveChangesAsync();
            ids.Add(db.ChangeTracker.Entries<OutboxState>().Single().Entity.OutboxId);
            await transaction.RollbackAsync();
        }
        Assert.NotEqual(ids[0], ids[1]);
        await using var observer = provider.CreateAsyncScope();
        var context = observer.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await context.Set<OutboxState>().AnyAsync(s => ids.Contains(s.OutboxId)));
    }

    private static ServiceProvider BuildProvider()
    {
        using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var connectionString = configured.Database.GetConnectionString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new TestTenantContext(Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d")));
        services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(connectionString));
        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<ApplicationDbContext>(o => { o.UsePostgres(); o.UseBusOutbox(); });
            x.UsingInMemory();
        });
        return services.BuildServiceProvider();
    }
}

public sealed class SharedCheckoutConfiguredFactAttribute : FactAttribute
{
    public SharedCheckoutConfiguredFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SHARED_CHECKOUT_CONFIGURED") != "1")
            Skip = "Explicit configured PostgreSQL rollback-only outbox probe opt-in required.";
    }
}
