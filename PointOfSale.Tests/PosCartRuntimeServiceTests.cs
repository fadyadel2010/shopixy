using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.PosCart;
using PointOfSale.API.Services.Pos;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests;

public sealed class PosCartRuntimeServiceTests
{
    [Fact]
    public async Task CreateAndAddItem_SnapshotsPriceValidatesInventoryAndStagesOutbox()
    {
        var fixture = await CreateFixtureAsync();
        var service = CreateService(fixture.Db, fixture.TenantId);

        var create = await service.CreateAsync(
            new CreatePosCartRequest { WarehouseId = fixture.WarehouseId },
            fixture.CashierId,
            "cart-create-1");
        Assert.Equal(StatusCodes.Status201Created, create.StatusCode);
        Assert.NotNull(create.Value);

        var added = await service.AddItemAsync(
            create.Value!.CartId,
            new UpsertPosCartItemRequest
            {
                ProductId = fixture.ProductId,
                ProductVariantId = fixture.VariantId,
                UnitId = fixture.UnitId,
                Quantity = 2m,
                DiscountAmount = 1m,
                ExpectedVersion = create.Value.Version
            },
            fixture.CashierId,
            "cart-add-1");

        Assert.Equal(StatusCodes.Status200OK, added.StatusCode);
        var item = Assert.Single(added.Value!.Items);
        Assert.Equal(15m, item.UnitPriceSnapshot);
        Assert.Equal(30m, added.Value.Subtotal);
        Assert.Equal(1m, added.Value.DiscountAmount);
        Assert.Equal(2.90m, added.Value.TaxAmount);
        Assert.Equal(31.90m, added.Value.TotalAmount);

        var outbox = await fixture.Db.PosCartOutboxMessages
            .Where(message => message.CartId == create.Value.CartId)
            .OrderBy(message => message.OccurredAtUtc)
            .ToListAsync();
        Assert.Equal(2, outbox.Count);
        Assert.Contains(outbox, message => message.EventType == "PosCartCreated");
        Assert.Contains(outbox, message => message.EventType == "PosCartItemAdded");
    }

    [Fact]
    public async Task AddItem_ReplaysSameIdempotencyKeyAndRejectsDifferentPayload()
    {
        var fixture = await CreateFixtureAsync();
        var service = CreateService(fixture.Db, fixture.TenantId);
        var cart = (await service.CreateAsync(
            new CreatePosCartRequest { WarehouseId = fixture.WarehouseId },
            fixture.CashierId,
            "cart-create-idem")).Value!;

        var request = new UpsertPosCartItemRequest
        {
            ProductId = fixture.ProductId,
            ProductVariantId = fixture.VariantId,
            UnitId = fixture.UnitId,
            Quantity = 1m,
            ExpectedVersion = cart.Version
        };

        var first = await service.AddItemAsync(cart.CartId, request, fixture.CashierId, "cart-add-idem");
        var replay = await service.AddItemAsync(cart.CartId, request, fixture.CashierId, "cart-add-idem");
        var conflict = await service.AddItemAsync(
            cart.CartId,
            request with { Quantity = 2m },
            fixture.CashierId,
            "cart-add-idem");

        Assert.Equal(StatusCodes.Status200OK, first.StatusCode);
        Assert.Equal(first.Value!.Version, replay.Value!.Version);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Single(fixture.Db.PosCartItems);
    }

    [Fact]
    public async Task Lifecycle_HoldResumeAbandonRecover_RequiresExpectedVersion()
    {
        var fixture = await CreateFixtureAsync();
        var service = CreateService(fixture.Db, fixture.TenantId);
        var cart = (await service.CreateAsync(
            new CreatePosCartRequest { WarehouseId = fixture.WarehouseId },
            fixture.CashierId,
            "cart-create-lifecycle")).Value!;

        var stale = await service.TransitionAsync(
            cart.CartId,
            "Hold",
            new PosCartLifecycleRequest { ExpectedVersion = cart.Version + 1 },
            fixture.CashierId,
            "cart-hold-stale");
        Assert.Equal(StatusCodes.Status409Conflict, stale.StatusCode);

        var held = await service.TransitionAsync(
            cart.CartId,
            "Hold",
            new PosCartLifecycleRequest { ExpectedVersion = cart.Version },
            fixture.CashierId,
            "cart-hold");
        Assert.Equal(PosCartStatus.Held, held.Value!.Status);

        var resume = await service.TransitionAsync(
            cart.CartId,
            "Resume",
            new PosCartLifecycleRequest { ExpectedVersion = held.Value.Version },
            fixture.CashierId,
            "cart-resume");
        Assert.Equal(PosCartStatus.Active, resume.Value!.Status);

        var abandoned = await service.TransitionAsync(
            cart.CartId,
            "Abandon",
            new PosCartLifecycleRequest { ExpectedVersion = resume.Value.Version },
            fixture.CashierId,
            "cart-abandon");
        Assert.Equal(PosCartStatus.Abandoned, abandoned.Value!.Status);

        var recovered = await service.TransitionAsync(
            cart.CartId,
            "Recover",
            new PosCartLifecycleRequest { ExpectedVersion = abandoned.Value.Version, Reason = "terminal reboot" },
            fixture.CashierId,
            "cart-recover");
        Assert.Equal(PosCartStatus.Recovered, recovered.Value!.Status);
    }

    [Fact]
    public async Task CashierIsolation_BlocksOtherCashierAccess()
    {
        var fixture = await CreateFixtureAsync();
        var service = CreateService(fixture.Db, fixture.TenantId);
        var cart = (await service.CreateAsync(
            new CreatePosCartRequest { WarehouseId = fixture.WarehouseId },
            fixture.CashierId,
            "cart-create-isolation")).Value!;

        var otherCashierId = Guid.NewGuid();
        fixture.Db.Users.Add(new User
        {
            Id = otherCashierId,
            TenantId = fixture.TenantId,
            Username = "cashier2",
            Email = "cashier2@example.test",
            Role = UserRole.Cashier,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var read = await service.GetAsync(cart.CartId, otherCashierId);
        var add = await service.AddItemAsync(
            cart.CartId,
            new UpsertPosCartItemRequest
            {
                ProductId = fixture.ProductId,
                ProductVariantId = fixture.VariantId,
                UnitId = fixture.UnitId,
                Quantity = 1m,
                ExpectedVersion = cart.Version
            },
            otherCashierId,
            "cart-add-cross-cashier");

        Assert.Equal(StatusCodes.Status404NotFound, read.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, add.StatusCode);
    }

    [Fact]
    public async Task CheckoutPreparation_UsesAuthoritativeCartLinesAndCompletionMarksCartComplete()
    {
        var fixture = await CreateFixtureAsync();
        var service = CreateService(fixture.Db, fixture.TenantId);
        var cart = (await service.CreateAsync(
            new CreatePosCartRequest { WarehouseId = fixture.WarehouseId },
            fixture.CashierId,
            "cart-create-checkout")).Value!;
        cart = (await service.AddItemAsync(
            cart.CartId,
            new UpsertPosCartItemRequest
            {
                ProductId = fixture.ProductId,
                ProductVariantId = fixture.VariantId,
                UnitId = fixture.UnitId,
                Quantity = 1m,
                ExpectedVersion = cart.Version
            },
            fixture.CashierId,
            "cart-add-checkout")).Value!;

        var checkout = await service.PrepareCheckoutAsync(
            cart.CartId,
            new CreateSaleRequest
            {
                WarehouseId = fixture.WarehouseId,
                Items = new List<SaleItemRequest>(),
                PaymentMethod = PaymentMethod.Cash,
                PaidAmount = cart.TotalAmount
            },
            fixture.CashierId);

        Assert.True(checkout.Succeeded);
        var saleLine = Assert.Single(checkout.Request.Items);
        Assert.Equal(fixture.ProductId, saleLine.ProductId);
        Assert.Equal(fixture.VariantId, saleLine.ProductVariantId);
        Assert.Equal(15m, saleLine.UnitPrice);

        var saleId = Guid.NewGuid();
        await service.CompleteFromSaleAsync(cart.CartId, saleId, fixture.CashierId);
        await fixture.Db.SaveChangesAsync();

        var completed = await fixture.Db.PosCarts.SingleAsync(posCart => posCart.Id == cart.CartId);
        Assert.Equal(PosCartStatus.Completed, completed.Status);
        Assert.Equal(saleId, completed.CompletedSaleId);
    }

    [Fact]
    public async Task AddItem_FailsClosedWhenInventoryIsInsufficient()
    {
        var fixture = await CreateFixtureAsync(stockQuantity: 1m);
        var service = CreateService(fixture.Db, fixture.TenantId);
        var cart = (await service.CreateAsync(
            new CreatePosCartRequest { WarehouseId = fixture.WarehouseId },
            fixture.CashierId,
            "cart-create-stock")).Value!;

        var result = await service.AddItemAsync(
            cart.CartId,
            new UpsertPosCartItemRequest
            {
                ProductId = fixture.ProductId,
                ProductVariantId = fixture.VariantId,
                UnitId = fixture.UnitId,
                Quantity = 2m,
                ExpectedVersion = cart.Version
            },
            fixture.CashierId,
            "cart-add-stock");

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Empty(fixture.Db.PosCartItems);
    }

    private static PosCartRuntimeService CreateService(ApplicationDbContext db, Guid tenantId) =>
        new(db, new TestTenantContext(tenantId), NullLogger<PosCartRuntimeService>.Instance);

    private static async Task<Fixture> CreateFixtureAsync(decimal stockQuantity = 10m)
    {
        var tenantId = Guid.NewGuid();
        var cashierId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            new TestTenantContext(tenantId));

        db.Users.Add(new User
        {
            Id = cashierId,
            TenantId = tenantId,
            Username = "cashier",
            Email = "cashier@example.test",
            Role = UserRole.Cashier,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Main",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Category",
            CreatedAt = DateTime.UtcNow
        });
        db.Units.Add(new PointOfSale.Core.Entities.Unit
        {
            Id = unitId,
            TenantId = tenantId,
            Name = "Piece",
            Symbol = "pc",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = categoryId,
            Name = "Product",
            SalePrice = 12m,
            TaxRate = 10m,
            IsActive = true,
            TrackStock = true,
            CreatedAt = DateTime.UtcNow
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = "SKU-1",
            Price = 12m,
            CostPrice = 5m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.ProductUnits.Add(new ProductUnit
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            UnitId = unitId,
            ConversionFactor = 1m,
            SellingPrice = 15m,
            IsBaseUnit = true,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        });
        db.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarehouseId = warehouseId,
            VariantId = variantId,
            Quantity = stockQuantity,
            ReservedQuantity = 0m,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return new Fixture(db, tenantId, cashierId, warehouseId, productId, variantId, unitId);
    }

    private sealed record Fixture(
        ApplicationDbContext Db,
        Guid TenantId,
        Guid CashierId,
        Guid WarehouseId,
        Guid ProductId,
        Guid VariantId,
        Guid UnitId);
}
