using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Reliability;
using Xunit;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ProductVariantMergeSplitAuthorityTests
{
    [Fact]
    public async Task PublicLifecycle_FailsClosed_ForMergedSourceVariant()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var sourceVariantId = Guid.NewGuid();
        var targetVariantId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Products.Add(CreateProduct(tenantId, productId));
        db.ProductVariants.Add(new ProductVariant
        {
            Id = sourceVariantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = null,
            Price = 10m,
            CostPrice = 5m,
            IsActive = false,
            IsDefault = false,
            SemanticLifecycleState = ProductVariantSemanticLifecycleState.Merged,
            SemanticReplacementVariantId = targetVariantId,
            SemanticOperationId = Guid.NewGuid(),
            SemanticOperationKind = ProductVariantMergeSplitAuthority.MergeOperationKind,
            SemanticSupersededAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var visible = await ProductVariantLifecycleAuthority.IsPubliclyVisibleAsync(
            db,
            tenantId,
            productId,
            sourceVariantId,
            CancellationToken.None,
            requireVariantActive: false);

        Assert.False(visible);
    }

    [Fact]
    public async Task CloneImportOwnership_IgnoresSupersededMergeSourceSku()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var sourceVariantId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Products.Add(CreateProduct(tenantId, productId));
        db.ProductVariants.Add(new ProductVariant
        {
            Id = sourceVariantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = "MERGED-SKU",
            Price = 10m,
            CostPrice = 5m,
            IsActive = false,
            IsDefault = false,
            SemanticLifecycleState = ProductVariantSemanticLifecycleState.Merged,
            SemanticReplacementVariantId = Guid.NewGuid(),
            SemanticOperationId = Guid.NewGuid(),
            SemanticOperationKind = ProductVariantMergeSplitAuthority.MergeOperationKind,
            SemanticSupersededAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var resolved = await ProductVariantCloneImportAuthority.ResolveUniqueSkuAsync(
            db,
            tenantId,
            "MERGED-SKU",
            null,
            productId,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("MERGED-SKU", resolved);
    }

    [Fact]
    public async Task ReserveAsync_RejectsSplitParentInventoryAuthority()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Products.Add(CreateProduct(tenantId, productId));
        db.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Main",
            CreatedAt = DateTime.UtcNow
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            Price = 10m,
            CostPrice = 5m,
            IsActive = false,
            IsDefault = false,
            SemanticLifecycleState = ProductVariantSemanticLifecycleState.SplitParent,
            SemanticOperationId = Guid.NewGuid(),
            SemanticOperationKind = ProductVariantMergeSplitAuthority.SplitOperationKind,
            SemanticSupersededAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        db.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variantId,
            WarehouseId = warehouseId,
            Quantity = 5m,
            ReservedQuantity = 0m,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var repository = new ProductVariantStockRepository(
            db,
            NullLogger<ProductVariantStockRepository>.Instance);

        var reserved = await repository.ReserveAsync(
            tenantId,
            variantId,
            warehouseId,
            1m,
            Guid.NewGuid(),
            cancellationToken: CancellationToken.None);

        Assert.False(reserved);
        Assert.Empty(db.InventoryReservations);
    }

    [Fact]
    public async Task ReserveAsync_RejectsInactiveWarehouseInventoryLineage()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Products.Add(CreateProduct(tenantId, productId));
        db.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Inactive",
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        });
        db.ProductVariants.Add(CreateVariant(tenantId, variantId, productId, "SKU-A", "BC-A"));
        db.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variantId,
            WarehouseId = warehouseId,
            Quantity = 5m,
            ReservedQuantity = 0m,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var repository = new ProductVariantStockRepository(
            db,
            NullLogger<ProductVariantStockRepository>.Instance);

        var reserved = await repository.ReserveAsync(
            tenantId,
            variantId,
            warehouseId,
            1m,
            Guid.NewGuid(),
            cancellationToken: CancellationToken.None);

        Assert.False(reserved);
        Assert.Empty(db.InventoryReservations);
    }

    [Fact]
    public async Task CommitOrderReservationsAsync_CollapsesExpiredReservationConsumptionReplay()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Products.Add(CreateProduct(tenantId, productId));
        db.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Main",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.ProductVariants.Add(CreateVariant(tenantId, variantId, productId, "SKU-B", "BC-B"));
        db.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variantId,
            WarehouseId = warehouseId,
            Quantity = 2m,
            ReservedQuantity = 1m,
            CreatedAt = DateTime.UtcNow
        });
        db.InventoryReservations.Add(new InventoryReservation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = orderId,
            OrderItemId = Guid.NewGuid(),
            ProductId = productId,
            VariantId = variantId,
            WarehouseId = warehouseId,
            ReservedQuantity = 1m,
            Status = InventoryReservationStatus.Active,
            ReservationEventId = Guid.NewGuid(),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        });
        await db.SaveChangesAsync();

        var repository = new ProductVariantStockRepository(
            db,
            NullLogger<ProductVariantStockRepository>.Instance);

        await repository.CommitOrderReservationsAsync(
            tenantId,
            orderId,
            CancellationToken.None);

        var stock = await db.ProductVariantStocks.IgnoreQueryFilters().SingleAsync();
        var reservation = await db.InventoryReservations.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(2m, stock.Quantity);
        Assert.Equal(1m, stock.ReservedQuantity);
        Assert.Equal(0m, reservation.CommittedQuantity);
    }

    [Fact]
    public async Task GetAvailableQuantityAsync_IgnoresInactiveWarehouseStockLineage()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var activeWarehouseId = Guid.NewGuid();
        var inactiveWarehouseId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Products.Add(CreateProduct(tenantId, productId));
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
        db.ProductVariants.Add(CreateVariant(tenantId, variantId, productId, "SKU-C", "BC-C"));
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

        var repository = new ProductVariantStockRepository(
            db,
            NullLogger<ProductVariantStockRepository>.Instance);

        var available = await repository.GetAvailableQuantityAsync(
            tenantId,
            variantId,
            CancellationToken.None);

        Assert.Equal(3m, available);
    }

    [Fact]
    public async Task GetAvailableQuantitiesAsync_IgnoresInactiveWarehouseStockLineage()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantAId = Guid.NewGuid();
        var variantBId = Guid.NewGuid();
        var activeWarehouseId = Guid.NewGuid();
        var inactiveWarehouseId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Products.Add(CreateProduct(tenantId, productId));
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
        db.ProductVariants.AddRange(
            CreateVariant(tenantId, variantAId, productId, "SKU-D", "BC-D"),
            CreateVariant(tenantId, variantBId, productId, "SKU-E", "BC-E"));
        db.ProductVariantStocks.AddRange(
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantAId,
                WarehouseId = activeWarehouseId,
                Quantity = 6m,
                ReservedQuantity = 2m,
                CreatedAt = DateTime.UtcNow
            },
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantAId,
                WarehouseId = inactiveWarehouseId,
                Quantity = 5m,
                ReservedQuantity = 0m,
                CreatedAt = DateTime.UtcNow
            },
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantBId,
                WarehouseId = activeWarehouseId,
                Quantity = 2m,
                ReservedQuantity = 1m,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var repository = new ProductVariantStockRepository(
            db,
            NullLogger<ProductVariantStockRepository>.Instance);

        var available = await repository.GetAvailableQuantitiesAsync(
            tenantId,
            [variantAId, variantBId],
            CancellationToken.None);

        Assert.Equal(4m, available[variantAId]);
        Assert.Equal(1m, available[variantBId]);
    }

    [Fact]
    public async Task ReserveAsync_StockSyncOutboxTotal_IgnoresInactiveWarehouseStockLineage()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var activeWarehouseId = Guid.NewGuid();
        var inactiveWarehouseId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Products.Add(CreateProduct(tenantId, productId));
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
        db.ProductVariants.Add(CreateVariant(tenantId, variantId, productId, "SKU-F", "BC-F"));
        db.ProductVariantStocks.AddRange(
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantId,
                WarehouseId = activeWarehouseId,
                Quantity = 5m,
                ReservedQuantity = 0m,
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

        var repository = new ProductVariantStockRepository(
            db,
            NullLogger<ProductVariantStockRepository>.Instance);

        var reserved = await repository.ReserveAsync(
            tenantId,
            variantId,
            activeWarehouseId,
            2m,
            orderId,
            cancellationToken: CancellationToken.None);

        Assert.True(reserved);

        var message = await db.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .SingleAsync();
        var stockEvent = JsonSerializer.Deserialize<ProductVariantStockChangedEvent>(message.PayloadJson);

        Assert.NotNull(stockEvent);
        Assert.Equal(3m, stockEvent!.TotalStockQuantity);
    }

    [Fact]
    public void MarkMergedInto_ClearsSourceOwnershipAndCollapsesSameOperationReplay()
    {
        var tenantId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var source = CreateVariant(tenantId, Guid.NewGuid(), Guid.NewGuid(), "SOURCE-SKU", "SOURCE-BC");
        var target = CreateVariant(tenantId, Guid.NewGuid(), source.ProductId, "TARGET-SKU", "TARGET-BC");
        var now = DateTime.UtcNow;

        ProductVariantMergeSplitAuthority.MarkMergedInto(source, target, operationId, now);
        ProductVariantMergeSplitAuthority.MarkMergedInto(source, target, operationId, now.AddSeconds(1));

        Assert.Equal(ProductVariantSemanticLifecycleState.Merged, source.SemanticLifecycleState);
        Assert.Equal(target.Id, source.SemanticReplacementVariantId);
        Assert.Null(source.SKU);
        Assert.Null(source.Barcode);
        Assert.Null(source.MediaId);
        Assert.False(source.IsActive);
    }

    [Fact]
    public async Task RejectOpenReservationsForSupersededVariantsAsync_BlocksOrphanReservationLineage()
    {
        var tenantId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.InventoryReservations.Add(new InventoryReservation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = variantId,
            WarehouseId = Guid.NewGuid(),
            ReservedQuantity = 2m,
            Status = InventoryReservationStatus.Active,
            ReservationEventId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProductVariantMergeSplitAuthority.RejectOpenReservationsForSupersededVariantsAsync(
                db,
                tenantId,
                [variantId],
                "variant merge",
                CancellationToken.None));
    }

    private static Product CreateProduct(Guid tenantId, Guid productId)
    {
        return new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Variant lifecycle product",
            CategoryId = Guid.NewGuid(),
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static ProductVariant CreateVariant(
        Guid tenantId,
        Guid variantId,
        Guid productId,
        string sku,
        string barcode)
    {
        return new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = sku,
            Barcode = barcode,
            Price = 10m,
            CostPrice = 5m,
            IsActive = true,
            IsDefault = false,
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
