using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Integration;

public sealed class ProductAuthoringProducerCompletenessIntegrationTests
{
    [Fact]
    public async Task PublicCreate_WithoutProductUnits_IsRejectedAndDoesNotCommit()
    {
        await using var harness = CreateHarness();
        var command = harness.CreateCommand(
            ProductAuthoringOperation.Create,
            PublishStatus.Published,
            isOnlineVisible: true,
            hasVariants: false,
            units: [],
            metadata: Metadata(productUnitsSubmitted: false, variantsSubmitted: false));

        var result = await harness.Orchestrator.AuthorAsync(command);

        Assert.Equal(ProductAuthoringStatus.NotReadyForPublication, result.Status);
        Assert.Contains(result.ValidationErrors, error => error.Code == "PRODUCT_UNITS_REQUIRED");
        Assert.Empty(await harness.Context.Products.ToListAsync());
        Assert.DoesNotContain(harness.EventPublisher.Events, item => item is ProductCreatedEvent);
    }

    [Fact]
    public async Task DraftCreate_WithoutProductUnits_IsAccepted()
    {
        await using var harness = CreateHarness();
        var command = harness.CreateCommand(
            ProductAuthoringOperation.Create,
            PublishStatus.Draft,
            isOnlineVisible: true,
            hasVariants: false,
            units: [],
            metadata: Metadata(productUnitsSubmitted: false, variantsSubmitted: false));

        var result = await harness.Orchestrator.AuthorAsync(command);

        Assert.Equal(ProductAuthoringStatus.Accepted, result.Status);
        Assert.Single(await harness.Context.Products.ToListAsync());
        Assert.Empty(await harness.Context.ProductUnits.ToListAsync());
    }

    [Fact]
    public async Task PublicUpdate_OmittingProductUnits_ValidatesReconstructedAggregateAndPreservesUnits()
    {
        await using var harness = CreateHarness();
        var product = await harness.CreatePublicSimpleProductAsync();
        var existingUnit = await harness.Context.ProductUnits.SingleAsync(unit => unit.ProductId == product.Id);
        harness.Context.ChangeTracker.Clear();

        var command = harness.CreateCommand(
            ProductAuthoringOperation.Update,
            PublishStatus.Published,
            isOnlineVisible: true,
            hasVariants: false,
            productId: product.Id,
            units: [],
            metadata: Metadata(productUnitsSubmitted: false, variantsSubmitted: false));

        var result = await harness.Orchestrator.AuthorAsync(command);

        Assert.Equal(ProductAuthoringStatus.Accepted, result.Status);
        var committedUnit = await harness.Context.ProductUnits.SingleAsync(unit => unit.ProductId == product.Id);
        Assert.Equal(existingUnit.Id, committedUnit.Id);
        Assert.True(committedUnit.IsBaseUnit);
        Assert.True(committedUnit.IsDefault);
        Assert.Contains(harness.EventPublisher.Events.OfType<ProductUpdatedEvent>(), @event =>
            @event.ProductId == product.Id &&
            @event.DefaultUnit != null &&
            @event.Units.Count == 1);
    }

    [Fact]
    public async Task PublicUpdate_RemovingDefaultUnit_IsRejectedAndDoesNotCommit()
    {
        await using var harness = CreateHarness();
        var product = await harness.CreatePublicSimpleProductAsync();
        var existingUnit = await harness.Context.ProductUnits.SingleAsync(unit => unit.ProductId == product.Id);
        harness.Context.ChangeTracker.Clear();

        var command = harness.CreateCommand(
            ProductAuthoringOperation.Update,
            PublishStatus.Published,
            isOnlineVisible: true,
            hasVariants: false,
            productId: product.Id,
            units:
            [
                new ProductAuthoringUnit(existingUnit.Id, existingUnit.UnitId, 1m, 5m, 12m, null, IsDefault: false, IsBaseUnit: true)
            ],
            metadata: Metadata(productUnitsSubmitted: true, variantsSubmitted: false));

        var result = await harness.Orchestrator.AuthorAsync(command);

        Assert.Equal(ProductAuthoringStatus.NotReadyForPublication, result.Status);
        Assert.Contains(result.ValidationErrors, error => error.Code == "PRODUCT_DEFAULT_UNIT_REQUIRED");
        var committedUnit = await harness.Context.ProductUnits.SingleAsync(unit => unit.Id == existingUnit.Id);
        Assert.True(committedUnit.IsDefault);
    }

    [Fact]
    public async Task PublicUpdate_RemovingBaseUnit_IsRejectedAndDoesNotCommit()
    {
        await using var harness = CreateHarness();
        var product = await harness.CreatePublicSimpleProductAsync();
        var existingUnit = await harness.Context.ProductUnits.SingleAsync(unit => unit.ProductId == product.Id);
        harness.Context.ChangeTracker.Clear();

        var command = harness.CreateCommand(
            ProductAuthoringOperation.Update,
            PublishStatus.Published,
            isOnlineVisible: true,
            hasVariants: false,
            productId: product.Id,
            units:
            [
                new ProductAuthoringUnit(existingUnit.Id, existingUnit.UnitId, 1m, 5m, 12m, null, IsDefault: true, IsBaseUnit: false)
            ],
            metadata: Metadata(productUnitsSubmitted: true, variantsSubmitted: false));

        var result = await harness.Orchestrator.AuthorAsync(command);

        Assert.Equal(ProductAuthoringStatus.NotReadyForPublication, result.Status);
        Assert.Contains(result.ValidationErrors, error => error.Code == "PRODUCT_BASE_UNIT_REQUIRED");
        var committedUnit = await harness.Context.ProductUnits.SingleAsync(unit => unit.Id == existingUnit.Id);
        Assert.True(committedUnit.IsBaseUnit);
    }

    [Fact]
    public async Task PublicCreate_WithCompleteSimpleUnits_PublishesCompleteProductEvent()
    {
        await using var harness = CreateHarness();
        var product = await harness.CreatePublicSimpleProductAsync();

        var createdEvent = Assert.Single(harness.EventPublisher.Events.OfType<ProductCreatedEvent>());
        Assert.Equal(product.Id, createdEvent.ProductId);
        Assert.NotNull(createdEvent.DefaultUnit);
        Assert.True(createdEvent.DefaultUnit!.SalePrice > 0m);
        Assert.Contains(createdEvent.Units, unit => unit.UnitId == createdEvent.DefaultUnit.UnitId);
    }

    [Fact]
    public async Task OriginalProductNotForSaleRegression_PublicCreateOmittingUnitsCannotReachProjection()
    {
        await using var harness = CreateHarness();
        var command = harness.CreateCommand(
            ProductAuthoringOperation.Create,
            PublishStatus.Published,
            isOnlineVisible: true,
            hasVariants: false,
            units: [],
            metadata: Metadata(productUnitsSubmitted: false, variantsSubmitted: false));

        var result = await harness.Orchestrator.AuthorAsync(command);

        Assert.Equal(ProductAuthoringStatus.NotReadyForPublication, result.Status);
        Assert.Empty(await harness.Context.Products.ToListAsync());
        Assert.DoesNotContain(harness.EventPublisher.Events.OfType<ProductCreatedEvent>(), @event =>
            @event.DefaultUnit is null || @event.Units.Count == 0);
    }

    [Fact]
    public async Task PublicVariantCreate_WithoutActivePricedVariant_IsRejected()
    {
        await using var harness = CreateHarness();
        var command = harness.CreateCommand(
            ProductAuthoringOperation.Create,
            PublishStatus.Published,
            isOnlineVisible: true,
            hasVariants: true,
            variants:
            [
                new ProductAuthoringVariant(Guid.NewGuid(), "VAR-1", null, 0m, 0m, 0m, IsActive: true, IsDefault: true, new Dictionary<string, string>())
            ],
            metadata: Metadata(productUnitsSubmitted: false, variantsSubmitted: true));

        var result = await harness.Orchestrator.AuthorAsync(command);

        Assert.Equal(ProductAuthoringStatus.NotReadyForPublication, result.Status);
        Assert.Contains(result.ValidationErrors, error => error.Code == "PRODUCT_ACTIVE_PRICED_VARIANT_REQUIRED");
        Assert.Empty(await harness.Context.Products.ToListAsync());
    }

    private static ProductAuthoringMetadata Metadata(
        bool productUnitsSubmitted,
        bool variantsSubmitted,
        string? idempotencyKey = null) =>
        new(
            false,
            null,
            null,
            "default",
            Guid.NewGuid().ToString("N"),
            idempotencyKey ?? $"producer-completeness-{Guid.NewGuid():N}",
            null,
            new Dictionary<string, string>
            {
                ["productUnitsSubmitted"] = productUnitsSubmitted ? "true" : "false",
                ["variantsSubmitted"] = variantsSubmitted ? "true" : "false"
            });

    private static Harness CreateHarness()
    {
        var tenantId = Guid.NewGuid();
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .EnableDetailedErrors()
            .Options;
        var tenantContext = new TestTenantContext(tenantId);
        var context = new ProductAuthoringProducerCompletenessDbContext(options, tenantContext);
        context.Database.EnsureCreated();
        var eventPublisher = new RecordingEventPublisher();
        var variantService = new ProductVariantService(
            context,
            eventPublisher,
            NullLogger<ProductVariantService>.Instance);
        var orchestrator = new ProductAuthoringOrchestrator(
            new ProductAuthoringPolicy(new ProductAuthoringValidator()),
            context,
            tenantContext,
            variantService,
            eventPublisher,
            new RecordingMarketplaceEventOutbox(),
            NullLogger<ProductAuthoringOrchestrator>.Instance,
            new HttpContextAccessor(),
            new ProductMutationIdempotencyService(context),
            NoOpProductAuthoringTransactionObserver.Instance,
            new ProductAuthoringReconstructionService(context, tenantContext));

        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Products"
        };
        var unit = new PointOfSale.Core.Entities.Unit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Piece",
            Symbol = "pc",
            IsActive = true
        };

        context.Categories.Add(category);
        context.Units.Add(unit);
        context.SaveChanges();

        return new Harness(connection, context, tenantContext, orchestrator, eventPublisher, category.Id, unit.Id);
    }

    private sealed record Harness(
        SqliteConnection Connection,
        ProductAuthoringProducerCompletenessDbContext Context,
        TestTenantContext TenantContext,
        ProductAuthoringOrchestrator Orchestrator,
        RecordingEventPublisher EventPublisher,
        Guid CategoryId,
        Guid UnitId) : IAsyncDisposable
    {
        public async Task<Product> CreatePublicSimpleProductAsync()
        {
            var command = CreateCommand(
                ProductAuthoringOperation.Create,
                PublishStatus.Published,
                isOnlineVisible: true,
                hasVariants: false,
                units:
                [
                    new ProductAuthoringUnit(null, UnitId, 1m, 5m, 12m, null, IsDefault: true, IsBaseUnit: true)
                ],
                metadata: Metadata(productUnitsSubmitted: true, variantsSubmitted: false));

            var result = await Orchestrator.AuthorAsync(command);

            Assert.Equal(ProductAuthoringStatus.Accepted, result.Status);
            Assert.NotNull(result.ProductId);
            return await Context.Products.SingleAsync(product => product.Id == result.ProductId.Value);
        }

        public ProductAuthoringCommand CreateCommand(
            ProductAuthoringOperation operation,
            PublishStatus publishStatus,
            bool isOnlineVisible,
            bool hasVariants,
            Guid? productId = null,
            IReadOnlyList<ProductAuthoringUnit>? units = null,
            IReadOnlyList<ProductAuthoringVariant>? variants = null,
            ProductAuthoringMetadata? metadata = null)
        {
            var unitItems = units ?? Array.Empty<ProductAuthoringUnit>();

            return new ProductAuthoringCommand(
                operation,
                new ProductAuthoringIdentity(TenantContext.TenantId!.Value, productId, CategoryId, "SKU-1", "BAR-1"),
                new ProductAuthoringContent("Test product", null, null, null, null, null, [], [], []),
                new ProductAuthoringPricing(5m, 12m, 12m, false, 0m),
                new ProductAuthoringPublication(publishStatus, true, isOnlineVisible, true, false),
                new ProductAuthoringSeo(null, null, null, null),
                new ProductAuthoringShippingProfile(0m, 0m, 0m, 0m, "kg", "cm"),
                new ProductAuthoringInventoryProfile("Piece", 0m, true, hasVariants, 10, false),
                new ProductAuthoringUnits(
                    unitItems.FirstOrDefault(unit => unit.IsBaseUnit)?.UnitId,
                    unitItems.FirstOrDefault(unit => unit.IsDefault)?.UnitId,
                    unitItems),
                variants ?? Array.Empty<ProductAuthoringVariant>(),
                metadata ?? Metadata(productUnitsSubmitted: units is not null, variantsSubmitted: variants is not null));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class ProductAuthoringProducerCompletenessDbContext : ApplicationDbContext
    {
        public ProductAuthoringProducerCompletenessDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext tenantContext)
            : base(options, tenantContext)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var keepTypes = new HashSet<Type>
            {
                typeof(Category),
                typeof(Product),
                typeof(PointOfSale.Core.Entities.Unit),
                typeof(ProductUnit),
                typeof(ProductVariant),
                typeof(ProductVariantAttribute),
                typeof(AuditLog),
                typeof(PointOfSale.Core.Entities.OutboxMessage),
                typeof(ProductRequestIdempotency)
            };

            var discoveredEntityTypes = typeof(ApplicationDbContext)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property =>
                    property.PropertyType.IsGenericType &&
                    property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                .Select(property => property.PropertyType.GetGenericArguments()[0])
                .Where(entityType => !keepTypes.Contains(entityType))
                .Distinct()
                .ToArray();

            foreach (var entityType in discoveredEntityTypes)
            {
                modelBuilder.Ignore(entityType);
            }

            modelBuilder.Entity<Category>(entity =>
            {
                entity.ToTable("Categories");
                entity.HasKey(category => category.Id);
                entity.Property(category => category.Name).IsRequired();
                entity.Ignore(category => category.ParentCategory);
                entity.Ignore(category => category.SubCategories);
                entity.Ignore(category => category.Products);
            });

            modelBuilder.Entity<PointOfSale.Core.Entities.Unit>(entity =>
            {
                entity.ToTable("Units");
                entity.HasKey(unit => unit.Id);
                entity.Property(unit => unit.Name).IsRequired();
                entity.Property(unit => unit.Symbol).IsRequired();
            });

            modelBuilder.Entity<Product>(entity =>
            {
                entity.ToTable("Products");
                entity.HasKey(product => product.Id);
                entity.Property(product => product.Name).IsRequired();
                entity.Property(product => product.Unit).IsRequired();
                entity.Property(product => product.WeightUnit).IsRequired();
                entity.Property(product => product.DimensionUnit).IsRequired();
                entity.HasOne(product => product.Category)
                    .WithMany()
                    .HasForeignKey(product => product.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.Ignore(product => product.Media);
                entity.Ignore(product => product.WarehouseStocks);
            });

            modelBuilder.Entity<ProductUnit>(entity =>
            {
                entity.ToTable("ProductUnits");
                entity.HasKey(productUnit => productUnit.Id);
                entity.HasOne(productUnit => productUnit.Product)
                    .WithMany(product => product.ProductUnits)
                    .HasForeignKey(productUnit => productUnit.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(productUnit => productUnit.Unit)
                    .WithMany()
                    .HasForeignKey(productUnit => productUnit.UnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ProductVariant>(entity =>
            {
                entity.ToTable("ProductVariants");
                entity.HasKey(variant => variant.Id);
                entity.HasOne(variant => variant.Product)
                    .WithMany(product => product.Variants)
                    .HasForeignKey(variant => variant.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.Ignore(variant => variant.Stocks);
                entity.Ignore(variant => variant.Media);
            });

            modelBuilder.Entity<ProductVariantAttribute>(entity =>
            {
                entity.ToTable("ProductVariantAttributes");
                entity.HasKey(attribute => attribute.Id);
                entity.HasOne(attribute => attribute.Variant)
                    .WithMany(variant => variant.Attributes)
                    .HasForeignKey(attribute => attribute.VariantId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("AuditLogs");
                entity.HasKey(audit => audit.LogId);
                entity.Property(audit => audit.LogId).ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<PointOfSale.Core.Entities.OutboxMessage>(entity =>
            {
                entity.ToTable("OutboxMessages");
                entity.HasKey(message => message.Id);
                entity.Property(message => message.EventType).IsRequired();
                entity.Property(message => message.EventPayload).IsRequired();
            });

            modelBuilder.Entity<ProductRequestIdempotency>(entity =>
            {
                entity.ToTable("ProductRequestIdempotency");
                entity.HasKey(record => record.Id);
                entity.HasIndex(record => new { record.TenantId, record.Key, record.Operation }).IsUnique();
                entity.Property(record => record.Key).IsRequired();
                entity.Property(record => record.Operation).IsRequired();
                entity.Property(record => record.RequestFingerprint).IsRequired();
            });
        }
    }

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<object> Events { get; } = new();

        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            Events.AddRange(events.Cast<object>());
            return Task.CompletedTask;
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingMarketplaceEventOutbox : IMarketplaceEventOutboxService
    {
        public Task EnqueueAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, PointOfSale.Core.Events.Marketplace.IMarketplaceDomainEvent =>
            Task.CompletedTask;
    }
}
