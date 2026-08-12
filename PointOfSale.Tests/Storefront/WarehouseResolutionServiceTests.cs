using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Storefront;

public sealed class WarehouseResolutionServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;

    public WarehouseResolutionServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _dbContext = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task ResolveForPreview_ResolvesExactSingleWarehouse()
    {
        var fixture = await SeedCheckoutAsync(warehouseCount: 1, stockPerWarehouse: 5);

        var result = await fixture.Service.ResolveForPreviewAsync(
            fixture.TenantId,
            fixture.Owner,
            fixture.CheckoutSessionId,
            "test");

        Assert.Equal(WarehouseResolutionStatus.Resolved, result.Resolution.Status);
        Assert.Equal(fixture.WarehouseIds.Single(), result.Resolution.WarehouseId);
        Assert.Equal(WarehouseResolutionService.AlgorithmCode, result.Resolution.AlgorithmCode);
    }

    [Fact]
    public async Task ManualFulfillment_ResolvesConfiguredWarehouse_WithoutCarrierWarehouseAssignment()
    {
        var fixture = await SeedCheckoutAsync(warehouseCount: 1, stockPerWarehouse: 5, includeManualShippingMethod: true);

        var result = await fixture.Service.ResolveForSelectedShippingMethodAsync(
            fixture.TenantId,
            fixture.Owner,
            fixture.CheckoutSessionId,
            fixture.ShippingMethodId!.Value,
            "manual-runtime-test");

        Assert.Equal(WarehouseResolutionStatus.Resolved, result.Resolution.Status);
        Assert.Equal(fixture.WarehouseIds.Single(), result.Resolution.WarehouseId);
        Assert.Empty(_dbContext.CarrierWarehouseAssignments.IgnoreQueryFilters());
    }

    [Fact]
    public async Task ActiveShippingMethod_WithoutValidWarehouse_IsRejectedByPersistenceInvariant()
    {
        var tenantId = Guid.NewGuid();
        _dbContext.ShippingMethods.Add(new ShippingMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Invalid active method",
            CarrierCode = CarrierCodes.Manual,
            ServiceLevel = "standard",
            FulfillmentWarehouseId = null,
            IsActive = true
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _dbContext.SaveChangesAsync());
        Assert.Contains("require a fulfillment warehouse", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveForPreview_RejectsNoEligibleWarehouse()
    {
        var fixture = await SeedCheckoutAsync(warehouseCount: 1, stockPerWarehouse: 0);

        var exception = await Assert.ThrowsAsync<WarehouseResolutionException>(() =>
            fixture.Service.ResolveForPreviewAsync(
                fixture.TenantId,
                fixture.Owner,
                fixture.CheckoutSessionId,
                "test"));

        Assert.Equal(WarehouseResolutionErrorCodes.NoEligibleWarehouse, exception.ErrorCode);
        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task ResolveForPreview_RejectsMultipleCandidatesWithoutInventingWinner()
    {
        var fixture = await SeedCheckoutAsync(warehouseCount: 2, stockPerWarehouse: 5);

        var exception = await Assert.ThrowsAsync<WarehouseResolutionException>(() =>
            fixture.Service.ResolveForPreviewAsync(
                fixture.TenantId,
                fixture.Owner,
                fixture.CheckoutSessionId,
                "test"));

        Assert.Equal(WarehouseResolutionErrorCodes.WarehouseSelectionPolicyRequired, exception.ErrorCode);
        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task ResolveForPreview_RejectsEmptyCart()
    {
        var fixture = await SeedCheckoutAsync(warehouseCount: 1, stockPerWarehouse: 5, includeCartItem: false);

        var exception = await Assert.ThrowsAsync<WarehouseResolutionException>(() =>
            fixture.Service.ResolveForPreviewAsync(
                fixture.TenantId,
                fixture.Owner,
                fixture.CheckoutSessionId,
                "test"));

        Assert.Equal(WarehouseResolutionErrorCodes.NoEligibleWarehouse, exception.ErrorCode);
    }

    [Fact]
    public async Task ResolveForPreview_RejectsCartLineWithoutVariantAuthority()
    {
        var fixture = await SeedCheckoutAsync(warehouseCount: 1, stockPerWarehouse: 5, omitVariantId: true);

        var exception = await Assert.ThrowsAsync<WarehouseResolutionException>(() =>
            fixture.Service.ResolveForPreviewAsync(
                fixture.TenantId,
                fixture.Owner,
                fixture.CheckoutSessionId,
                "test"));

        Assert.Equal(WarehouseResolutionErrorCodes.NoEligibleWarehouse, exception.ErrorCode);
    }

    [Fact]
    public async Task WarehouseResolution_DecisionFieldsCannotBeMutatedAfterCreation()
    {
        var fixture = await SeedCheckoutAsync(warehouseCount: 1, stockPerWarehouse: 5);
        var result = await fixture.Service.ResolveForPreviewAsync(
            fixture.TenantId,
            fixture.Owner,
            fixture.CheckoutSessionId,
            "test");

        result.Resolution.WarehouseId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ResolveForPreview_WhenCartChanges_InvalidatesAndVersionsResolution()
    {
        var fixture = await SeedCheckoutAsync(warehouseCount: 1, stockPerWarehouse: 5);
        var first = await fixture.Service.ResolveForPreviewAsync(
            fixture.TenantId,
            fixture.Owner,
            fixture.CheckoutSessionId,
            "test");

        fixture.Cart.Items.Single().Quantity = 2;
        var second = await fixture.Service.ResolveForPreviewAsync(
            fixture.TenantId,
            fixture.Owner,
            fixture.CheckoutSessionId,
            "test");

        Assert.Equal(WarehouseResolutionStatus.Invalidated, first.Resolution.Status);
        Assert.Equal(WarehouseResolutionErrorCodes.CartChangedReResolutionRequired, first.Resolution.InvalidationReason);
        Assert.Equal(first.Resolution.ResolutionVersion + 1, second.Resolution.ResolutionVersion);
        Assert.Equal(first.Resolution.WarehouseId, second.Resolution.WarehouseId);
    }

    [Fact]
    public async Task ResolveForPreview_WhenResolutionExpires_CreatesNextVersion()
    {
        var fixture = await SeedCheckoutAsync(warehouseCount: 1, stockPerWarehouse: 5);
        var first = await fixture.Service.ResolveForPreviewAsync(
            fixture.TenantId,
            fixture.Owner,
            fixture.CheckoutSessionId,
            "test");
        first.Resolution.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await _dbContext.SaveChangesAsync();

        var second = await fixture.Service.ResolveForPreviewAsync(
            fixture.TenantId,
            fixture.Owner,
            fixture.CheckoutSessionId,
            "test");

        Assert.Equal(WarehouseResolutionStatus.Expired, first.Resolution.Status);
        Assert.Equal(first.Resolution.ResolutionVersion + 1, second.Resolution.ResolutionVersion);
    }

    [Fact]
    public async Task MarkOrdered_RequiresLockedResolution()
    {
        var fixture = await SeedCheckoutAsync(warehouseCount: 1, stockPerWarehouse: 5);
        var preview = await fixture.Service.ResolveForPreviewAsync(
            fixture.TenantId,
            fixture.Owner,
            fixture.CheckoutSessionId,
            "test");

        var exception = await Assert.ThrowsAsync<WarehouseResolutionException>(() =>
            fixture.Service.MarkOrderedAsync(fixture.TenantId, preview.Resolution.Id, Guid.NewGuid()));

        Assert.Equal(WarehouseResolutionErrorCodes.WarehouseChanged, exception.ErrorCode);
    }

    [Fact]
    public async Task MarkOrdered_FromLockedResolution_PersistsOrderAuthority()
    {
        var fixture = await SeedCheckoutAsync(warehouseCount: 1, stockPerWarehouse: 5);
        var preview = await fixture.Service.ResolveForPreviewAsync(
            fixture.TenantId,
            fixture.Owner,
            fixture.CheckoutSessionId,
            "test");
        preview.Resolution.Status = WarehouseResolutionStatus.Locked;
        await _dbContext.SaveChangesAsync();
        var orderId = Guid.NewGuid();

        await fixture.Service.MarkOrderedAsync(fixture.TenantId, preview.Resolution.Id, orderId);

        Assert.Equal(WarehouseResolutionStatus.Ordered, preview.Resolution.Status);
        Assert.Equal(orderId, preview.Resolution.ConvertedOrderId);
    }

    public void Dispose() => _dbContext.Dispose();

    private async Task<Fixture> SeedCheckoutAsync(
        int warehouseCount,
        decimal stockPerWarehouse,
        bool includeCartItem = true,
        bool omitVariantId = false,
        bool includeManualShippingMethod = false)
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var checkoutSessionId = Guid.NewGuid();
        const string ownerId = "guest-session";
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        var session = new CheckoutSession
        {
            Id = checkoutSessionId,
            TenantId = tenantId,
            CartId = cartId,
            SessionId = ownerId,
            Status = CheckoutSessionStatus.Active,
            RecoveryToken = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTime.UtcNow,
            LastActivityAtUtc = DateTime.UtcNow
        };
        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = Guid.NewGuid(),
            Name = "Product",
            TrackStock = true,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            CreatedAt = DateTime.UtcNow
        };
        var variant = new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            Product = product,
            SKU = "SKU-1",
            IsActive = true,
            IsDefault = true,
            SemanticLifecycleState = ProductVariantSemanticLifecycleState.Current,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.CheckoutSessions.Add(session);
        _dbContext.Products.Add(product);
        _dbContext.ProductVariants.Add(variant);

        var warehouseIds = new List<Guid>();
        for (var index = 0; index < warehouseCount; index++)
        {
            var warehouse = new Warehouse
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = $"Warehouse {index + 1}",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            warehouseIds.Add(warehouse.Id);
            _dbContext.Warehouses.Add(warehouse);
            _dbContext.ProductVariantStocks.Add(new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantId,
                WarehouseId = warehouse.Id,
                Variant = variant,
                Warehouse = warehouse,
                Quantity = stockPerWarehouse,
                ReservedQuantity = 0,
                CreatedAt = DateTime.UtcNow
            });
        }

        Guid? shippingMethodId = null;
        if (includeManualShippingMethod)
        {
            shippingMethodId = Guid.NewGuid();
            _dbContext.ShippingMethods.Add(new ShippingMethod
            {
                Id = shippingMethodId.Value,
                TenantId = tenantId,
                Name = "Manual Delivery",
                CarrierCode = CarrierCodes.Manual,
                ServiceLevel = "standard",
                FulfillmentWarehouseId = warehouseIds.Single(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();

        var items = includeCartItem
            ? new List<CartItemDto>
            {
                new()
                {
                    ProductId = productId,
                    VariantId = omitVariantId ? null : variantId,
                    SKU = "SKU-1",
                    Name = "Product",
                    Quantity = 1,
                    UnitPrice = 10
                }
            }
            : new List<CartItemDto>();
        var cart = new CartDto
        {
            CartId = cartId,
            TenantId = tenantId,
            OwnerType = "session",
            OwnerId = ownerId,
            Items = items,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            LastUpdatedUtc = DateTime.UtcNow,
            Currency = "EGP"
        };
        var cartService = new Mock<ICartService>();
        cartService.Setup(service => service.GetCartAsync(
                tenantId,
                It.Is<CartOwner>(owner => owner.OwnerType == "session" && owner.OwnerId == ownerId),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cart);

        var service = new WarehouseResolutionService(
            _dbContext,
            cartService.Object,
            Mock.Of<ILogger<WarehouseResolutionService>>());

        return new Fixture(
            service,
            tenantId,
            checkoutSessionId,
            new CartOwner { OwnerType = "session", OwnerId = ownerId },
            warehouseIds,
            cart,
            shippingMethodId);
    }

    private sealed record Fixture(
        WarehouseResolutionService Service,
        Guid TenantId,
        Guid CheckoutSessionId,
        CartOwner Owner,
        IReadOnlyList<Guid> WarehouseIds,
        CartDto Cart,
        Guid? ShippingMethodId);
}
