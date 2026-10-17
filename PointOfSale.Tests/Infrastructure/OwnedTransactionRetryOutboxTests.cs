using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.EventBus;
using PointOfSale.Infrastructure.MultiTenancy;
using BusOutboxMessage = MassTransit.EntityFrameworkCoreIntegration.OutboxMessage;

namespace PointOfSale.Tests.Infrastructure;

/// <summary>
/// Actual MassTransit scoped bus-outbox staging and EF state transitions only.
/// No SaveChanges, database creation, SQL connection, broker or hosted service.
/// </summary>
public sealed class OwnedTransactionRetryOutboxTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RolledBackPublish_RetryKeepsCachedParentTracked_AndDiscardsOnlyFailedMessages(bool savedBeforeRollback)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var retry = OwnedTransactionRetryState.Capture(db);
        db.Add(new Product { Name = "synthetic failed-attempt source" });
        await publisher.PublishAsync((object)new ShipmentStatusChangedProbe { Attempt = 1 });
        var parent = Assert.Single(db.ChangeTracker.Entries<OutboxState>());
        var failedMessage = Assert.Single(db.ChangeTracker.Entries<BusOutboxMessage>()).Entity;
        if (savedBeforeRollback)
        {
            // SaveChanges accepted its entries, but the caller's outer transaction
            // rolled back. No rows are written by this offline fixture.
            foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Unchanged;
        }

        retry.ClearRolledBackAttempt();
        db.Add(new Product { Name = "synthetic retry source" });
        await publisher.PublishAsync((object)new ShipmentStatusChangedProbe { Attempt = 2 });
        var message = Assert.Single(db.ChangeTracker.Entries<BusOutboxMessage>());
        Assert.NotSame(failedMessage, message.Entity);
        var stagedParent = Assert.Single(db.ChangeTracker.Entries<OutboxState>(), entry => entry.Entity.OutboxId == message.Entity.OutboxId);
        Assert.Same(parent.Entity, stagedParent.Entity);
        Assert.Equal(EntityState.Added, stagedParent.State);
        Assert.Equal(EntityState.Added, parent.State); // the actual cached EntityEntry, not a replacement entry
        Assert.DoesNotContain(db.ChangeTracker.Entries<Product>(), entry => entry.Entity.Name.Contains("failed-attempt"));
        Assert.Contains("ShipmentStatusChangedProbe", message.Entity.MessageType);

        // A second rollback must not lose the parent or retain earlier messages.
        retry.ClearRolledBackAttempt();
        db.Add(new Product { Name = "synthetic final retry source" });
        await publisher.PublishAsync((object)new ShipmentStatusChangedProbe { Attempt = 3 });
        var last = Assert.Single(db.ChangeTracker.Entries<BusOutboxMessage>());
        Assert.Equal(parent.Entity.OutboxId, last.Entity.OutboxId);
        Assert.Equal(EntityState.Added, parent.State);
    }

    [Fact]
    public async Task CommittedParent_IsNotReinserted_AndNextPublishUsesANewParent()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        db.Add(new Product { Name = "synthetic previously committed source" });
        await publisher.PublishAsync((object)new ShipmentStatusChangedProbe { Attempt = 0 });
        var previous = Assert.Single(db.ChangeTracker.Entries<OutboxState>());
        foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Unchanged;
        var retry = OwnedTransactionRetryState.Capture(db);

        db.Add(new Product { Name = "synthetic failed source" });
        await publisher.PublishAsync((object)new ShipmentStatusChangedProbe { Attempt = 1 });
        var failedParent = Assert.Single(db.ChangeTracker.Entries<OutboxState>(), entry => entry.State == EntityState.Added);
        retry.ClearRolledBackAttempt();
        db.Add(new Product { Name = "synthetic retried source" });
        await publisher.PublishAsync((object)new ShipmentStatusChangedProbe { Attempt = 2 });
        var current = Assert.Single(db.ChangeTracker.Entries<BusOutboxMessage>());
        Assert.NotEqual(previous.Entity.OutboxId, current.Entity.OutboxId);
        Assert.Equal(failedParent.Entity.OutboxId, current.Entity.OutboxId);
        Assert.Equal(EntityState.Unchanged, previous.State);
        Assert.Equal(EntityState.Added, failedParent.State);
    }

    [Fact]
    public async Task PendingCallerWork_IsRejectedWithoutDetachingOrChangingIt()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var caller = db.Add(new Product { Name = "caller mutation must survive" });
        Assert.Throws<InvalidOperationException>(() => OwnedTransactionRetryState.Capture(db));
        Assert.Equal(EntityState.Added, caller.State);
        Assert.Same(caller.Entity, Assert.Single(db.ChangeTracker.Entries<Product>()).Entity);
    }

    [Fact]
    public void CallerOwnedTransaction_IsRejectedBeforeTouchingTheTrackerOrTransaction()
    {
        var db = new Mock<DbContext>();
        var transaction = new Mock<IDbContextTransaction>(MockBehavior.Strict);
        var database = new Mock<DatabaseFacade>(db.Object);
        database.SetupGet(facade => facade.CurrentTransaction).Returns(transaction.Object);
        db.SetupGet(context => context.Database).Returns(database.Object);
        Assert.Throws<InvalidOperationException>(() => OwnedTransactionRetryState.Capture(db.Object));
        db.VerifyGet(context => context.ChangeTracker, Times.Never);
        transaction.VerifyNoOtherCalls();
    }

    private static ServiceProvider CreateProvider()
    {
        var tenant = new TenantContext(); tenant.SetTenantId(Guid.NewGuid());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenant);
        services.AddScoped<ApplicationDbContext>(_ => new StagingContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"outbox-staging-only-{Guid.NewGuid():N}").Options, tenant));
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
        services.AddMassTransitTestHarness(bus => bus.AddEntityFrameworkOutbox<ApplicationDbContext>(outbox =>
        {
            outbox.UsePostgres();
            outbox.UseBusOutbox(delivery => delivery.DisableDeliveryService());
        }));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class StagingContext(DbContextOptions<ApplicationDbContext> options, ITenantContext tenant)
        : ApplicationDbContext(options, tenant)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            foreach (var property in typeof(ApplicationDbContext).GetProperties())
            {
                if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                    builder.Ignore(property.PropertyType.GetGenericArguments()[0]);
            }
            builder.Entity<Product>().HasKey(product => product.Id);
            foreach (var navigation in builder.Entity<Product>().Metadata.GetNavigations().ToList())
                builder.Entity<Product>().Ignore(navigation.Name);
            builder.AddInboxStateEntity(); builder.AddOutboxMessageEntity(); builder.AddOutboxStateEntity();
        }
    }
}

public sealed class ShipmentStatusChangedProbe
{
    public int Attempt { get; set; }
}
