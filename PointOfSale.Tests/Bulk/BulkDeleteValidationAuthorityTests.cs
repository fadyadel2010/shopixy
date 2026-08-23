using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkDeleteValidationAuthorityTests
{
    [Theory]
    [InlineData(DeleteBlocker.Stock, "PRODUCT_HAS_STOCK")]
    [InlineData(DeleteBlocker.Purchase, "PRODUCT_USED_IN_PURCHASES")]
    [InlineData(DeleteBlocker.Sale, "PRODUCT_USED_IN_SALES")]
    [InlineData(DeleteBlocker.Return, "PRODUCT_USED_IN_RETURNS")]
    [InlineData(DeleteBlocker.Transfer, "PRODUCT_USED_IN_TRANSFERS")]
    [InlineData(DeleteBlocker.Movement, "PRODUCT_HAS_MOVEMENTS")]
    public async Task CanDeleteProductAsync_PreservesExistingBlockerRules(
        DeleteBlocker blocker,
        string expectedCode)
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Delete validation"
        };
        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = category.Id,
            Name = "Delete validation product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm"
        };
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Delete validation warehouse"
        };
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Delete validation supplier"
        };
        context.AddRange(category, product, warehouse, supplier);

        switch (blocker)
        {
            case DeleteBlocker.Stock:
                var variant = new ProductVariant
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProductId = productId,
                    Product = product,
                    SKU = "DELETE-STOCK",
                    IsActive = true
                };
                context.ProductVariants.Add(variant);
                context.ProductVariantStocks.Add(new ProductVariantStock
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    VariantId = variant.Id,
                    Variant = variant,
                    WarehouseId = warehouse.Id,
                    Warehouse = warehouse,
                    Quantity = 1
                });
                break;
            case DeleteBlocker.Purchase:
                var purchaseInvoice = new PurchaseInvoice
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    InvoiceNumber = "DELETE-PURCHASE",
                    SupplierId = supplier.Id,
                    Supplier = supplier,
                    WarehouseId = warehouse.Id,
                    Warehouse = warehouse
                };
                context.PurchaseInvoices.Add(purchaseInvoice);
                context.PurchaseInvoiceItems.Add(new PurchaseInvoiceItem
                {
                    Id = Guid.NewGuid(),
                    PurchaseInvoiceId = purchaseInvoice.Id,
                    PurchaseInvoice = purchaseInvoice,
                    ProductId = productId,
                    Product = product
                });
                break;
            case DeleteBlocker.Sale:
                var salesInvoice = new SalesInvoice
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    InvoiceNumber = "DELETE-SALE",
                    WarehouseId = warehouse.Id,
                    Warehouse = warehouse
                };
                context.SalesInvoices.Add(salesInvoice);
                context.SalesInvoiceItems.Add(new SalesInvoiceItem
                {
                    Id = Guid.NewGuid(),
                    SalesInvoiceId = salesInvoice.Id,
                    SalesInvoice = salesInvoice,
                    ProductId = productId,
                    Product = product
                });
                break;
            case DeleteBlocker.Return:
                var originalPurchase = new PurchaseInvoice
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    InvoiceNumber = "DELETE-RETURN-ORIGINAL",
                    SupplierId = supplier.Id,
                    Supplier = supplier,
                    WarehouseId = warehouse.Id,
                    Warehouse = warehouse
                };
                var returnHeader = new Return
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ReturnNumber = "DELETE-RETURN",
                    ReturnType = PointOfSale.Core.Enums.ReturnType.PurchaseReturn,
                    SupplierId = supplier.Id,
                    Supplier = supplier,
                    OriginalPurchaseInvoiceId = originalPurchase.Id,
                    OriginalPurchaseInvoice = originalPurchase,
                    WarehouseId = warehouse.Id,
                    Warehouse = warehouse
                };
                context.AddRange(originalPurchase, returnHeader);
                context.ReturnItems.Add(new ReturnItem
                {
                    Id = Guid.NewGuid(),
                    ReturnId = returnHeader.Id,
                    Return = returnHeader,
                    ProductId = productId,
                    Product = product
                });
                break;
            case DeleteBlocker.Transfer:
                var transfer = new StockTransfer
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    TransferNumber = "DELETE-TRANSFER",
                    FromWarehouseId = warehouse.Id,
                    ToWarehouseId = warehouse.Id
                };
                context.StockTransfers.Add(transfer);
                context.StockTransferItems.Add(new StockTransferItem
                {
                    Id = Guid.NewGuid(),
                    StockTransferId = transfer.Id,
                    StockTransfer = transfer,
                    ProductId = productId,
                    Product = product
                });
                break;
            case DeleteBlocker.Movement:
                context.StockMovements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProductId = productId,
                    Product = product,
                    WarehouseId = warehouse.Id,
                    Warehouse = warehouse
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(blocker), blocker, null);
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var validation = new ValidationService(
            context,
            new TestTenantContext(tenantId),
            NullLogger<ValidationService>.Instance);

        var result = await validation.CanDeleteProductAsync(productId);

        Assert.False(result.IsValid);
        Assert.Equal(expectedCode, result.ErrorCode);
    }

    [Fact]
    public async Task CanDeleteProductAsync_PropagatesCancellation()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var validation = new ValidationService(
            context,
            new TestTenantContext(tenantId),
            NullLogger<ValidationService>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            validation.CanDeleteProductAsync(Guid.NewGuid(), cancellation.Token));
    }

    [Fact]
    public async Task CanDeleteProductAsync_IgnoresSoftDeletedTransactionRowsAndParents()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var category = new Category { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Delete validation" };
        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = category.Id,
            Name = "Disposable product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm"
        };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Warehouse" };
        var supplier = new Supplier { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Supplier" };
        var purchase = new PurchaseInvoice
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumber = "DELETED-PURCHASE",
            SupplierId = supplier.Id, Supplier = supplier, WarehouseId = warehouse.Id, Warehouse = warehouse
        };
        var sale = new SalesInvoice
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumber = "DELETED-SALE",
            WarehouseId = warehouse.Id, Warehouse = warehouse
        };
        var returnHeader = new Return
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ReturnNumber = "DELETED-RETURN",
            ReturnType = PointOfSale.Core.Enums.ReturnType.PurchaseReturn,
            SupplierId = supplier.Id, Supplier = supplier,
            OriginalPurchaseInvoiceId = purchase.Id, OriginalPurchaseInvoice = purchase,
            WarehouseId = warehouse.Id, Warehouse = warehouse
        };
        var transfer = new StockTransfer
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransferNumber = "DELETED-TRANSFER",
            FromWarehouseId = warehouse.Id, ToWarehouseId = warehouse.Id
        };
        context.AddRange(category, product, warehouse, supplier, purchase, sale, returnHeader, transfer);
        var purchaseItem = new PurchaseInvoiceItem
        {
            Id = Guid.NewGuid(), PurchaseInvoiceId = purchase.Id, PurchaseInvoice = purchase,
            ProductId = productId, Product = product
        };
        context.PurchaseInvoiceItems.Add(purchaseItem);
        context.SalesInvoiceItems.Add(new SalesInvoiceItem
        {
            Id = Guid.NewGuid(), SalesInvoiceId = sale.Id, SalesInvoice = sale,
            ProductId = productId, Product = product
        });
        var returnItem = new ReturnItem
        {
            Id = Guid.NewGuid(), ReturnId = returnHeader.Id, Return = returnHeader,
            ProductId = productId, Product = product
        };
        context.ReturnItems.Add(returnItem);
        context.StockTransferItems.Add(new StockTransferItem
        {
            Id = Guid.NewGuid(), StockTransferId = transfer.Id, StockTransfer = transfer,
            ProductId = productId, Product = product
        });
        await context.SaveChangesAsync();
        purchaseItem.IsDeleted = true;
        sale.IsDeleted = true;
        returnItem.IsDeleted = true;
        transfer.IsDeleted = true;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var validation = new ValidationService(
            context,
            new TestTenantContext(tenantId),
            NullLogger<ValidationService>.Instance);

        var result = await validation.CanDeleteProductAsync(productId);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task CanDeleteProductAsync_IgnoresForeignTenantTransactionReferences()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim());
        connection.CreateFunction<string?, string?, string?>(
            "btrim",
            (value, characters) => value?.Trim((characters ?? " ").ToCharArray()));
        connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        connection.CreateFunction<string?, string, string, string, string?>(
            "regexp_replace",
            (value, pattern, replacement, _) => value == null
                ? null
                : System.Text.RegularExpressions.Regex.Replace(value, pattern, replacement));
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new DeleteValidationSqliteContext(options, new TestTenantContext(tenantId));
        await context.Database.EnsureCreatedAsync();

        var category = new Category { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Current category" };
        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = category.Id,
            Category = category,
            Name = "Current product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            RowVersion = 1
        };
        var foreignWarehouse = new Warehouse
        {
            Id = Guid.NewGuid(), TenantId = foreignTenantId, Name = "Foreign warehouse"
        };
        var foreignInvoice = new SalesInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = foreignTenantId,
            InvoiceNumber = "FOREIGN-INVOICE",
            WarehouseId = foreignWarehouse.Id,
            Warehouse = foreignWarehouse
        };
        context.AddRange(category, product, foreignWarehouse, foreignInvoice);
        await context.SaveChangesAsync();

        // Deliberately bypass the application co-ownership guard to model a
        // legacy/corrupt cross-tenant child row. Delete validation must scope
        // through the authoritative parent tenant even in that state.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SalesInvoiceItems"
                ("Id", "SalesInvoiceId", "ProductId", "Quantity", "ConversionFactor",
                 "BaseQuantity", "UnitPrice", "DiscountAmount", "TaxRate", "TaxAmount",
                 "TotalAmount", "CreatedAt", "IsDeleted")
            VALUES
                ({Guid.NewGuid()}, {foreignInvoice.Id}, {productId}, {1m}, {1m},
                 {1m}, {1m}, {0m}, {0m}, {0m}, {1m}, {DateTime.UtcNow}, {false})
            """);
        context.ChangeTracker.Clear();
        var validation = new ValidationService(
            context,
            new TestTenantContext(tenantId),
            NullLogger<ValidationService>.Instance);

        var result = await validation.CanDeleteProductAsync(productId);

        Assert.True(result.IsValid);
    }

    private static ApplicationDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"bulk-delete-validation-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    public enum DeleteBlocker
    {
        Stock,
        Purchase,
        Sale,
        Return,
        Transfer,
        Movement
    }
}

internal sealed class DeleteValidationSqliteContext : ApplicationDbContext
{
    public DeleteValidationSqliteContext(
        DbContextOptions<ApplicationDbContext> options,
        PointOfSale.Core.Interfaces.ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Product>()
            .Property(product => product.RowVersion)
            .HasColumnName("xmin")
            .HasDefaultValue(1u)
            .ValueGeneratedNever()
            .IsConcurrencyToken();
    }
}
