using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests;

public sealed class PurchaseInvoiceFinancialGuardTests
{
    [Fact]
    public async Task SaveChangesAsync_RejectsNewPurchaseInvoice_WhenPaidAmountExceedsTotalAmount()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");

        await using var context = CreateContext(databaseName, tenantId);
        var invoice = CreateInvoice(tenantId, totalAmount: 100m, paidAmount: 120m);
        SeedPurchaseAuthority(context, invoice);
        context.PurchaseInvoices.Add(invoice);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        Assert.Contains("Paid amount cannot exceed total amount.", exception.Message);

        await using var verifyContext = CreateContext(databaseName, tenantId);
        Assert.Equal(0, await verifyContext.PurchaseInvoices.CountAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_RejectsModifiedPurchaseInvoice_WhenPaidAmountExceedsTotalAmount()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");

        await using var seedContext = CreateContext(databaseName, tenantId);
        var invoice = CreateInvoice(tenantId, totalAmount: 100m, paidAmount: 100m);
        SeedPurchaseAuthority(seedContext, invoice);
        seedContext.PurchaseInvoices.Add(invoice);
        await seedContext.SaveChangesAsync();

        await using var actionContext = CreateContext(databaseName, tenantId);
        var persistedInvoice = await actionContext.PurchaseInvoices.SingleAsync();
        persistedInvoice.PaidAmount = 120m;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => actionContext.SaveChangesAsync());

        Assert.Contains("Paid amount cannot exceed total amount.", exception.Message);

        await using var verifyContext = CreateContext(databaseName, tenantId);
        var reloadedInvoice = await verifyContext.PurchaseInvoices.SingleAsync();
        Assert.Equal(100m, reloadedInvoice.TotalAmount);
        Assert.Equal(100m, reloadedInvoice.PaidAmount);
    }

    private static ApplicationDbContext CreateContext(string databaseName, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static PurchaseInvoice CreateInvoice(Guid tenantId, decimal totalAmount, decimal paidAmount)
    {
        return new PurchaseInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            InvoiceNumber = "PUR-GUARD-00000001",
            InvoiceDate = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc),
            SupplierId = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            Status = InvoiceStatus.Completed,
            Subtotal = totalAmount,
            DiscountAmount = 0m,
            DiscountPercentage = 0m,
            TaxAmount = 0m,
            TotalAmount = totalAmount,
            PaidAmount = paidAmount,
            PaymentMethod = PaymentMethod.Card,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static void SeedPurchaseAuthority(ApplicationDbContext context, PurchaseInvoice invoice)
    {
        context.Suppliers.Add(new Supplier
        {
            Id = invoice.SupplierId,
            TenantId = invoice.TenantId,
            Name = "Financial guard supplier",
            CreatedAt = DateTime.UtcNow
        });
        context.Warehouses.Add(new Warehouse
        {
            Id = invoice.WarehouseId,
            TenantId = invoice.TenantId,
            Name = "Financial guard warehouse",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
    }
}
