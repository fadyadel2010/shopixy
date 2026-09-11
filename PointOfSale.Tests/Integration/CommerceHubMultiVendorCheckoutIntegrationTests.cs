using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Pricing;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.TestDoubles;

namespace PointOfSale.Tests.Integration;

public sealed class CommerceHubMultiVendorCheckoutIntegrationTests
{
    [Fact]
    public async Task CheckoutAsync_TwoStores_PersistsCanonicalSplitReservationsAndReplay()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) || databaseScope is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using var context = scope.CreateDbContext();
        var fixture = await SeedCheckoutAsync(context, secondStoreStock: 10m);
        var eventPublisher = CreateEventPublisher();
        var service = CreateService(
            context,
            new ProductVariantStockRepository(context, NullLogger<ProductVariantStockRepository>.Instance),
            eventPublisher.Object,
            fixture);
        var request = BuildRequest(fixture);

        var first = await service.CheckoutAsync(
            request,
            fixture.CustomerId,
            "m1c-two-store-first",
            "m1c-two-store-idempotency");
        var replay = await service.CheckoutAsync(
            request,
            fixture.CustomerId,
            "m1c-two-store-replay",
            "m1c-two-store-idempotency");

        Assert.False(first.IsIdempotentReplay);
        Assert.True(replay.IsIdempotentReplay);
        Assert.Equal(first.MarketplaceOrderId, replay.MarketplaceOrderId);
        Assert.Equal(450m, first.Subtotal);
        Assert.Equal(30m, first.ShippingTotal);
        Assert.Equal(480m, first.Total);
        Assert.Equal("EGP", first.Currency);
        Assert.Equal(PaymentStatus.CashOnDelivery, first.PaymentStatus);
        Assert.Equal(2, first.SubOrders.Count);

        await using var verifyContext = scope.CreateDbContext();
        var order = await verifyContext.CommerceHubMarketplaceOrders
            .AsNoTracking()
            .Include(item => item.SubOrders)
            .ThenInclude(item => item.Items)
            .SingleAsync(item => item.Id == first.MarketplaceOrderId);
        var reservations = await verifyContext.InventoryReservations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.OrderId == order.Id)
            .ToListAsync();
        var stocks = await verifyContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.VariantId == fixture.First.VariantId || item.VariantId == fixture.Second.VariantId)
            .ToDictionaryAsync(item => item.VariantId);

        Assert.Equal(fixture.CustomerId, order.CustomerId);
        Assert.Equal("m1c-two-store-idempotency", order.IdempotencyKey);
        Assert.Equal(450m, order.Subtotal);
        Assert.Equal(30m, order.ShippingTotal);
        Assert.Equal(480m, order.TotalAmount);
        Assert.Equal(2, order.SubOrders.Count);
        Assert.Equal(2, order.SubOrders.Select(item => item.StoreId).Distinct().Count());
        Assert.Equal(new[] { fixture.First.StoreId, fixture.Second.StoreId }.OrderBy(item => item),
            order.SubOrders.Select(item => item.StoreId).OrderBy(item => item));

        var firstSubOrder = Assert.Single(order.SubOrders, item => item.StoreId == fixture.First.StoreId);
        Assert.Equal(200m, firstSubOrder.Subtotal);
        Assert.Equal(10m, firstSubOrder.Shipping);
        Assert.Equal(210m, firstSubOrder.Total);
        var firstItem = Assert.Single(firstSubOrder.Items);
        Assert.Equal(fixture.First.ListingId, firstItem.ListingId);
        Assert.Equal(fixture.First.ProductId, firstItem.ProductId);
        Assert.Equal(fixture.First.VariantId, firstItem.VariantId);
        Assert.Equal(2, firstItem.Quantity);
        Assert.Equal(100m, firstItem.UnitPrice);
        Assert.Equal(200m, firstItem.Total);

        var secondSubOrder = Assert.Single(order.SubOrders, item => item.StoreId == fixture.Second.StoreId);
        Assert.Equal(250m, secondSubOrder.Subtotal);
        Assert.Equal(20m, secondSubOrder.Shipping);
        Assert.Equal(270m, secondSubOrder.Total);
        var secondItem = Assert.Single(secondSubOrder.Items);
        Assert.Equal(fixture.Second.ListingId, secondItem.ListingId);
        Assert.Equal(fixture.Second.ProductId, secondItem.ProductId);
        Assert.Equal(fixture.Second.VariantId, secondItem.VariantId);
        Assert.Equal(1, secondItem.Quantity);
        Assert.Equal(250m, secondItem.UnitPrice);
        Assert.Equal(250m, secondItem.Total);

        Assert.Equal(2, reservations.Count);
        Assert.All(reservations, reservation =>
        {
            var subOrder = Assert.Single(order.SubOrders, item => item.Id == reservation.SubOrderId);
            var orderItem = Assert.Single(subOrder.Items, item => item.Id == reservation.OrderItemId);
            Assert.Equal(subOrder.StoreId, reservation.TenantId);
            Assert.Equal(orderItem.ProductId, reservation.ProductId);
            Assert.Equal(orderItem.VariantId, reservation.VariantId);
            Assert.Equal(orderItem.Quantity, reservation.ReservedQuantity);
            Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
            Assert.Null(reservation.ExpiresAtUtc);
        });
        Assert.Equal(2m, stocks[fixture.First.VariantId].ReservedQuantity);
        Assert.Equal(1m, stocks[fixture.Second.VariantId].ReservedQuantity);
        Assert.False(await verifyContext.CommerceHubCarts.IgnoreQueryFilters().AnyAsync());
        Assert.Single(await verifyContext.CommerceHubCheckoutIdempotency.AsNoTracking().ToListAsync());

        eventPublisher.Verify(
            publisher => publisher.PublishAsync(
                It.Is<CommerceHubMarketplaceOrderCreatedEvent>(@event =>
                    @event.MarketplaceOrderId == order.Id &&
                    @event.AffectedStoreIds.Count == 2 &&
                    @event.AffectedStoreIds.Contains(fixture.First.StoreId) &&
                    @event.AffectedStoreIds.Contains(fixture.Second.StoreId)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckoutAsync_WhenSecondStoreReservationFails_RollsBackWholeMarketplaceOrder()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) || databaseScope is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using var context = scope.CreateDbContext();
        var fixture = await SeedCheckoutAsync(context, secondStoreStock: 0m);
        var eventPublisher = CreateEventPublisher();
        var preflightStock = new Mock<IProductVariantStockRepository>();
        preflightStock
            .Setup(repository => repository.GetAvailableQuantityAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(10m);
        var service = CreateService(context, preflightStock.Object, eventPublisher.Object, fixture);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() => service.CheckoutAsync(
            BuildRequest(fixture),
            fixture.CustomerId,
            "m1c-two-store-rollback",
            "m1c-two-store-rollback-idempotency"));

        Assert.Equal("OUT_OF_STOCK", exception.Code);

        await using var verifyContext = scope.CreateDbContext();
        Assert.False(await verifyContext.CommerceHubMarketplaceOrders.AsNoTracking().AnyAsync());
        Assert.False(await verifyContext.CommerceHubSubOrders.AsNoTracking().AnyAsync());
        Assert.False(await verifyContext.CommerceHubSubOrderItems.AsNoTracking().AnyAsync());
        Assert.False(await verifyContext.InventoryReservations.IgnoreQueryFilters().AsNoTracking().AnyAsync());
        Assert.False(await verifyContext.InventoryEventInbox.IgnoreQueryFilters().AsNoTracking().AnyAsync());
        Assert.False(await verifyContext.StockMovements.IgnoreQueryFilters().AsNoTracking().AnyAsync());
        Assert.False(await verifyContext.StockSyncOutboxMessages.IgnoreQueryFilters().AsNoTracking().AnyAsync());
        Assert.False(await verifyContext.CommerceHubCheckoutIdempotency.AsNoTracking().AnyAsync());

        var stocks = await verifyContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.VariantId == fixture.First.VariantId || item.VariantId == fixture.Second.VariantId)
            .ToListAsync();
        Assert.All(stocks, item => Assert.Equal(0m, item.ReservedQuantity));

        var cart = await verifyContext.CommerceHubCarts.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.False(cart.IsLocked);
        Assert.Equal(3, cart.Version);
        eventPublisher.Verify(
            publisher => publisher.PublishAsync(
                It.IsAny<CommerceHubMarketplaceOrderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckoutAsync_TwoStores_WithAmbientVendorTenant_UsesScopedGlobalAuthority()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) || databaseScope is null)
        {
            return;
        }

        await using var scope = databaseScope;
        CheckoutFixture fixture;
        string connectionString;
        await using (var seedContext = scope.CreateDbContext())
        {
            fixture = await SeedCheckoutAsync(seedContext, secondStoreStock: 10m);
            connectionString = seedContext.Database.GetConnectionString()
                ?? throw new InvalidOperationException("PostgreSQL test connection is unavailable.");
        }

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(fixture.Second.StoreId);
        using var serviceProvider = new ServiceCollection()
            .AddEntityFrameworkNpgsql()
            .AddSingleton<ITenantContext>(tenantContext)
            .BuildServiceProvider();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .UseInternalServiceProvider(serviceProvider)
            .AddInterceptors(new TenantSaveChangesInterceptor(NullLogger<TenantSaveChangesInterceptor>.Instance))
            .Options;

        await using var context = new ApplicationDbContext(options, tenantContext);
        var service = CreateService(
            context,
            new ProductVariantStockRepository(context, NullLogger<ProductVariantStockRepository>.Instance),
            CreateEventPublisher().Object,
            fixture,
            tenantContext);

        var result = await service.CheckoutAsync(
            BuildRequest(fixture),
            fixture.CustomerId,
            "m1c-ambient-vendor",
            "m1c-ambient-vendor-idempotency");

        Assert.Equal(fixture.Second.StoreId, tenantContext.TenantId);
        Assert.Equal(2, result.SubOrders.Count);

        await using var verifyContext = scope.CreateDbContext();
        var reservations = await verifyContext.InventoryReservations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.OrderId == result.MarketplaceOrderId)
            .ToListAsync();
        Assert.Equal(2, reservations.Count);
        Assert.Contains(reservations, item => item.TenantId == fixture.First.StoreId);
        Assert.Contains(reservations, item => item.TenantId == fixture.Second.StoreId);
    }

    private static CommerceHubCheckoutService CreateService(
        ApplicationDbContext context,
        IProductVariantStockRepository stockRepository,
        IEventPublisher eventPublisher,
        CheckoutFixture fixture,
        TenantContext? tenantContext = null)
    {
        var financialControl = new Mock<ICommerceHubVendorFinancialControlService>();
        financialControl
            .Setup(service => service.GetVendorsSuspendedFromNewOrdersAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        var readinessGate = new Mock<ICommerceHubFinancialReadinessGate>();
        readinessGate
            .Setup(service => service.EnsureCommerceIntakeAllowedAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var lockService = new Mock<IDistributedLockService>();
        lockService
            .Setup(service => service.TryAcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>()))
            .ReturnsAsync(DistributedLockResult.Acquired());
        lockService
            .Setup(service => service.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var commissionService = new Mock<ICommissionService>();
        commissionService
            .Setup(service => service.ResolveCommission(It.IsAny<Guid?>(), It.IsAny<Guid?>()))
            .Returns(new CommissionResolutionResult(0.10m, null, null, null, null, false, "Test"));

        var orderAccessService = new Mock<ICommerceHubOrderAccessService>();
        orderAccessService
            .Setup(service => service.GenerateAccessToken(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns("m1c-test-access-token");

        return new CommerceHubCheckoutService(
            context,
            Mock.Of<ICommerceHubCartService>(),
            stockRepository,
            new InventoryConsistencyService(context, NullLogger<InventoryConsistencyService>.Instance),
            CreateShippingService(fixture),
            commissionService.Object,
            new PromoService(context),
            financialControl.Object,
            orderAccessService.Object,
            new NoOpUserGovernanceService(),
            readinessGate.Object,
            lockService.Object,
            eventPublisher,
            new CommerceHubPriceAuthority(),
            Array.Empty<PointOfSale.API.Services.Payments.ProviderContracts.IPaymentProviderAdapter>(),
            tenantContext ?? new TenantContext(),
            NullLogger<CommerceHubCheckoutService>.Instance);
    }

    private static Mock<IEventPublisher> CreateEventPublisher()
    {
        var eventPublisher = new Mock<IEventPublisher>();
        eventPublisher
            .Setup(publisher => publisher.PublishAsync(
                It.IsAny<CommerceHubMarketplaceOrderCreatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return eventPublisher;
    }

    private static ICommerceHubShippingService CreateShippingService(CheckoutFixture fixture)
    {
        var prices = new Dictionary<Guid, decimal>
        {
            [fixture.First.StoreId] = fixture.First.ShippingPrice,
            [fixture.Second.StoreId] = fixture.Second.ShippingPrice
        };
        var shippingService = new Mock<ICommerceHubShippingService>();
        shippingService
            .Setup(service => service.CalculateAsync(
                It.IsAny<IReadOnlyCollection<CommerceHubShippingStoreRequest>>(),
                It.IsAny<IReadOnlyDictionary<Guid, Guid>?>(),
                It.IsAny<CancellationToken>(),
                true))
            .Returns((
                IReadOnlyCollection<CommerceHubShippingStoreRequest> stores,
                IReadOnlyDictionary<Guid, Guid>? requestedSelections,
                CancellationToken _,
                bool _) => Task.FromResult(new CommerceHubShippingCalculationResult
            {
                TotalShipping = stores.Sum(store => prices[store.StoreId]),
                StoreSelections = stores.Select(store => new CommerceHubShippingStoreSelection
                {
                    StoreId = store.StoreId,
                    ShippingOptionId = requestedSelections![store.StoreId],
                    ShippingPrice = prices[store.StoreId],
                    ShippingName = $"M-1C shipping {store.StoreId:N}"
                }).ToList()
            }));
        return shippingService.Object;
    }

    private static CommerceHubCheckoutRequestDto BuildRequest(CheckoutFixture fixture) => new()
    {
        CartVersion = 1,
        Customer = new CommerceHubCustomerInfoDto
        {
            FullName = "M-1C Customer",
            PhoneNumber = "01000000000",
            AddressLine = "123 Certification Street",
            City = "Cairo"
        },
        PaymentMethod = CommerceHubPaymentMethods.Cod,
        ShippingTotal = fixture.First.ShippingPrice + fixture.Second.ShippingPrice,
        Stores =
        {
            BuildStoreRequest(fixture.First),
            BuildStoreRequest(fixture.Second)
        },
        ShippingSelections =
        {
            BuildShippingSelection(fixture.First),
            BuildShippingSelection(fixture.Second)
        }
    };

    private static CommerceHubCheckoutStoreRequestDto BuildStoreRequest(VendorFixture vendor) => new()
    {
        StoreId = vendor.StoreId,
        Items =
        {
            new CommerceHubCheckoutItemRequestDto
            {
                ListingId = vendor.ListingId,
                ProductId = vendor.ProductId,
                VariantId = vendor.VariantId,
                Quantity = vendor.Quantity,
                ExpectedUnitPrice = vendor.UnitPrice
            }
        }
    };

    private static CommerceHubCheckoutShippingSelectionRequestDto BuildShippingSelection(VendorFixture vendor) => new()
    {
        StoreId = vendor.StoreId,
        ShippingOptionId = vendor.ShippingOptionId,
        ShippingPrice = vendor.ShippingPrice
    };

    private static async Task<CheckoutFixture> SeedCheckoutAsync(
        ApplicationDbContext context,
        decimal secondStoreStock)
    {
        var customerId = Guid.NewGuid();
        var commerceHubCategoryId = Guid.NewGuid();
        var first = CreateVendorFixture("A", 100m, 2, 10m);
        var second = CreateVendorFixture("B", 250m, 1, 20m);
        var nowUtc = DateTime.UtcNow;

        var customer = CommerceHubCustomer.Create(
            $"{customerId:N}@m1c.example.test",
            "M-1C Customer",
            "01000000000",
            null);
        customer.Id = customerId;
        context.CommerceHubCustomers.Add(customer);
        context.CommerceHubCategories.Add(new CommerceHubCategory
        {
            Id = commerceHubCategoryId,
            Name = "M-1C Category",
            Slug = $"m1c-{commerceHubCategoryId:N}",
            Level = 0,
            IsActive = true,
            CreatedAtUtc = nowUtc
        });

        SeedVendor(context, first, commerceHubCategoryId, stock: 10m, nowUtc);
        SeedVendor(context, second, commerceHubCategoryId, secondStoreStock, nowUtc);
        context.CommerceHubCarts.Add(new CommerceHubCart
        {
            Id = Guid.NewGuid(),
            TenantId = CommerceHubCartOwnerContext.CommerceHubCartTenantScopeId,
            CommerceHubCustomerId = customerId,
            OnlineCustomerId = customerId,
            Version = 1,
            ItemsJson = JsonSerializer.Serialize(new[]
            {
                BuildStoredCartItem(first),
                BuildStoredCartItem(second)
            })
        });

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return new CheckoutFixture(customerId, first, second);
    }

    private static void SeedVendor(
        ApplicationDbContext context,
        VendorFixture vendor,
        Guid commerceHubCategoryId,
        decimal stock,
        DateTime nowUtc)
    {
        context.Tenants.Add(new Tenant
        {
            Id = vendor.StoreId,
            Name = $"M-1C Vendor {vendor.Label}",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        context.Categories.Add(new Category
        {
            Id = vendor.CategoryId,
            TenantId = vendor.StoreId,
            Name = $"M-1C Local Category {vendor.Label}",
            CreatedAt = nowUtc
        });
        context.Products.Add(new Product
        {
            Id = vendor.ProductId,
            TenantId = vendor.StoreId,
            CategoryId = vendor.CategoryId,
            Name = $"M-1C Product {vendor.Label}",
            SKU = $"M1C-{vendor.Label}-{vendor.ProductId:N}",
            PurchasePrice = vendor.UnitPrice / 2m,
            SalePrice = vendor.UnitPrice,
            OnlinePrice = vendor.UnitPrice,
            TrackStock = true,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            CreatedAt = nowUtc
        });
        context.ProductVariants.Add(new ProductVariant
        {
            Id = vendor.VariantId,
            TenantId = vendor.StoreId,
            ProductId = vendor.ProductId,
            SKU = $"M1C-V-{vendor.Label}-{vendor.VariantId:N}",
            Price = vendor.UnitPrice,
            OnlinePrice = vendor.UnitPrice,
            CostPrice = vendor.UnitPrice / 2m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = nowUtc
        });
        context.Warehouses.Add(new Warehouse
        {
            Id = vendor.WarehouseId,
            TenantId = vendor.StoreId,
            Name = $"M-1C Warehouse {vendor.Label}",
            IsActive = true,
            IsDefault = true,
            CreatedAt = nowUtc
        });
        context.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = vendor.StoreId,
            VariantId = vendor.VariantId,
            WarehouseId = vendor.WarehouseId,
            Quantity = stock,
            ReservedQuantity = 0m,
            DamagedQuantity = 0m,
            CreatedAt = nowUtc
        });
        var listing = CommerceHubListing.Create(
            vendor.StoreId,
            vendor.ProductId,
            vendor.VariantId,
            commerceHubCategoryId,
            null);
        listing.Id = vendor.ListingId;
        context.CommerceHubListings.Add(listing);
        context.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = vendor.StoreId,
            Code = CommerceHubPaymentMethods.Cod,
            DisplayName = "Cash on delivery",
            IsEnabled = true,
            SupportsOffline = true
        });
    }

    private static object BuildStoredCartItem(VendorFixture vendor) => new
    {
        vendor.StoreId,
        vendor.ListingId,
        vendor.ProductId,
        vendor.VariantId,
        vendor.Quantity,
        PriceSnapshot = vendor.UnitPrice
    };

    private static VendorFixture CreateVendorFixture(
        string label,
        decimal unitPrice,
        int quantity,
        decimal shippingPrice) => new(
            label,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            unitPrice,
            quantity,
            shippingPrice);

    private sealed record CheckoutFixture(Guid CustomerId, VendorFixture First, VendorFixture Second);

    private sealed record VendorFixture(
        string Label,
        Guid StoreId,
        Guid CategoryId,
        Guid ProductId,
        Guid VariantId,
        Guid ListingId,
        Guid WarehouseId,
        Guid ShippingOptionId,
        decimal UnitPrice,
        int Quantity,
        decimal ShippingPrice);
}
