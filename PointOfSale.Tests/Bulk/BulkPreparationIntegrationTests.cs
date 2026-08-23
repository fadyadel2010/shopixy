using System.Data.Common;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PointOfSale.API.Services.ProductAuthoring.Bulk;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkPreparationIntegrationTests
{
    [Fact]
    public async Task PrepareAsync_LoadsOnceInRequestOrderAndDoesNotMutateOrPersist()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var context = harness.Context;
        var beforeProducts = await context.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(product => product.Id)
            .Select(product => new ProductSnapshot(
                product.Id,
                product.IsActive,
                product.PublishStatus,
                product.IsOnlineVisible,
                product.IsDeleted,
                product.RowVersion))
            .ToListAsync();
        var beforeVariants = await context.ProductVariants
            .AsNoTracking()
            .OrderBy(variant => variant.Id)
            .Select(variant => new VariantSnapshot(
                variant.Id,
                variant.IsActive,
                variant.Price,
                variant.OnlinePrice,
                variant.CostPrice,
                variant.IsDeleted,
                variant.RowVersion))
            .ToListAsync();

        context.ChangeTracker.Clear();
        harness.QueryCounter.Reset();
        var service = new BulkPreparationService(
            new BulkProductRequestValidator(),
            new BulkProductLoader(context, harness.TenantContext),
            new StaticCorrelationIdAccessor("bulk-correlation-1"));
        var request = new BulkProductRequest(
        [
            Item(harness.ForeignProductId, 11),
            Item(harness.ConflictProductId, 99),
            Item(harness.PreparedProductId, 7),
            Item(harness.PreparedProductId, 7),
            Item(harness.DeletedProductId, 13)
        ]);

        var outcome = await service.PrepareAsync(request);

        Assert.True(outcome.IsValid);
        Assert.Equal("bulk-correlation-1", outcome.CorrelationId);
        Assert.Equal(5, outcome.Requested);
        Assert.Equal(4, outcome.Accepted);
        Assert.Equal(
            new[]
            {
                harness.ForeignProductId,
                harness.ConflictProductId,
                harness.PreparedProductId,
                harness.DeletedProductId
            },
            outcome.PreparedItems.Select(item => item.ProductId));
        Assert.Equal(BulkPreparedItemState.NotFound, outcome.PreparedItems[0].State);
        Assert.Null(outcome.PreparedItems[0].CurrentRowVersion);
        Assert.Equal(BulkPreparedItemState.ConcurrencyConflict, outcome.PreparedItems[1].State);
        Assert.Equal(8u, outcome.PreparedItems[1].CurrentRowVersion);
        Assert.Equal(BulkPreparedItemState.Prepared, outcome.PreparedItems[2].State);
        Assert.Equal(7u, outcome.PreparedItems[2].CurrentRowVersion);
        Assert.Equal(BulkPreparedItemState.NotFound, outcome.PreparedItems[3].State);
        Assert.Equal(1, harness.QueryCounter.ProductSelectCount);
        Assert.Empty(context.ChangeTracker.Entries());

        var afterProducts = await context.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(product => product.Id)
            .Select(product => new ProductSnapshot(
                product.Id,
                product.IsActive,
                product.PublishStatus,
                product.IsOnlineVisible,
                product.IsDeleted,
                product.RowVersion))
            .ToListAsync();
        var afterVariants = await context.ProductVariants
            .AsNoTracking()
            .OrderBy(variant => variant.Id)
            .Select(variant => new VariantSnapshot(
                variant.Id,
                variant.IsActive,
                variant.Price,
                variant.OnlinePrice,
                variant.CostPrice,
                variant.IsDeleted,
                variant.RowVersion))
            .ToListAsync();

        Assert.Equal(beforeProducts, afterProducts);
        Assert.Equal(beforeVariants, afterVariants);
        Assert.Equal(0, await context.Set<OutboxMessage>().CountAsync());
        Assert.Equal(0, await context.MarketplaceEventOutboxMessages.CountAsync());
        Assert.Equal(0, await context.ProductRequestIdempotencyRecords.CountAsync());
    }

    [Fact]
    public async Task PrepareAsync_RejectsInvalidRequestWithoutQueryingProducts()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var service = new BulkPreparationService(
            new BulkProductRequestValidator(),
            new BulkProductLoader(harness.Context, harness.TenantContext),
            new StaticCorrelationIdAccessor("bulk-correlation-2"));

        harness.QueryCounter.Reset();
        var outcome = await service.PrepareAsync(new BulkProductRequest([]));

        Assert.False(outcome.IsValid);
        Assert.Equal(BulkErrorCodes.ProductsRequired, Assert.Single(outcome.Errors).Code);
        Assert.Equal(0, harness.QueryCounter.ProductSelectCount);
        Assert.Equal(0, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
    }

    [Fact]
    public async Task Loader_RequiresTenantContextBeforeQuerying()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var missingTenant = new TestTenantContext(null);
        var loader = new BulkProductLoader(harness.Context, missingTenant);

        harness.QueryCounter.Reset();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            loader.LoadProjectionsAsync(new HashSet<Guid> { Guid.NewGuid() }));

        Assert.Equal(0, harness.QueryCounter.ProductSelectCount);
    }

    private static BulkProductRequestItem Item(Guid id, uint version) =>
        new(id.ToString("D"), version.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private sealed record ProductSnapshot(
        Guid Id,
        bool IsActive,
        PublishStatus PublishStatus,
        bool IsOnlineVisible,
        bool IsDeleted,
        uint RowVersion);

    private sealed record VariantSnapshot(
        Guid Id,
        bool IsActive,
        decimal Price,
        decimal? OnlinePrice,
        decimal CostPrice,
        bool IsDeleted,
        uint RowVersion);
}

internal sealed class BulkFoundationTestHarness : IAsyncDisposable
{
    private BulkFoundationTestHarness(
        SqliteConnection connection,
        BulkFoundationDbContext context,
        TestTenantContext tenantContext,
        ProductSelectCountingInterceptor queryCounter,
        Guid preparedProductId,
        Guid conflictProductId,
        Guid foreignProductId,
        Guid deletedProductId)
    {
        Connection = connection;
        Context = context;
        TenantContext = tenantContext;
        QueryCounter = queryCounter;
        PreparedProductId = preparedProductId;
        ConflictProductId = conflictProductId;
        ForeignProductId = foreignProductId;
        DeletedProductId = deletedProductId;
    }

    public SqliteConnection Connection { get; }
    public BulkFoundationDbContext Context { get; }
    public TestTenantContext TenantContext { get; }
    public ProductSelectCountingInterceptor QueryCounter { get; }
    public Guid PreparedProductId { get; }
    public Guid ConflictProductId { get; }
    public Guid ForeignProductId { get; }
    public Guid DeletedProductId { get; }

    public static BulkFoundationTestHarness Create(bool seedProducts, string? connectionString = null)
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        var queryCounter = new ProductSelectCountingInterceptor();
        var connection = new SqliteConnection(connectionString ?? "Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(queryCounter)
            .Options;
        var context = new BulkFoundationDbContext(options, tenantContext);
        context.Database.EnsureCreated();

        var preparedProductId = Guid.NewGuid();
        var conflictProductId = Guid.NewGuid();
        var foreignProductId = Guid.NewGuid();
        var deletedProductId = Guid.NewGuid();

        if (seedProducts)
        {
            context.Products.AddRange(
                Product(preparedProductId, tenantId, 6, isActive: true),
                Product(conflictProductId, tenantId, 7, isActive: false),
                Product(foreignProductId, Guid.NewGuid(), 10, isActive: true),
                Product(deletedProductId, tenantId, 12, isActive: true, isDeleted: true));
            context.ProductVariants.Add(new ProductVariant
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProductId = preparedProductId,
                SKU = "BULK-VARIANT",
                Price = 20m,
                OnlinePrice = 18m,
                CostPrice = 9m,
                IsActive = true,
                IsDefault = true,
                RowVersion = 21
            });
            context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        queryCounter.Reset();
        return new BulkFoundationTestHarness(
            connection,
            context,
            tenantContext,
            queryCounter,
            preparedProductId,
            conflictProductId,
            foreignProductId,
            deletedProductId);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await Connection.DisposeAsync();
    }

    private static Product Product(
        Guid id,
        Guid tenantId,
        uint rowVersion,
        bool isActive,
        bool isDeleted = false) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            CategoryId = Guid.NewGuid(),
            Name = $"Product {id:N}",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            IsActive = isActive,
            PublishStatus = PublishStatus.Draft,
            IsOnlineVisible = false,
            IsDeleted = isDeleted,
            RowVersion = rowVersion
        };
}

internal sealed class BulkFoundationDbContext : ApplicationDbContext
{
    private readonly Guid? _tenantId;

    public BulkFoundationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContext tenantContext)
        : base(options, tenantContext)
    {
        _tenantId = tenantContext.TenantId;
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
            typeof(OutboxMessage),
            typeof(MarketplaceEventOutboxMessage),
            typeof(ProductRequestIdempotency)
        };
        var discoveredEntityTypes = typeof(ApplicationDbContext)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property =>
                property.PropertyType.IsGenericType &&
                property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(property => property.PropertyType.GetGenericArguments()[0])
            .Where(entityType => !keepTypes.Contains(entityType))
            .Distinct();

        foreach (var entityType in discoveredEntityTypes)
        {
            modelBuilder.Ignore(entityType);
        }

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(product => product.Id);
            entity.HasQueryFilter(product => !product.IsDeleted && product.TenantId == _tenantId);
            entity.Ignore(product => product.Category);
            entity.Ignore(product => product.Variants);
            entity.Ignore(product => product.Media);
            entity.Ignore(product => product.WarehouseStocks);
            entity.Ignore(product => product.ProductUnits);
        });
        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(category => category.Id);
            entity.Ignore(category => category.ParentCategory);
            entity.Ignore(category => category.SubCategories);
            entity.Ignore(category => category.Products);
        });
        modelBuilder.Entity<PointOfSale.Core.Entities.Unit>(entity =>
        {
            entity.ToTable("Units");
            entity.HasKey(unit => unit.Id);
        });
        modelBuilder.Entity<ProductUnit>(entity =>
        {
            entity.ToTable("ProductUnits");
            entity.HasKey(productUnit => productUnit.Id);
            entity.HasOne(productUnit => productUnit.Product)
                .WithMany()
                .HasForeignKey(productUnit => productUnit.ProductId);
            entity.HasOne(productUnit => productUnit.Unit)
                .WithMany()
                .HasForeignKey(productUnit => productUnit.UnitId);
        });
        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.ToTable("ProductVariants");
            entity.HasKey(variant => variant.Id);
            entity.Ignore(variant => variant.Product);
            entity.Ignore(variant => variant.Media);
            entity.Ignore(variant => variant.Stocks);
        });
        modelBuilder.Entity<ProductVariantAttribute>(entity =>
        {
            entity.ToTable("ProductVariantAttributes");
            entity.HasKey(attribute => attribute.Id);
            entity.HasOne(attribute => attribute.Variant)
                .WithMany(variant => variant.Attributes)
                .HasForeignKey(attribute => attribute.VariantId);
        });
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(audit => audit.LogId);
            entity.Property(audit => audit.LogId).ValueGeneratedOnAdd();
        });
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(message => message.Id);
        });
        modelBuilder.Entity<MarketplaceEventOutboxMessage>(entity =>
        {
            entity.ToTable("MarketplaceEventOutboxMessages");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.RowVersion).HasDefaultValue(1u);
        });
        modelBuilder.Entity<ProductRequestIdempotency>(entity =>
        {
            entity.ToTable("ProductRequestIdempotency");
            entity.HasKey(record => record.Id);
            entity.HasIndex(record => new { record.TenantId, record.Key, record.Operation }).IsUnique();
            entity.Property(record => record.Key).HasMaxLength(200).IsRequired();
            entity.Property(record => record.Operation).HasMaxLength(32).IsRequired();
            entity.Property(record => record.RequestFingerprint).HasMaxLength(64).IsRequired();
        });
    }
}

internal sealed class TestTenantContext(Guid? tenantId) : ITenantContext
{
    public Guid? TenantId => tenantId;
}

internal sealed class StaticCorrelationIdAccessor(string correlationId) : ICorrelationIdAccessor
{
    public string GetCorrelationId() => correlationId;
}

internal sealed class ProductSelectCountingInterceptor : DbCommandInterceptor
{
    private int _productSelectCount;

    public int ProductSelectCount => Volatile.Read(ref _productSelectCount);

    public void Reset() => Interlocked.Exchange(ref _productSelectCount, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        CountProductSelect(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        CountProductSelect(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void CountProductSelect(DbCommand command)
    {
        if (command.CommandText.Contains("FROM \"Products\"", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _productSelectCount);
        }
    }
}
