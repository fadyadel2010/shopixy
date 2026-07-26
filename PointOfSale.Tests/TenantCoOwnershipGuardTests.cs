using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests;

public sealed class TenantCoOwnershipGuardTests
{
    [Fact]
    public async Task SaveChangesAsync_RejectsSalesInvoice_WhenCustomerBelongsToDifferentTenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var warehouseId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using (var seedContext = CreateContext(databaseName, tenantId))
        {
            seedContext.Warehouses.Add(CreateWarehouse(tenantId, warehouseId));
            seedContext.Customers.Add(CreateCustomer(otherTenantId, customerId));
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(databaseName, tenantId);
        context.SalesInvoices.Add(CreateSalesInvoice(tenantId, warehouseId, customerId));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        Assert.Contains("Tenant co-ownership violation blocked", exception.Message);
        Assert.Contains("CustomerId", exception.Message);
    }

    [Fact]
    public async Task SaveChangesAsync_RejectsPurchaseInvoice_WhenSupplierBelongsToDifferentTenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var warehouseId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();

        await using (var seedContext = CreateContext(databaseName, tenantId))
        {
            seedContext.Warehouses.Add(CreateWarehouse(tenantId, warehouseId));
            seedContext.Suppliers.Add(CreateSupplier(otherTenantId, supplierId));
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(databaseName, tenantId);
        context.PurchaseInvoices.Add(CreatePurchaseInvoice(tenantId, warehouseId, supplierId));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        Assert.Contains("Tenant co-ownership violation blocked", exception.Message);
        Assert.Contains("SupplierId", exception.Message);
    }

    [Fact]
    public async Task SaveChangesAsync_RejectsSalesInvoiceItem_WhenProductBelongsToDifferentTenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var warehouseId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await using (var seedContext = CreateContext(databaseName, tenantId))
        {
            seedContext.Warehouses.Add(CreateWarehouse(tenantId, warehouseId));
            seedContext.Products.Add(CreateProduct(otherTenantId, productId));
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(databaseName, tenantId);
        var invoice = CreateSalesInvoice(tenantId, warehouseId, customerId: null);
        context.SalesInvoices.Add(invoice);
        context.SalesInvoiceItems.Add(new SalesInvoiceItem
        {
            Id = Guid.NewGuid(),
            SalesInvoiceId = invoice.Id,
            ProductId = productId,
            Quantity = 1m,
            BaseQuantity = 1m,
            UnitPrice = 10m,
            TotalAmount = 10m,
            CreatedAt = DateTime.UtcNow
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        Assert.Contains("Tenant co-ownership violation blocked", exception.Message);
        Assert.Contains("ProductId", exception.Message);
    }

    [Fact]
    public async Task SaveChangesAsync_RejectsStockMovement_WhenVariantBelongsToDifferentTenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var warehouseId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        await using (var seedContext = CreateContext(databaseName, tenantId))
        {
            seedContext.Warehouses.Add(CreateWarehouse(tenantId, warehouseId));
            seedContext.Products.Add(CreateProduct(tenantId, productId));
            seedContext.ProductVariants.Add(CreateVariant(otherTenantId, variantId, productId));
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(databaseName, tenantId);
        context.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarehouseId = warehouseId,
            ProductId = productId,
            ProductVariantId = variantId,
            MovementType = StockMovementType.Sale,
            QuantityChange = -1m,
            QuantityAfter = 0m,
            AvailableAfter = 0m,
            CreatedAt = DateTime.UtcNow
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        Assert.Contains("Tenant co-ownership violation blocked", exception.Message);
        Assert.Contains("ProductVariantId", exception.Message);
    }

    [Fact]
    public async Task SaveChangesAsync_RejectsAccountLedger_WhenPartyBelongsToDifferentTenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var customerId = Guid.NewGuid();

        await using (var seedContext = CreateContext(databaseName, tenantId))
        {
            seedContext.Customers.Add(CreateCustomer(otherTenantId, customerId));
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(databaseName, tenantId);
        context.AccountLedgers.Add(new AccountLedger
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PartyType = PartyType.Customer,
            PartyId = customerId,
            TransactionDate = DateTime.UtcNow,
            TransactionType = TransactionType.Sale,
            DocumentNumber = "SALE-XTENANT",
            DebitAmount = 10m,
            RunningBalance = 10m,
            CreatedAt = DateTime.UtcNow
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        Assert.Contains("Tenant co-ownership violation blocked", exception.Message);
        Assert.Contains("PartyId", exception.Message);
    }

    private static ApplicationDbContext CreateContext(string databaseName, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static Warehouse CreateWarehouse(Guid tenantId, Guid id) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Name = $"Warehouse {id:N}",
            CreatedAt = DateTime.UtcNow
        };

    private static Customer CreateCustomer(Guid tenantId, Guid id) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Name = $"Customer {id:N}",
            CreatedAt = DateTime.UtcNow
        };

    private static Supplier CreateSupplier(Guid tenantId, Guid id) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Name = $"Supplier {id:N}",
            CreatedAt = DateTime.UtcNow
        };

    private static Product CreateProduct(Guid tenantId, Guid id) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            CategoryId = Guid.NewGuid(),
            Name = $"Product {id:N}",
            SalePrice = 10m,
            PurchasePrice = 5m,
            CreatedAt = DateTime.UtcNow
        };

    private static ProductVariant CreateVariant(Guid tenantId, Guid id, Guid productId) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ProductId = productId,
            SKU = $"SKU-{id:N}",
            Price = 10m,
            CostPrice = 5m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

    private static SalesInvoice CreateSalesInvoice(Guid tenantId, Guid warehouseId, Guid? customerId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            InvoiceNumber = $"SALE-{Guid.NewGuid():N}",
            InvoiceDate = DateTime.UtcNow,
            CustomerId = customerId,
            WarehouseId = warehouseId,
            Status = InvoiceStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            Subtotal = 10m,
            TotalAmount = 10m,
            PaidAmount = 10m,
            CreatedAt = DateTime.UtcNow
        };

    private static PurchaseInvoice CreatePurchaseInvoice(Guid tenantId, Guid warehouseId, Guid supplierId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            InvoiceNumber = $"PUR-{Guid.NewGuid():N}",
            InvoiceDate = DateTime.UtcNow,
            SupplierId = supplierId,
            WarehouseId = warehouseId,
            Status = InvoiceStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            Subtotal = 10m,
            TotalAmount = 10m,
            PaidAmount = 10m,
            CreatedAt = DateTime.UtcNow
        };
}
