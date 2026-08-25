using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MassTransit.Testing;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.EventBus;
using MassTransitOutboxMessage = MassTransit.EntityFrameworkCoreIntegration.OutboxMessage;

namespace PointOfSale.Tests.Infrastructure;

public sealed class MassTransitEventPublisherRuntimeTypeTests
{
    [Fact]
    public async Task GlobalAffiliateCategoryEvent_IsTenantlessAndAtomicWithSourceMutation()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = $"publisher-global-category-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";

        await using var rootConnection = new SqliteConnection(connectionString);
        await rootConnection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new TestTenantContext(tenantId));
        services.AddScoped<ApplicationDbContext>(_ => new PublisherOutboxDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connectionString)
                .Options,
            new TestTenantContext(tenantId)));
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
        services.AddMassTransitTestHarness(configurator =>
        {
            configurator.AddEntityFrameworkOutbox<ApplicationDbContext>(outbox =>
            {
                outbox.UseSqlite();
                outbox.UseBusOutbox();
            });
        });

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            await setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Database.EnsureCreatedAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            var category = AffiliateCategory.CreateMainCategory("Electronics", "electronics");
            dbContext.AffiliateCategories.Add(category);
            var categoryEvent = new AffiliateCategoryUpdatedEvent
            {
                CategoryId = category.Id,
                NewName = category.Name,
                NewSlug = category.Slug
            };

            await publisher.PublishAsync(categoryEvent);

            Assert.Null(categoryEvent.TenantId);
            Assert.IsAssignableFrom<IGlobalPlatformIntegrationEvent>(categoryEvent);
            Assert.Equal(EntityState.Added, dbContext.Entry(category).State);
            Assert.Contains(
                dbContext.ChangeTracker.Entries(),
                entry => entry.Entity is MassTransitOutboxMessage && entry.State == EntityState.Added);

            await dbContext.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await verificationContext.AffiliateCategories.ToListAsync());
        Assert.Empty(await verificationContext.Set<MassTransitOutboxMessage>().ToListAsync());
    }

    [Fact]
    public async Task ProductUpdatedEvent_PreservesConcreteContract_ThroughEfBusOutboxDispatch()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var databaseName = $"publisher-runtime-type-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";

        await using var rootConnection = new SqliteConnection(connectionString);
        await rootConnection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new TestTenantContext(tenantId));
        services.AddScoped<ApplicationDbContext>(_ => new PublisherOutboxDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connectionString)
                .Options,
            new TestTenantContext(tenantId)));
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
        services.AddMassTransitTestHarness(configurator =>
        {
            configurator.AddConsumer<ProductUpdatedProbeConsumer>();
            configurator.AddEntityFrameworkOutbox<ApplicationDbContext>(outbox =>
            {
                outbox.UseSqlite();
                outbox.UseBusOutbox();
                outbox.QueryDelay = TimeSpan.FromMilliseconds(25);
            });
        });

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var harness = provider.GetRequiredService<ITestHarness>();

        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupContext = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await setupContext.Database.EnsureCreatedAsync();
        }

        await harness.Start();
        try
        {
            await using var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            var product = new Product
            {
                Id = productId,
                TenantId = tenantId,
                Name = "Concrete contract probe"
            };
            product.AdvanceProjectionMutationVersion(1);
            dbContext.Attach(product);

            await publisher.PublishAsync(new ProductUpdatedEvent
            {
                TenantId = tenantId,
                EventVersion = 1,
                IdempotencyKey = $"product-updated:{productId:N}:1",
                CorrelationId = Guid.NewGuid().ToString("N"),
                ProductId = productId,
                ChangedFields = ["Name"],
                Name = "Concrete contract probe"
            });
            await dbContext.SaveChangesAsync();

            var stagedMessage = await dbContext.Set<MassTransitOutboxMessage>().SingleAsync();
            Assert.Contains(
                "urn:message:PointOfSale.Core.Events:ProductUpdatedEvent",
                stagedMessage.MessageType,
                StringComparison.Ordinal);

            await transaction.CommitAsync();

            Assert.True(await harness.Consumed.Any<ProductUpdatedEvent>());
        }
        finally
        {
            await harness.Stop();
        }
    }

    private sealed class ProductUpdatedProbeConsumer : IConsumer<ProductUpdatedEvent>
    {
        public Task Consume(ConsumeContext<ProductUpdatedEvent> context) => Task.CompletedTask;
    }

    private sealed class PublisherOutboxDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContext tenantContext)
        : ApplicationDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var excludedEntityTypes = typeof(ApplicationDbContext)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property =>
                    property.PropertyType.IsGenericType &&
                    property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                .Select(property => property.PropertyType.GetGenericArguments()[0])
                .Where(entityType => entityType != typeof(Product) && entityType != typeof(AffiliateCategory))
                .Distinct();

            foreach (var entityType in excludedEntityTypes)
            {
                modelBuilder.Ignore(entityType);
            }

            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasKey(product => product.Id);
                entity.Ignore(product => product.Category);
                entity.Ignore(product => product.Variants);
                entity.Ignore(product => product.Media);
                entity.Ignore(product => product.WarehouseStocks);
                entity.Ignore(product => product.ProductUnits);
            });

            modelBuilder.Entity<AffiliateCategory>(entity => entity.HasKey(category => category.Id));

            modelBuilder.AddInboxStateEntity();
            modelBuilder.AddOutboxMessageEntity();
            modelBuilder.AddOutboxStateEntity();
        }
    }
}
