using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MongoDB.Driver;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Pricing;
using PointOfSale.API.Services.Promotions;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Consumers.Affiliate;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ProductInventorySqlAuthorityFallbackTests
{
    [Fact]
    public async Task ProductSearchSqlAuthorityOverlay_IgnoresInactiveWarehouseStockLineage()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var activeWarehouseId = Guid.NewGuid();
        var inactiveWarehouseId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Tenants.Add(CreateActiveTenant(tenantId));
        db.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Overlay product",
            CategoryId = Guid.Empty,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            HasVariants = true,
            TrackStock = true,
            CreatedAt = DateTime.UtcNow
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = "OVERLAY-SKU",
            Price = 10m,
            CostPrice = 5m,
            IsActive = true,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        });
        db.Warehouses.AddRange(
            new Warehouse
            {
                Id = activeWarehouseId,
                TenantId = tenantId,
                Name = "Active",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Warehouse
            {
                Id = inactiveWarehouseId,
                TenantId = tenantId,
                Name = "Inactive",
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            });
        db.ProductVariantStocks.AddRange(
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantId,
                WarehouseId = activeWarehouseId,
                Quantity = 4m,
                ReservedQuantity = 1m,
                CreatedAt = DateTime.UtcNow
            },
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantId,
                WarehouseId = inactiveWarehouseId,
                Quantity = 9m,
                ReservedQuantity = 0m,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var overlayProduct = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            db,
            new ProductDocument
            {
                TenantId = tenantId,
                ProductId = productId
            },
            tenantId,
            CancellationToken.None);

        Assert.NotNull(overlayProduct);
        var variant = Assert.Single(overlayProduct!.Variants);
        Assert.Equal(3m, variant.TotalStock);
        Assert.Equal(3, overlayProduct.StockQuantity);
    }

    [Fact]
    public async Task ProductVariantReadRepository_SqlFallback_IgnoresInactiveWarehouseStockLineage()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var activeWarehouseId = Guid.NewGuid();
        var inactiveWarehouseId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Tenants.Add(CreateActiveTenant(tenantId));
        db.Products.Add(CreateVisibleProduct(tenantId, productId));
        db.ProductVariants.Add(CreateVisibleVariant(tenantId, productId, variantId, isDefault: true));
        db.Warehouses.AddRange(
            new Warehouse
            {
                Id = activeWarehouseId,
                TenantId = tenantId,
                Name = "Active",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Warehouse
            {
                Id = inactiveWarehouseId,
                TenantId = tenantId,
                Name = "Inactive",
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            });
        db.ProductVariantStocks.AddRange(
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantId,
                WarehouseId = activeWarehouseId,
                Quantity = 6m,
                ReservedQuantity = 2m,
                CreatedAt = DateTime.UtcNow
            },
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantId,
                WarehouseId = inactiveWarehouseId,
                Quantity = 7m,
                ReservedQuantity = 0m,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var repository = CreateRepository(db);
        var result = await InvokePrivateAsync<(ProductDocument? Product, ProductVariantDocument? Variant)>(
            repository,
            "LoadVariantFromSqlAsync",
            tenantId,
            variantId,
            CancellationToken.None);

        Assert.NotNull(result.Product);
        Assert.NotNull(result.Variant);
        Assert.Equal(4m, result.Variant!.TotalStock);
        Assert.Equal(4, result.Product!.StockQuantity);
    }

    [Fact]
    public async Task ProductVariantReadRepository_SqlFallback_DefaultVariantSkipsSupersededDefaultOwner()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var supersededVariantId = Guid.NewGuid();
        var currentVariantId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Tenants.Add(CreateActiveTenant(tenantId));
        db.Products.Add(CreateVisibleProduct(tenantId, productId));
        db.ProductVariants.AddRange(
            new ProductVariant
            {
                Id = supersededVariantId,
                TenantId = tenantId,
                ProductId = productId,
                SKU = "OLD-DEFAULT",
                Price = 10m,
                CostPrice = 5m,
                IsActive = false,
                IsDefault = true,
                SemanticLifecycleState = ProductVariantSemanticLifecycleState.Merged,
                SemanticReplacementVariantId = currentVariantId,
                SemanticOperationId = Guid.NewGuid(),
                SemanticOperationKind = "variant-merge",
                SemanticSupersededAtUtc = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            },
            CreateVisibleVariant(tenantId, productId, currentVariantId, isDefault: false));
        await db.SaveChangesAsync();

        var repository = CreateRepository(db);
        var result = await InvokePrivateAsync<(ProductDocument? Product, ProductVariantDocument? Variant)>(
            repository,
            "LoadDefaultVariantFromSqlAsync",
            tenantId,
            productId,
            CancellationToken.None);

        Assert.NotNull(result.Product);
        Assert.NotNull(result.Variant);
        Assert.Equal(currentVariantId, result.Variant!.VariantId);
    }

    [Fact]
    public async Task CartService_SqlAuthoritySnapshot_IgnoresInactiveWarehouseStockLineage()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var activeWarehouseId = Guid.NewGuid();
        var inactiveWarehouseId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Tenants.Add(CreateActiveTenant(tenantId));
        db.Products.Add(CreateVisibleProduct(tenantId, productId));
        db.ProductVariants.Add(CreateVisibleVariant(tenantId, productId, variantId, isDefault: true));
        db.Warehouses.AddRange(
            new Warehouse
            {
                Id = activeWarehouseId,
                TenantId = tenantId,
                Name = "Active",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Warehouse
            {
                Id = inactiveWarehouseId,
                TenantId = tenantId,
                Name = "Inactive",
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            });
        db.ProductVariantStocks.AddRange(
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantId,
                WarehouseId = activeWarehouseId,
                Quantity = 5m,
                ReservedQuantity = 2m,
                CreatedAt = DateTime.UtcNow
            },
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantId,
                WarehouseId = inactiveWarehouseId,
                Quantity = 11m,
                ReservedQuantity = 0m,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var service = CreateCartService(db);
        var result = await InvokePrivateTaskResultAsync(
            service,
            "GetAuthoritativeVariantStockSnapshotsAsync",
            tenantId,
            new List<Guid> { variantId },
            CancellationToken.None);

        var snapshots = Assert.IsAssignableFrom<IDictionary>(result);
        var snapshot = snapshots[variantId];
        Assert.NotNull(snapshot);
        var availableStock = Assert.IsType<int>(
            snapshot!.GetType().GetProperty("AvailableStock")!.GetValue(snapshot));

        Assert.Equal(3, availableStock);
    }

    [Fact]
    public async Task AffiliateProductIntelligenceProjectionConsumer_SqlStockSnapshot_IgnoresInactiveWarehouseStockLineage()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var activeWarehouseId = Guid.NewGuid();
        var inactiveWarehouseId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Tenants.Add(CreateActiveTenant(tenantId));
        db.Products.Add(CreateVisibleProduct(tenantId, productId));
        db.ProductVariants.Add(CreateVisibleVariant(tenantId, productId, variantId, isDefault: true));
        db.Warehouses.AddRange(
            new Warehouse
            {
                Id = activeWarehouseId,
                TenantId = tenantId,
                Name = "Active",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Warehouse
            {
                Id = inactiveWarehouseId,
                TenantId = tenantId,
                Name = "Inactive",
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            });
        db.ProductVariantStocks.AddRange(
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantId,
                WarehouseId = activeWarehouseId,
                Quantity = 6m,
                ReservedQuantity = 2m,
                CreatedAt = DateTime.UtcNow
            },
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantId,
                WarehouseId = inactiveWarehouseId,
                Quantity = 8m,
                ReservedQuantity = 0m,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var consumer = new AffiliateProductIntelligenceProjectionConsumer(
            (MongoDbContext)RuntimeHelpers.GetUninitializedObject(typeof(MongoDbContext)),
            db,
            new ConfigurationBuilder().Build(),
            NullLogger<AffiliateProductIntelligenceProjectionConsumer>.Instance);

        var result = await InvokePrivateTaskResultAsync(
            consumer,
            "LoadSqlVariantStockSnapshotsAsync",
            productId,
            tenantId,
            null,
            null,
            null,
            CancellationToken.None);

        var snapshots = Assert.IsAssignableFrom<IEnumerable>(result)
            .Cast<object>()
            .ToList();

        var snapshot = Assert.Single(snapshots);
        var availableStock = Assert.IsType<decimal>(
            snapshot.GetType().GetProperty("AvailableStock")!.GetValue(snapshot));

        Assert.Equal(4m, availableStock);
    }

    private static ProductVariantReadRepository CreateRepository(ApplicationDbContext dbContext)
    {
        var mongoContext = (MongoDbContext)RuntimeHelpers.GetUninitializedObject(typeof(MongoDbContext));
        return new ProductVariantReadRepository(
            mongoContext,
            dbContext,
            NullLogger<ProductVariantReadRepository>.Instance);
    }

    private static async Task<T> InvokePrivateAsync<T>(
        object target,
        string methodName,
        params object[] args)
    {
        var method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task<T>)method!.Invoke(target, args)!;
        return await task;
    }

    private static async Task<object?> InvokePrivateTaskResultAsync(
        object target,
        string methodName,
        params object[] args)
    {
        var method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task)method!.Invoke(target, args)!;
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static CartService CreateCartService(ApplicationDbContext dbContext)
    {
        var redisSettings = Options.Create(new RedisSettings
        {
            Enabled = false,
            InstanceName = "inventory-authority-test"
        });

        var mongoCollection = new Mock<IMongoCollection<StorefrontConfigDocument>>();
        var mongoDatabase = new Mock<IMongoDatabase>();
        mongoDatabase
            .Setup(database => database.GetCollection<StorefrontConfigDocument>(
                "storefront_configs",
                It.IsAny<MongoCollectionSettings>()))
            .Returns(mongoCollection.Object);

        return new CartService(
            dbContext,
            new RedisConnectionFactory(
                redisSettings,
                NullLogger<RedisConnectionFactory>.Instance,
                connectionOverride: null),
            redisSettings,
            Mock.Of<IProductReadRepository>(),
            Mock.Of<IEventPublisher>(),
            mongoDatabase.Object,
            Mock.Of<IPromotionEngine>(),
            Mock.Of<ITaxCalculationService>(),
            new CommerceReliabilityTelemetry(),
            Mock.Of<IDistributedLockService>(),
            NullLogger<CartService>.Instance);
    }

    private static Product CreateVisibleProduct(Guid tenantId, Guid productId)
    {
        return new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Visible product",
            CategoryId = Guid.Empty,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            HasVariants = true,
            TrackStock = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static Tenant CreateActiveTenant(Guid tenantId)
    {
        return new Tenant
        {
            Id = tenantId,
            Name = "Inventory authority tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        };
    }

    private static ProductVariant CreateVisibleVariant(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        bool isDefault)
    {
        return new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = $"SKU-{variantId:N}".Substring(0, 12),
            Price = 10m,
            CostPrice = 5m,
            IsActive = true,
            IsDefault = isDefault,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
