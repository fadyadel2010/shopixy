using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Bulk;
using Xunit;

namespace PointOfSale.Tests.ProductDeletionSafety;

/// <summary>
/// WAVE 2A — Safe Product/Variant deletion certification.
///
/// Every test seeds history through legitimate entity graphs (no direct SQL,
/// no historical rewrites) and then asserts the deletion authority refuses
/// destructive deletion while keeping all history rows intact.
///
/// Pre-fix behavior: only <c>CanDeleteProductAsync</c> existed with six
/// product-level checks (stock quantity, purchases, sales, returns,
/// transfers, movements). Variant-level, storefront-order, CommerceHub,
/// reservation, reserved-stock, and listing protection did not exist, and the
/// variant-matrix save omission path soft-deleted unconditionally.
/// </summary>
public sealed class ProductVariantDeletionSafetyTests
{
    // ------------------------------------------------------------------
    // Variant-level: clean case
    // ------------------------------------------------------------------

    [Fact]
    public async Task CanDeleteVariantAsync_CleanUnusedVariant_IsAllowed()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.True(result.IsValid);
    }

    // ------------------------------------------------------------------
    // Variant-level: inventory blockers
    // ------------------------------------------------------------------

    [Fact]
    public async Task CanDeleteVariantAsync_VariantWithStock_IsBlocked()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        AddStock(context, tenantId, seeded.VariantId, seeded.WarehouseId, quantity: 3m);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.False(result.IsValid);
        Assert.Equal("VARIANT_HAS_STOCK", result.ErrorCode);
    }

    [Fact]
    public async Task CanDeleteVariantAsync_VariantWithReservedOnlyStock_IsBlocked()
    {
        // Quantity == 0 but reserved units are still holding sellable stock.
        // The pre-fix product check (Quantity != 0) missed this state.
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        var stock = AddStock(context, tenantId, seeded.VariantId, seeded.WarehouseId, quantity: 0m);
        stock.ReservedQuantity = 2m;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.False(result.IsValid);
        Assert.Equal("VARIANT_HAS_STOCK", result.ErrorCode);
    }

    [Fact]
    public async Task CanDeleteVariantAsync_SiblingVariantStock_DoesNotBlock()
    {
        // Variant-scoped precision: history on variant B must not block
        // deletion of unused variant A on the same product.
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        var sibling = await AddVariantAsync(context, tenantId, seeded.ProductId, "SIBLING-1");
        AddStock(context, tenantId, sibling, seeded.WarehouseId, quantity: 5m);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.True(result.IsValid);
    }

    // ------------------------------------------------------------------
    // Variant-level: transactional history blockers
    // ------------------------------------------------------------------

    [Fact]
    public async Task CanDeleteVariantAsync_VariantWithPurchaseHistory_IsBlocked()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        await AddPurchaseLineAsync(context, tenantId, seeded, seeded.VariantId);
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.False(result.IsValid);
        Assert.Equal("VARIANT_USED_IN_PURCHASES", result.ErrorCode);
        Assert.Contains("VARIANT_USED_IN_PURCHASES", Reasons(result));
    }

    [Fact]
    public async Task CanDeleteVariantAsync_VariantWithSalesAndCogsHistory_IsBlockedAndHistorySurvives()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        await AddSaleLineAsync(context, tenantId, seeded, seeded.VariantId, costSnapshot: 7.5m);
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.False(result.IsValid);
        Assert.Equal("VARIANT_USED_IN_SALES", result.ErrorCode);
        // NO HISTORICAL REWRITE: the COGS snapshot row is untouched.
        var line = await context.SalesInvoiceItems.AsNoTracking()
            .SingleAsync(item => item.ProductVariantId == seeded.VariantId);
        Assert.Equal(7.5m, line.CostPriceSnapshot);
    }

    [Fact]
    public async Task CanDeleteVariantAsync_VariantWithReturnHistory_IsBlocked()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        await AddReturnLineAsync(context, tenantId, seeded, seeded.VariantId);
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.False(result.IsValid);
        Assert.Equal("VARIANT_USED_IN_RETURNS", result.ErrorCode);
    }

    [Fact]
    public async Task CanDeleteVariantAsync_VariantWithTransferHistory_IsBlocked()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        await AddTransferLineAsync(context, tenantId, seeded, seeded.VariantId);
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.False(result.IsValid);
        Assert.Equal("VARIANT_USED_IN_TRANSFERS", result.ErrorCode);
    }

    [Fact]
    public async Task CanDeleteVariantAsync_VariantWithMovementHistory_IsBlocked()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        context.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = seeded.ProductId,
            ProductVariantId = seeded.VariantId,
            WarehouseId = seeded.WarehouseId,
            QuantityChange = 2m
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.False(result.IsValid);
        Assert.Equal("VARIANT_HAS_MOVEMENTS", result.ErrorCode);
    }

    // ------------------------------------------------------------------
    // Variant-level: order / reservation / listing blockers
    // ------------------------------------------------------------------

    [Fact]
    public async Task CanDeleteVariantAsync_VariantWithStorefrontOrder_IsBlockedAndOrderSurvives()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        await AddStorefrontOrderLineAsync(context, tenantId, seeded, seeded.VariantId);
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.False(result.IsValid);
        Assert.Equal("VARIANT_HAS_STOREFRONT_ORDERS", result.ErrorCode);
        Assert.Equal(1, await context.OrderItems.AsNoTracking()
            .CountAsync(item => item.VariantId == seeded.VariantId && !item.IsDeleted));
    }

    [Fact]
    public async Task CanDeleteVariantAsync_VariantWithCommerceHubOrder_IsBlocked()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        await AddCommerceHubOrderLineAsync(context, tenantId, seeded, seeded.VariantId, storeId: tenantId);
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.False(result.IsValid);
        Assert.Equal("VARIANT_HAS_COMMERCEHUB_ORDERS", result.ErrorCode);
    }

    [Fact]
    public async Task CanDeleteVariantAsync_VariantWithActiveReservation_IsBlocked()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        context.InventoryReservations.Add(new InventoryReservation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid(),
            ProductId = seeded.ProductId,
            VariantId = seeded.VariantId,
            WarehouseId = seeded.WarehouseId,
            ReservedQuantity = 4m,
            CommittedQuantity = 0m,
            ReleasedQuantity = 0m,
            Status = InventoryReservationStatus.Active,
            ReservationEventId = Guid.NewGuid()
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.False(result.IsValid);
        Assert.Equal("VARIANT_HAS_RESERVATIONS", result.ErrorCode);
    }

    [Fact]
    public async Task CanDeleteVariantAsync_VariantWithReleasedReservation_IsAllowed()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        context.InventoryReservations.Add(new InventoryReservation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid(),
            ProductId = seeded.ProductId,
            VariantId = seeded.VariantId,
            WarehouseId = seeded.WarehouseId,
            ReservedQuantity = 4m,
            CommittedQuantity = 0m,
            ReleasedQuantity = 4m,
            Status = InventoryReservationStatus.Released,
            ReservationEventId = Guid.NewGuid()
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task CanDeleteVariantAsync_VariantWithPublishedListing_IsBlocked()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        context.CommerceHubListings.Add(CommerceHubListing.Create(
            tenantId, seeded.ProductId, seeded.VariantId, Guid.NewGuid(), commerceHubPrice: null));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.False(result.IsValid);
        Assert.Equal("VARIANT_HAS_COMMERCEHUB_LISTING", result.ErrorCode);
    }

    [Fact]
    public async Task GetVariantDeleteBlockersAsync_MultipleHistories_ReturnsAllReasonsInOrder()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        AddStock(context, tenantId, seeded.VariantId, seeded.WarehouseId, quantity: 1m);
        await context.SaveChangesAsync();
        await AddPurchaseLineAsync(context, tenantId, seeded, seeded.VariantId);
        await AddStorefrontOrderLineAsync(context, tenantId, seeded, seeded.VariantId);
        var validation = CreateValidation(context, tenantId);

        var blockers = await validation.GetVariantDeleteBlockersAsync(seeded.ProductId, seeded.VariantId);

        var codes = blockers.Select(blocker => blocker.Code).ToArray();
        Assert.Equal(
            new[] { "VARIANT_HAS_STOCK", "VARIANT_USED_IN_PURCHASES", "VARIANT_HAS_STOREFRONT_ORDERS" },
            codes);
        Assert.All(blockers, blocker =>
        {
            Assert.False(string.IsNullOrWhiteSpace(blocker.MessageEn));
            Assert.False(string.IsNullOrWhiteSpace(blocker.MessageAr));
        });
    }

    // ------------------------------------------------------------------
    // Tenant isolation
    // ------------------------------------------------------------------

    [Fact]
    public async Task CanDeleteVariantAsync_ForeignTenantVariant_IsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var storeName = $"wave2a-isolation-{Guid.NewGuid():N}";
        await using var seedContext = CreateContext(tenantId, storeName);
        var seeded = await SeedProductAsync(seedContext, tenantId, hasVariants: true);
        seedContext.ChangeTracker.Clear();

        await using var foreignContext = CreateContext(foreignTenantId, storeName);
        var validation = CreateValidation(foreignContext, foreignTenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.False(result.IsValid);
        Assert.Equal("VARIANT_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task CanDeleteProductAsync_ForeignTenantHistory_DoesNotBlock()
    {
        // Tenant A's purchase history for its own product must not block
        // tenant B's identically-keyed product decision: checks scope through
        // the authoritative parent tenant.
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var storeName = $"wave2a-isolation-{Guid.NewGuid():N}";
        await using var seedContext = CreateContext(tenantId, storeName);
        var seeded = await SeedProductAsync(seedContext, tenantId, hasVariants: true);
        await AddPurchaseLineAsync(seedContext, tenantId, seeded, seeded.VariantId);
        seedContext.ChangeTracker.Clear();

        await using var foreignContext = CreateContext(foreignTenantId, storeName);
        var validation = CreateValidation(foreignContext, foreignTenantId);

        var result = await validation.CanDeleteProductAsync(seeded.ProductId);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task CanDeleteVariantAsync_CommerceHubOrderOfOtherStore_DoesNotBlock()
    {
        // SubOrder.StoreId IS the store tenant: another store's marketplace
        // history must not protect (or endanger) this tenant's variant.
        var tenantId = Guid.NewGuid();
        var otherStoreId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        await AddCommerceHubOrderLineAsync(context, tenantId, seeded, seeded.VariantId, storeId: otherStoreId);
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.True(result.IsValid);
    }

    // ------------------------------------------------------------------
    // Simple product: no bypass of variant history protection
    // ------------------------------------------------------------------

    [Fact]
    public async Task CanDeleteProductAsync_SimpleProductWithPurchaseHistory_IsBlocked()
    {
        // Simple product = Product(HasVariants=false) + one default variant.
        // Product deletion must observe the same history that protects the
        // underlying variant.
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: false);
        await AddPurchaseLineAsync(context, tenantId, seeded, seeded.VariantId);
        var validation = CreateValidation(context, tenantId);

        var productResult = await validation.CanDeleteProductAsync(seeded.ProductId);
        var variantResult = await validation.CanDeleteVariantAsync(seeded.ProductId, seeded.VariantId);

        Assert.False(productResult.IsValid);
        Assert.Equal("PRODUCT_USED_IN_PURCHASES", productResult.ErrorCode);
        Assert.False(variantResult.IsValid);
        Assert.Equal("VARIANT_USED_IN_PURCHASES", variantResult.ErrorCode);
    }

    [Fact]
    public async Task CanDeleteProductAsync_SimpleProductWithStorefrontOrder_IsBlocked()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: false);
        await AddStorefrontOrderLineAsync(context, tenantId, seeded, seeded.VariantId);
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteProductAsync(seeded.ProductId);

        Assert.False(result.IsValid);
        Assert.Equal("PRODUCT_HAS_STOREFRONT_ORDERS", result.ErrorCode);
    }

    [Fact]
    public async Task CanDeleteProductAsync_ProductWithReservedOnlyStock_IsBlocked()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var seeded = await SeedProductAsync(context, tenantId, hasVariants: true);
        var stock = AddStock(context, tenantId, seeded.VariantId, seeded.WarehouseId, quantity: 0m);
        stock.ReservedQuantity = 1m;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var validation = CreateValidation(context, tenantId);

        var result = await validation.CanDeleteProductAsync(seeded.ProductId);

        Assert.False(result.IsValid);
        Assert.Equal("PRODUCT_HAS_STOCK", result.ErrorCode);
    }

    // ------------------------------------------------------------------
    // Production model completeness: the authority must never silently
    // degrade outside trimmed test harnesses.
    // ------------------------------------------------------------------

    [Fact]
    public void DeletionProtectionEntities_AreAllMappedInProductionModel()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"wave2a-model-check-{Guid.NewGuid():N}")
            .Options;
        using var context = new ApplicationDbContext(options, new TestTenantContext(Guid.NewGuid()));

        var required = new[]
        {
            typeof(ProductVariantStock),
            typeof(PurchaseInvoiceItem),
            typeof(SalesInvoiceItem),
            typeof(ReturnItem),
            typeof(StockTransferItem),
            typeof(StockMovement),
            typeof(OrderItem),
            typeof(CommerceHubSubOrderItem),
            typeof(InventoryReservation),
            typeof(CommerceHubListing)
        };

        foreach (var entityType in required)
        {
            Assert.NotNull(context.Model.FindEntityType(entityType));
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private sealed record SeededProduct(
        Guid ProductId,
        Guid VariantId,
        Guid WarehouseId,
        Guid CategoryId,
        Product Product);

    private static ApplicationDbContext CreateContext(Guid tenantId, string? storeName = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(storeName ?? $"wave2a-deletion-safety-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static ValidationService CreateValidation(ApplicationDbContext context, Guid tenantId) =>
        new(context, new TestTenantContext(tenantId), NullLogger<ValidationService>.Instance);

    private static string[] Reasons(ValidationResult result) =>
        result.Data.TryGetValue("reasons", out var reasons) && reasons is string[] codes
            ? codes
            : Array.Empty<string>();

    private static async Task<SeededProduct> SeedProductAsync(
        ApplicationDbContext context, Guid tenantId, bool hasVariants)
    {
        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Wave2A deletion safety"
        };
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = category.Id,
            Name = "Wave2A Test Product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            HasVariants = hasVariants
        };
        var variantId = Guid.NewGuid();
        var variant = new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            Product = product,
            SKU = $"WAVE2A-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            IsActive = true,
            IsDefault = true
        };
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Wave2A warehouse"
        };
        context.AddRange(category, product, variant, warehouse);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return new SeededProduct(productId, variantId, warehouse.Id, category.Id, product);
    }

    private static async Task<Guid> AddVariantAsync(
        ApplicationDbContext context, Guid tenantId, Guid productId, string sku)
    {
        var variantId = Guid.NewGuid();
        context.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = sku,
            IsActive = true,
            IsDefault = false
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return variantId;
    }

    private static ProductVariantStock AddStock(
        ApplicationDbContext context, Guid tenantId, Guid variantId, Guid warehouseId, decimal quantity)
    {
        var stock = new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variantId,
            WarehouseId = warehouseId,
            Quantity = quantity
        };
        context.ProductVariantStocks.Add(stock);
        return stock;
    }

    private static async Task AddPurchaseLineAsync(
        ApplicationDbContext context, Guid tenantId, SeededProduct seeded, Guid variantId)
    {
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Wave2A supplier"
        };
        var invoice = new PurchaseInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            InvoiceNumber = $"WAVE2A-P-{Guid.NewGuid():N}"[..12],
            SupplierId = supplier.Id,
            Supplier = supplier,
            WarehouseId = seeded.WarehouseId
        };
        context.AddRange(supplier, invoice);
        context.PurchaseInvoiceItems.Add(new PurchaseInvoiceItem
        {
            Id = Guid.NewGuid(),
            PurchaseInvoiceId = invoice.Id,
            PurchaseInvoice = invoice,
            ProductId = seeded.ProductId,
            ProductVariantId = variantId,
            Quantity = 2m,
            UnitPrice = 10m,
            TotalAmount = 20m
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static async Task AddSaleLineAsync(
        ApplicationDbContext context, Guid tenantId, SeededProduct seeded, Guid variantId, decimal costSnapshot)
    {
        var invoice = new SalesInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            InvoiceNumber = $"WAVE2A-S-{Guid.NewGuid():N}"[..12],
            WarehouseId = seeded.WarehouseId
        };
        context.SalesInvoices.Add(invoice);
        context.SalesInvoiceItems.Add(new SalesInvoiceItem
        {
            Id = Guid.NewGuid(),
            SalesInvoiceId = invoice.Id,
            SalesInvoice = invoice,
            ProductId = seeded.ProductId,
            ProductVariantId = variantId,
            Quantity = 1m,
            UnitPrice = 15m,
            TotalAmount = 15m,
            CostPriceSnapshot = costSnapshot,
            CostAmountSnapshot = costSnapshot
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static async Task AddReturnLineAsync(
        ApplicationDbContext context, Guid tenantId, SeededProduct seeded, Guid variantId)
    {
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Wave2A return supplier"
        };
        var original = new PurchaseInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            InvoiceNumber = $"WAVE2A-RP-{Guid.NewGuid():N}"[..12],
            SupplierId = supplier.Id,
            Supplier = supplier,
            WarehouseId = seeded.WarehouseId
        };
        var header = new Return
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ReturnNumber = $"WAVE2A-R-{Guid.NewGuid():N}"[..12],
            ReturnType = ReturnType.PurchaseReturn,
            SupplierId = supplier.Id,
            Supplier = supplier,
            OriginalPurchaseInvoiceId = original.Id,
            OriginalPurchaseInvoice = original,
            WarehouseId = seeded.WarehouseId
        };
        context.AddRange(supplier, original, header);
        context.ReturnItems.Add(new ReturnItem
        {
            Id = Guid.NewGuid(),
            ReturnId = header.Id,
            Return = header,
            ProductId = seeded.ProductId,
            ProductVariantId = variantId,
            Quantity = 1m,
            UnitPrice = 10m,
            TotalAmount = 10m,
            CostPriceSnapshot = 8m,
            CostAmountSnapshot = 8m
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static async Task AddTransferLineAsync(
        ApplicationDbContext context, Guid tenantId, SeededProduct seeded, Guid variantId)
    {
        var transfer = new StockTransfer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TransferNumber = $"WAVE2A-T-{Guid.NewGuid():N}"[..12],
            FromWarehouseId = seeded.WarehouseId,
            ToWarehouseId = seeded.WarehouseId
        };
        context.StockTransfers.Add(transfer);
        context.StockTransferItems.Add(new StockTransferItem
        {
            Id = Guid.NewGuid(),
            StockTransferId = transfer.Id,
            StockTransfer = transfer,
            ProductId = seeded.ProductId,
            ProductVariantId = variantId,
            Quantity = 1m
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static async Task AddStorefrontOrderLineAsync(
        ApplicationDbContext context, Guid tenantId, SeededProduct seeded, Guid variantId)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = $"WAVE2A-O-{Guid.NewGuid():N}"[..12],
            Currency = "EGP",
            Subtotal = 20m,
            Total = 20m
        };
        context.Orders.Add(order);
        context.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            ProductId = seeded.ProductId,
            VariantId = variantId,
            SKU = "WAVE2A-SKU",
            ProductName = "Wave2A Test Product",
            Quantity = 1,
            UnitPrice = 20m,
            Total = 20m,
            CostPrice = 8m
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static async Task AddCommerceHubOrderLineAsync(
        ApplicationDbContext context,
        Guid tenantId,
        SeededProduct seeded,
        Guid variantId,
        Guid storeId)
    {
        var customer = CommerceHubCustomer.Create("wave2a@example.com", "Wave2A Buyer", null, null);
        context.CommerceHubCustomers.Add(customer);
        var marketplaceOrder = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Customer = customer,
            OrderNumber = $"WAVE2A-CH-{Guid.NewGuid():N}"[..12],
            FullName = "Wave2A Buyer",
            PhoneNumber = "01000000000",
            AddressLine = "Wave2A Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on Delivery"
        };
        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = marketplaceOrder.Id,
            MarketplaceOrder = marketplaceOrder,
            OrderNumber = $"WAVE2A-SUB-{Guid.NewGuid():N}"[..11],
            StoreId = storeId,
            StoreName = "Wave2A Store",
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on Delivery"
        };
        var item = new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = seeded.ProductId,
            VariantId = variantId,
            CategoryId = seeded.CategoryId,
            ProductName = "Wave2A Test Product",
            Quantity = 1,
            UnitPrice = 20m,
            Total = 20m,
            UnitCostSnapshot = 8m,
            CostAmountSnapshot = 8m,
            CostSnapshotCapturedAtUtc = DateTime.UtcNow
        };
        subOrder.Items.Add(item);
        marketplaceOrder.SubOrders.Add(subOrder);
        context.CommerceHubMarketplaceOrders.Add(marketplaceOrder);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            await context.SaveChangesAsync();
        }

        context.ChangeTracker.Clear();
    }
}
