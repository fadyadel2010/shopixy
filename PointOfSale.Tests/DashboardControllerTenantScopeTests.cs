using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests;

public sealed class DashboardControllerTenantScopeTests
{
    [Fact]
    public async Task GetRecentSales_WithoutAmbientTenantFilters_ReturnsOnlyCurrentTenantInvoices()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var foreignWarehouseId = Guid.NewGuid();

        await using var dbContext = CreateUnscopedDbContext();
        dbContext.Warehouses.AddRange(
            new Warehouse
            {
                Id = warehouseId,
                TenantId = tenantId,
                Name = "Local Warehouse",
                CreatedAt = DateTime.UtcNow
            },
            new Warehouse
            {
                Id = foreignWarehouseId,
                TenantId = foreignTenantId,
                Name = "Foreign Warehouse",
                CreatedAt = DateTime.UtcNow
            });

        dbContext.SalesInvoices.AddRange(
            new SalesInvoice
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                WarehouseId = warehouseId,
                InvoiceNumber = "LOCAL-001",
                InvoiceDate = DateTime.UtcNow.AddMinutes(-5),
                Status = InvoiceStatus.Completed,
                TotalAmount = 150m,
                PaidAmount = 150m,
                CreatedAt = DateTime.UtcNow
            },
            new SalesInvoice
            {
                Id = Guid.NewGuid(),
                TenantId = foreignTenantId,
                WarehouseId = foreignWarehouseId,
                InvoiceNumber = "FOREIGN-001",
                InvoiceDate = DateTime.UtcNow.AddMinutes(-1),
                Status = InvoiceStatus.Completed,
                TotalAmount = 999m,
                PaidAmount = 999m,
                CreatedAt = DateTime.UtcNow
            });

        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, tenantId);

        var result = await controller.GetRecentSales(10, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var sales = Assert.IsAssignableFrom<List<RecentSaleResponse>>(ok.Value);
        var sale = Assert.Single(sales);
        Assert.Equal("LOCAL-001", sale.InvoiceNumber);
        Assert.Equal(150m, sale.TotalAmount);
    }

    [Fact]
    public async Task GetTopProducts_WithoutAmbientTenantFilters_AggregatesOnlyCurrentTenantSales()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var foreignWarehouseId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var foreignCategoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var foreignProductId = Guid.NewGuid();
        var localInvoiceId = Guid.NewGuid();
        var foreignInvoiceId = Guid.NewGuid();

        await using var dbContext = CreateUnscopedDbContext();
        dbContext.Categories.AddRange(
            new Category
            {
                Id = categoryId,
                TenantId = tenantId,
                Name = "Local Category",
                CreatedAt = DateTime.UtcNow
            },
            new Category
            {
                Id = foreignCategoryId,
                TenantId = foreignTenantId,
                Name = "Foreign Category",
                CreatedAt = DateTime.UtcNow
            });
        dbContext.Warehouses.AddRange(
            new Warehouse
            {
                Id = warehouseId,
                TenantId = tenantId,
                Name = "Local Warehouse",
                CreatedAt = DateTime.UtcNow
            },
            new Warehouse
            {
                Id = foreignWarehouseId,
                TenantId = foreignTenantId,
                Name = "Foreign Warehouse",
                CreatedAt = DateTime.UtcNow
            });

        var localProduct = new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = categoryId,
            Name = "Local Product",
            SKU = "LOCAL-SKU",
            PurchasePrice = 25m,
            SalePrice = 50m,
            PublishStatus = PublishStatus.Published,
            IsOnlineVisible = true,
            CreatedAt = DateTime.UtcNow
        };
        var foreignProduct = new Product
        {
            Id = foreignProductId,
            TenantId = foreignTenantId,
            CategoryId = foreignCategoryId,
            Name = "Foreign Product",
            SKU = "FOREIGN-SKU",
            PurchasePrice = 40m,
            SalePrice = 80m,
            PublishStatus = PublishStatus.Published,
            IsOnlineVisible = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Products.AddRange(localProduct, foreignProduct);

        var localInvoice = new SalesInvoice
        {
            Id = localInvoiceId,
            TenantId = tenantId,
            WarehouseId = warehouseId,
            InvoiceNumber = "LOCAL-002",
            InvoiceDate = DateTime.UtcNow.AddMinutes(-10),
            Status = InvoiceStatus.Completed,
            TotalAmount = 100m,
            PaidAmount = 100m,
            CreatedAt = DateTime.UtcNow
        };
        var foreignInvoice = new SalesInvoice
        {
            Id = foreignInvoiceId,
            TenantId = foreignTenantId,
            WarehouseId = foreignWarehouseId,
            InvoiceNumber = "FOREIGN-002",
            InvoiceDate = DateTime.UtcNow.AddMinutes(-8),
            Status = InvoiceStatus.Completed,
            TotalAmount = 400m,
            PaidAmount = 400m,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.SalesInvoices.AddRange(localInvoice, foreignInvoice);

        dbContext.SalesInvoiceItems.AddRange(
            new SalesInvoiceItem
            {
                Id = Guid.NewGuid(),
                SalesInvoiceId = localInvoiceId,
                ProductId = productId,
                SalesInvoice = localInvoice,
                Product = localProduct,
                Quantity = 2,
                BaseQuantity = 2,
                UnitPrice = 50m,
                TotalAmount = 100m,
                CreatedAt = DateTime.UtcNow
            },
            new SalesInvoiceItem
            {
                Id = Guid.NewGuid(),
                SalesInvoiceId = foreignInvoiceId,
                ProductId = foreignProductId,
                SalesInvoice = foreignInvoice,
                Product = foreignProduct,
                Quantity = 5,
                BaseQuantity = 5,
                UnitPrice = 80m,
                TotalAmount = 400m,
                CreatedAt = DateTime.UtcNow
            });

        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, tenantId);

        var result = await controller.GetTopProducts(10, "month", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var products = Assert.IsAssignableFrom<List<TopProductResponse>>(ok.Value);
        var product = Assert.Single(products);
        Assert.Equal(productId, product.ProductId);
        Assert.Equal("Local Product", product.ProductName);
        Assert.Equal(2, product.QuantitySold);
        Assert.Equal(100m, product.TotalRevenue);
    }

    private static ApplicationDbContext CreateUnscopedDbContext() =>
        new QueryFilterDisabledApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static DashboardController CreateController(ApplicationDbContext dbContext, Guid tenantId) =>
        new(
            dbContext,
            new TestTenantContext(tenantId),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<DashboardController>.Instance);

    private sealed class QueryFilterDisabledApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                entityType.SetQueryFilter(null);
            }
        }
    }
}
