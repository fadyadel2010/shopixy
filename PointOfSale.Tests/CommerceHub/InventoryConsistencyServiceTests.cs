using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class InventoryConsistencyServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly InventoryConsistencyService _service;

    public InventoryConsistencyServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _service = new InventoryConsistencyService(_dbContext, NullLogger<InventoryConsistencyService>.Instance);
    }

    [Fact]
    public async Task CommitDeliveryAsync_ShouldDecreaseStockOnce_WhenEventIsRetried()
    {
        var graph = await SeedInventoryGraphAsync(initialQuantity: 10m, orderQuantity: 3);

        var reserved = await _service.ReserveOrderItemAsync(
            new InventoryReservationRequest(
                graph.TenantId,
                graph.OrderId,
                graph.SubOrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                3m,
                null,
                null,
                InventoryEventIdFactory.Create("reserve", graph.OrderItemId),
                "corr-reserve",
                "test"));

        Assert.True(reserved);

        var deliveryEventId = InventoryEventIdFactory.Create("delivery", graph.OrderItemId);
        var request = new InventoryDeliveryCommitRequest(
            graph.TenantId,
            graph.OrderId,
            graph.SubOrderId,
            graph.OrderItemId,
            graph.ProductId,
            graph.VariantId,
            3m,
            deliveryEventId,
            "corr-delivery",
            "test");

        await _service.CommitDeliveryAsync(request);
        await _service.CommitDeliveryAsync(request);

        var stock = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync();
        Assert.Equal(7m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);

        var deliveryMovements = await _dbContext.StockMovements
            .IgnoreQueryFilters()
            .Where(movement => movement.InventoryEventType == InventoryEventType.StockDecreased)
            .ToListAsync();

        Assert.Single(deliveryMovements);
    }

    [Fact]
    public async Task ReleaseOrderItemReservationsAsync_ShouldReleaseReservedStock_OnCancellation()
    {
        var graph = await SeedInventoryGraphAsync(initialQuantity: 8m, orderQuantity: 2);

        await _service.ReserveOrderItemAsync(
            new InventoryReservationRequest(
                graph.TenantId,
                graph.OrderId,
                graph.SubOrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                2m,
                null,
                null,
                InventoryEventIdFactory.Create("reserve", graph.OrderItemId),
                "corr-reserve",
                "test"));

        await _service.ReleaseOrderItemReservationsAsync(
            graph.TenantId,
            graph.OrderItemId,
            quantity: null,
            eventId: InventoryEventIdFactory.Create("cancel", graph.OrderItemId),
            correlationId: "corr-cancel",
            source: "test");

        var stock = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync();
        Assert.Equal(8m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
    }

    [Fact]
    public async Task ReserveOrderItemAsync_ShouldRejectSameEventIdWithDifferentPayload()
    {
        var graph = await SeedInventoryGraphAsync(initialQuantity: 8m, orderQuantity: 2);
        var eventId = InventoryEventIdFactory.Create("reserve-conflict", graph.OrderItemId);

        var reserved = await _service.ReserveOrderItemAsync(
            new InventoryReservationRequest(
                graph.TenantId,
                graph.OrderId,
                graph.SubOrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                2m,
                null,
                null,
                eventId,
                "corr-reserve",
                "test"));

        Assert.True(reserved);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ReserveOrderItemAsync(
                new InventoryReservationRequest(
                    graph.TenantId,
                    graph.OrderId,
                    graph.SubOrderId,
                    graph.OrderItemId,
                    graph.ProductId,
                    graph.VariantId,
                    1m,
                    null,
                    null,
                    eventId,
                    "corr-reserve-replay",
                    "test")));

        Assert.Contains("INVENTORY_EVENT_ID_PAYLOAD_CONFLICT", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReceiveReturnAsync_ShouldIncreaseSellableStock_ForResellableReceipt()
    {
        var graph = await SeedDeliveredGraphAsync(initialQuantity: 10m, orderQuantity: 2);
        var returnRequest = await SeedReturnRequestAsync(graph, requestedQuantity: 1);

        await _service.ReceiveReturnAsync(
            new InventoryReturnReceiptRequest(
                graph.TenantId,
                returnRequest.Id,
                returnRequest.RefundId,
                graph.OrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                graph.OrderQuantity,
                1m,
                0m,
                InventoryEventIdFactory.Create("return", returnRequest.Id),
                "corr-return",
                "test"));

        var stock = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync();
        Assert.Equal(9m, stock.Quantity);
        Assert.Equal(0m, stock.DamagedQuantity);
    }

    [Fact]
    public async Task ReceiveReturnAsync_ShouldIncreaseDamagedBucket_ForDamagedReceipt()
    {
        var graph = await SeedDeliveredGraphAsync(initialQuantity: 10m, orderQuantity: 2);
        var returnRequest = await SeedReturnRequestAsync(graph, requestedQuantity: 1);

        await _service.ReceiveReturnAsync(
            new InventoryReturnReceiptRequest(
                graph.TenantId,
                returnRequest.Id,
                returnRequest.RefundId,
                graph.OrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                graph.OrderQuantity,
                0m,
                1m,
                InventoryEventIdFactory.Create("return-damaged", returnRequest.Id),
                "corr-return",
                "test"));

        var stock = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync();
        Assert.Equal(8m, stock.Quantity);
        Assert.Equal(1m, stock.DamagedQuantity);
    }

    [Fact]
    public async Task ReceiveReturnAsync_ShouldBlock_WhenCumulativeQuantityExceedsDelivered()
    {
        var graph = await SeedDeliveredGraphAsync(initialQuantity: 10m, orderQuantity: 2);
        var firstReturn = await SeedReturnRequestAsync(graph, requestedQuantity: 2);
        var secondReturn = await SeedReturnRequestAsync(graph, requestedQuantity: 1);

        await _service.ReceiveReturnAsync(
            new InventoryReturnReceiptRequest(
                graph.TenantId,
                firstReturn.Id,
                firstReturn.RefundId,
                graph.OrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                graph.OrderQuantity,
                2m,
                0m,
                InventoryEventIdFactory.Create("return-one", firstReturn.Id),
                "corr-return-1",
                "test"));

        secondReturn.Status = CommerceHubReturnStatus.DeliveredToVendor;
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            await _dbContext.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.ReceiveReturnAsync(
                new InventoryReturnReceiptRequest(
                    graph.TenantId,
                    secondReturn.Id,
                    secondReturn.RefundId,
                    graph.OrderId,
                    graph.OrderItemId,
                    graph.ProductId,
                    graph.VariantId,
                    graph.OrderQuantity,
                    1m,
                    0m,
                    InventoryEventIdFactory.Create("return-two", secondReturn.Id),
                    "corr-return-2",
                    "test")));

        Assert.Equal("RETURN_QUANTITY_EXCEEDS_DELIVERED", exception.Code);
    }

    [Fact]
    public async Task ReceiveReturnAsync_ShouldRejectInactiveWarehouseReplay()
    {
        var graph = await SeedDeliveredGraphAsync(initialQuantity: 10m, orderQuantity: 2);
        var returnRequest = await SeedReturnRequestAsync(graph, requestedQuantity: 1);

        var warehouse = await _dbContext.Warehouses
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == graph.WarehouseId);
        warehouse.IsActive = false;
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.ReceiveReturnAsync(
                new InventoryReturnReceiptRequest(
                    graph.TenantId,
                    returnRequest.Id,
                    returnRequest.RefundId,
                    graph.OrderId,
                    graph.OrderItemId,
                    graph.ProductId,
                    graph.VariantId,
                    graph.OrderQuantity,
                    1m,
                    0m,
                    InventoryEventIdFactory.Create("return-inactive-warehouse", returnRequest.Id),
                    "corr-return",
                    "test")));

        Assert.Equal("INVENTORY_RETURN_STOCK_AUTHORITY_LOST", exception.Code);

        var stock = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == graph.TenantId && item.VariantId == graph.VariantId);
        Assert.Equal(8m, stock.Quantity);
    }

    [Fact]
    public async Task ReceiveReturnAsync_ShouldRejectSupersededVariantReplay()
    {
        var graph = await SeedDeliveredGraphAsync(initialQuantity: 10m, orderQuantity: 2);
        var returnRequest = await SeedReturnRequestAsync(graph, requestedQuantity: 1);

        var variant = await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == graph.VariantId);
        variant.SemanticLifecycleState = ProductVariantSemanticLifecycleState.Detached;
        variant.SemanticSupersededAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.ReceiveReturnAsync(
                new InventoryReturnReceiptRequest(
                    graph.TenantId,
                    returnRequest.Id,
                    returnRequest.RefundId,
                    graph.OrderId,
                    graph.OrderItemId,
                    graph.ProductId,
                    graph.VariantId,
                    graph.OrderQuantity,
                    1m,
                    0m,
                    InventoryEventIdFactory.Create("return-superseded-variant", returnRequest.Id),
                    "corr-return",
                    "test")));

        Assert.Equal("INVENTORY_RETURN_STOCK_AUTHORITY_LOST", exception.Code);

        var stock = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == graph.TenantId && item.VariantId == graph.VariantId);
        Assert.Equal(8m, stock.Quantity);
    }

    [Fact]
    public async Task ReserveOrderItemAsync_ShouldRejectInactiveWarehouseStockAuthority()
    {
        var graph = await SeedInventoryGraphAsync(initialQuantity: 6m, orderQuantity: 2);

        var warehouse = await _dbContext.Warehouses
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == graph.WarehouseId);
        warehouse.IsActive = false;
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.ReserveOrderItemAsync(
                new InventoryReservationRequest(
                    graph.TenantId,
                    graph.OrderId,
                    graph.SubOrderId,
                    graph.OrderItemId,
                    graph.ProductId,
                    graph.VariantId,
                    2m,
                    null,
                    DateTime.UtcNow.AddMinutes(10),
                    InventoryEventIdFactory.Create("reserve-inactive-warehouse", graph.OrderItemId),
                    "corr-reserve",
                    "test")));

        Assert.Equal("MARKETPLACE_RESERVATION_STOCK_AUTHORITY_LOST", exception.Code);
        Assert.Empty(await _dbContext.InventoryReservations.IgnoreQueryFilters().ToListAsync());

        var stock = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == graph.TenantId && item.VariantId == graph.VariantId);
        Assert.Equal(0m, stock.ReservedQuantity);
    }

    [Fact]
    public async Task CommitDeliveryAsync_ShouldAllowCommittedStockAfterProductUnpublish()
    {
        var graph = await SeedInventoryGraphAsync(initialQuantity: 10m, orderQuantity: 2);

        var reserved = await _service.ReserveOrderItemAsync(
            new InventoryReservationRequest(
                graph.TenantId,
                graph.OrderId,
                graph.SubOrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                2m,
                null,
                DateTime.UtcNow.AddMinutes(10),
                InventoryEventIdFactory.Create("reserve-before-unpublish", graph.OrderItemId),
                "corr-reserve",
                "test"));

        Assert.True(reserved);

        var product = await _dbContext.Products
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == graph.ProductId);
        product.IsOnlineVisible = false;
        product.PublishStatus = PublishStatus.Draft;
        await _dbContext.SaveChangesAsync();

        await _service.CommitDeliveryAsync(
            new InventoryDeliveryCommitRequest(
                graph.TenantId,
                graph.OrderId,
                graph.SubOrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                2m,
                InventoryEventIdFactory.Create("delivery-after-unpublish", graph.OrderItemId),
                "corr-delivery",
                "test"));

        var stock = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == graph.TenantId && item.VariantId == graph.VariantId);
        Assert.Equal(8m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
    }

    [Fact]
    public async Task CommitDeliveryAsync_ShouldNotAffectOtherTenantStock()
    {
        var vendorA = await SeedDeliveredGraphAsync(initialQuantity: 5m, orderQuantity: 1);
        var vendorB = await SeedInventoryGraphAsync(initialQuantity: 9m, orderQuantity: 1);

        await _service.CommitDeliveryAsync(
            new InventoryDeliveryCommitRequest(
                vendorA.TenantId,
                vendorA.OrderId,
                vendorA.SubOrderId,
                vendorA.OrderItemId,
                vendorA.ProductId,
                vendorA.VariantId,
                1m,
                InventoryEventIdFactory.Create("vendor-a-delivery", vendorA.OrderItemId),
                "corr-vendor-a",
                "test"));

        var vendorAStock = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(stock => stock.TenantId == vendorA.TenantId);
        var vendorBStock = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(stock => stock.TenantId == vendorB.TenantId);

        Assert.Equal(4m, vendorAStock.Quantity);
        Assert.Equal(9m, vendorBStock.Quantity);
    }

    [Fact]
    public async Task EnsureMarketplaceOrderReservationsActiveAsync_ShouldRejectNonActiveReservationReplay()
    {
        var graph = await SeedInventoryGraphAsync(initialQuantity: 3m, orderQuantity: 2, onlinePayment: true);

        await _service.ReserveOrderItemAsync(
            new InventoryReservationRequest(
                graph.TenantId,
                graph.OrderId,
                graph.SubOrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                2m,
                null,
                DateTime.UtcNow.AddMinutes(10),
                InventoryEventIdFactory.Create("reserve-non-active-replay", graph.OrderItemId),
                "corr-reserve",
                "test"));

        var reservation = await _dbContext.InventoryReservations.SingleAsync();
        reservation.Status = InventoryReservationStatus.Released;
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.EnsureMarketplaceOrderReservationsActiveAsync(graph.OrderId));

        Assert.Equal("MARKETPLACE_ORDER_RESERVATION_MISSING", exception.Code);
    }

    [Fact]
    public async Task EnsureMarketplaceOrderReservationsActiveAsync_ShouldRejectReservationWithoutBackingSqlStock()
    {
        var graph = await SeedInventoryGraphAsync(initialQuantity: 3m, orderQuantity: 2, onlinePayment: true);

        await _service.ReserveOrderItemAsync(
            new InventoryReservationRequest(
                graph.TenantId,
                graph.OrderId,
                graph.SubOrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                2m,
                null,
                DateTime.UtcNow.AddMinutes(10),
                InventoryEventIdFactory.Create("reserve-stock-drift", graph.OrderItemId),
                "corr-reserve",
                "test"));

        var stock = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == graph.TenantId && item.VariantId == graph.VariantId);
        stock.ReservedQuantity = 0m;
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.EnsureMarketplaceOrderReservationsActiveAsync(graph.OrderId));

        Assert.Equal("MARKETPLACE_ORDER_STOCK_AUTHORITY_LOST", exception.Code);
    }

    [Fact]
    public async Task EnsureMarketplaceOrderReservationsActiveAsync_ShouldRejectDuplicateOverReservationReplay()
    {
        var graph = await SeedInventoryGraphAsync(initialQuantity: 5m, orderQuantity: 2, onlinePayment: true);

        await _service.ReserveOrderItemAsync(
            new InventoryReservationRequest(
                graph.TenantId,
                graph.OrderId,
                graph.SubOrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                2m,
                null,
                DateTime.UtcNow.AddMinutes(10),
                InventoryEventIdFactory.Create("reserve-overlap-one", graph.OrderItemId),
                "corr-reserve-one",
                "test"));

        await _service.ReserveOrderItemAsync(
            new InventoryReservationRequest(
                graph.TenantId,
                graph.OrderId,
                graph.SubOrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                2m,
                null,
                DateTime.UtcNow.AddMinutes(10),
                InventoryEventIdFactory.Create("reserve-overlap-two", graph.OrderItemId),
                "corr-reserve-two",
                "test"));

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.EnsureMarketplaceOrderReservationsActiveAsync(graph.OrderId));

        Assert.Equal("MARKETPLACE_ORDER_RESERVATION_OVER_RESERVED", exception.Code);
    }

    [Fact]
    public async Task EnsureMarketplaceOrderReservationsActiveAsync_ShouldRejectSplitReservationOwnershipReplay()
    {
        var graph = await SeedInventoryGraphAsync(initialQuantity: 5m, orderQuantity: 2, onlinePayment: true);

        await _service.ReserveOrderItemAsync(
            new InventoryReservationRequest(
                graph.TenantId,
                graph.OrderId,
                graph.SubOrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                2m,
                null,
                DateTime.UtcNow.AddMinutes(10),
                InventoryEventIdFactory.Create("reserve-owner-valid", graph.OrderItemId),
                "corr-reserve-valid",
                "test"));

        var stock = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == graph.TenantId && item.VariantId == graph.VariantId);
        stock.ReservedQuantity += 1m;
        _dbContext.InventoryReservations.Add(new InventoryReservation
        {
            Id = Guid.NewGuid(),
            TenantId = graph.TenantId,
            OrderId = graph.OrderId,
            SubOrderId = graph.SubOrderId,
            OrderItemId = Guid.NewGuid(),
            ProductId = graph.ProductId,
            VariantId = graph.VariantId,
            WarehouseId = graph.WarehouseId,
            ReservedQuantity = 1m,
            Status = InventoryReservationStatus.Active,
            ReservationEventId = InventoryEventIdFactory.Create("reserve-owner-foreign", graph.OrderItemId),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.EnsureMarketplaceOrderReservationsActiveAsync(graph.OrderId));

        Assert.Equal("MARKETPLACE_ORDER_RESERVATION_OWNERSHIP_CONFLICT", exception.Code);
    }

    [Fact]
    public async Task EnsureMarketplaceOrderReservationsActiveAsync_ShouldAcceptCurrentReservationsAcrossMultipleTenantSubOrders()
    {
        var graph = await SeedMultiTenantMarketplaceReservationAuthorityGraphAsync();

        foreach (var item in graph.Items)
        {
            var reserved = await _service.ReserveOrderItemAsync(
                new InventoryReservationRequest(
                    item.TenantId,
                    graph.OrderId,
                    item.SubOrderId,
                    item.OrderItemId,
                    item.ProductId,
                    item.VariantId,
                    item.Quantity,
                    null,
                    DateTime.UtcNow.AddMinutes(10),
                    InventoryEventIdFactory.Create("reserve-multi-tenant", item.OrderItemId),
                    $"corr-{item.OrderItemId:N}",
                    "test"));

            Assert.True(reserved);
        }

        await _service.EnsureMarketplaceOrderReservationsActiveAsync(graph.OrderId);
    }

    [Fact]
    public async Task CommitDeliveryAsync_ShouldRejectExpiredReservationReplay()
    {
        var graph = await SeedInventoryGraphAsync(initialQuantity: 3m, orderQuantity: 2);

        await _service.ReserveOrderItemAsync(
            new InventoryReservationRequest(
                graph.TenantId,
                graph.OrderId,
                graph.SubOrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                2m,
                null,
                DateTime.UtcNow.AddMinutes(-1),
                InventoryEventIdFactory.Create("reserve-expired-delivery", graph.OrderItemId),
                "corr-reserve",
                "test"));

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.CommitDeliveryAsync(
                new InventoryDeliveryCommitRequest(
                    graph.TenantId,
                    graph.OrderId,
                    graph.SubOrderId,
                    graph.OrderItemId,
                    graph.ProductId,
                    graph.VariantId,
                    2m,
                    InventoryEventIdFactory.Create("delivery-expired-replay", graph.OrderItemId),
                    "corr-delivery",
                    "test")));

        Assert.Equal("INVENTORY_DELIVERY_REQUIRES_ACTIVE_RESERVATION", exception.Code);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task<InventoryGraph> SeedInventoryGraphAsync(decimal initialQuantity, int orderQuantity, bool onlinePayment = false)
    {
        var tenantId = Guid.NewGuid();
        var utcNow = DateTime.UtcNow;
        var warehouseId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        var orderItemId = Guid.NewGuid();

        _dbContext.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Inventory Test Warehouse",
            IsActive = true,
            CreatedAt = utcNow
        });

        _dbContext.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Inventory Test Category",
            CreatedAt = utcNow
        });

        _dbContext.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Inventory Test Product",
            CategoryId = categoryId,
            SalePrice = 100m,
            OnlinePrice = 100m,
            PurchasePrice = 60m,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            TrackStock = true,
            CreatedAt = utcNow
        });

        _dbContext.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = "INV-CONSISTENCY-001",
            Price = 100m,
            OnlinePrice = 100m,
            CostPrice = 60m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = utcNow
        });

        _dbContext.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variantId,
            WarehouseId = warehouseId,
            Quantity = initialQuantity,
            ReservedQuantity = 0m,
            DamagedQuantity = 0m,
            CreatedAt = utcNow
        });

        var customer = CommerceHubCustomer.Create(
            $"inventory-{Guid.NewGuid():N}@example.com",
            "Inventory Consistency Customer",
            "01011111111",
            "hash");

        var order = new CommerceHubMarketplaceOrder
        {
            Id = orderId,
            CustomerId = customer.Id,
            Customer = customer,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..12],
            FullName = "Inventory Consistency Customer",
            PhoneNumber = "01011111111",
            AddressLine = "123 Test Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = onlinePayment ? "STRIPE" : "COD",
            PaymentMethodName = onlinePayment ? "Stripe" : "Cash on delivery",
            Subtotal = orderQuantity * 100m,
            ShippingTotal = 0m,
            TotalAmount = orderQuantity * 100m,
            Status = OrderStatus.Shipped,
            PaymentStatus = onlinePayment ? PaymentStatus.Pending : PaymentStatus.CashOnDelivery
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = subOrderId,
            MarketplaceOrderId = orderId,
            MarketplaceOrder = order,
            OrderNumber = $"SUB-{Guid.NewGuid():N}"[..12],
            StoreId = tenantId,
            StoreName = "Inventory Test Store",
            Currency = "EGP",
            Subtotal = orderQuantity * 100m,
            Shipping = 0m,
            PaymentMethod = onlinePayment ? CommerceHubPaymentMethod.Online : CommerceHubPaymentMethod.CashOnDelivery,
            PaymentMethodCode = onlinePayment ? "STRIPE" : "COD",
            PaymentMethodName = onlinePayment ? "Stripe" : "Cash on delivery",
            Total = orderQuantity * 100m,
            Status = OrderStatus.Shipped,
            PaymentStatus = onlinePayment ? PaymentStatus.Pending : PaymentStatus.CashOnDelivery
        };

        var orderItem = new CommerceHubSubOrderItem
        {
            Id = orderItemId,
            SubOrderId = subOrderId,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = productId,
            VariantId = variantId,
            ProductName = "Inventory Test Product",
            Quantity = orderQuantity,
            UnitPrice = 100m,
            Total = orderQuantity * 100m,
            Status = CommerceHubOrderItemStatus.Shipped,
            CreatedAt = utcNow
        };

        subOrder.Items.Add(orderItem);
        order.SubOrders.Add(subOrder);
        _dbContext.CommerceHubCustomers.Add(customer);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        {
            await _dbContext.SaveChangesAsync();
        }

        return new InventoryGraph(
            tenantId,
            warehouseId,
            productId,
            variantId,
            orderId,
            subOrderId,
            orderItemId,
            orderQuantity);
    }

    private async Task<MultiTenantInventoryGraph> SeedMultiTenantMarketplaceReservationAuthorityGraphAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var orderId = Guid.NewGuid();
        var customer = CommerceHubCustomer.Create(
            $"multi-tenant-{Guid.NewGuid():N}@example.com",
            "Multi Tenant Inventory Customer",
            "01012223333",
            "hash");

        var order = new CommerceHubMarketplaceOrder
        {
            Id = orderId,
            CustomerId = customer.Id,
            Customer = customer,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..12],
            FullName = "Multi Tenant Inventory Customer",
            PhoneNumber = "01012223333",
            AddressLine = "456 Shared Market Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "STRIPE",
            PaymentMethodName = "Stripe",
            Subtotal = 300m,
            ShippingTotal = 0m,
            TotalAmount = 300m,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        };

        var items = new List<MultiTenantInventoryItem>();
        _dbContext.CommerceHubCustomers.Add(customer);

        for (var index = 0; index < 2; index++)
        {
            var tenantId = Guid.NewGuid();
            var warehouseId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var variantId = Guid.NewGuid();
            var subOrderId = Guid.NewGuid();
            var orderItemId = Guid.NewGuid();
            var quantity = index + 1;

            _dbContext.Warehouses.Add(new Warehouse
            {
                Id = warehouseId,
                TenantId = tenantId,
                Name = $"Inventory Tenant {index + 1} Warehouse",
                IsActive = true,
                CreatedAt = nowUtc
            });

            _dbContext.Categories.Add(new Category
            {
                Id = categoryId,
                TenantId = tenantId,
                Name = $"Inventory Tenant {index + 1} Category",
                CreatedAt = nowUtc
            });

            _dbContext.Products.Add(new Product
            {
                Id = productId,
                TenantId = tenantId,
                Name = $"Inventory Tenant {index + 1} Product",
                CategoryId = categoryId,
                SalePrice = 100m + (index * 50m),
                OnlinePrice = 100m + (index * 50m),
                PurchasePrice = 60m,
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                TrackStock = true,
                CreatedAt = nowUtc
            });

            _dbContext.ProductVariants.Add(new ProductVariant
            {
                Id = variantId,
                TenantId = tenantId,
                ProductId = productId,
                SKU = $"INV-MULTI-{index + 1}-{Guid.NewGuid():N}"[..20],
                Price = 100m + (index * 50m),
                OnlinePrice = 100m + (index * 50m),
                CostPrice = 60m,
                IsDefault = true,
                IsActive = true,
                CreatedAt = nowUtc
            });

            _dbContext.ProductVariantStocks.Add(new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantId,
                WarehouseId = warehouseId,
                Quantity = 10m,
                ReservedQuantity = 0m,
                DamagedQuantity = 0m,
                CreatedAt = nowUtc
            });

            var subOrder = new CommerceHubSubOrder
            {
                Id = subOrderId,
                MarketplaceOrderId = orderId,
                MarketplaceOrder = order,
                OrderNumber = $"SUB-{Guid.NewGuid():N}"[..12],
                StoreId = tenantId,
                StoreName = $"Inventory Tenant {index + 1} Store",
                Currency = "EGP",
                Subtotal = quantity * (100m + (index * 50m)),
                Shipping = 0m,
                PaymentMethod = CommerceHubPaymentMethod.Online,
                PaymentMethodCode = "STRIPE",
                PaymentMethodName = "Stripe",
                Total = quantity * (100m + (index * 50m)),
                Status = OrderStatus.Pending,
                PaymentStatus = PaymentStatus.Pending
            };

            var orderItem = new CommerceHubSubOrderItem
            {
                Id = orderItemId,
                SubOrderId = subOrderId,
                SubOrder = subOrder,
                ListingId = Guid.NewGuid(),
                ProductId = productId,
                VariantId = variantId,
                ProductName = $"Inventory Tenant {index + 1} Product",
                Quantity = quantity,
                UnitPrice = 100m + (index * 50m),
                Total = quantity * (100m + (index * 50m)),
                Status = CommerceHubOrderItemStatus.Pending,
                CreatedAt = nowUtc
            };

            subOrder.Items.Add(orderItem);
            order.SubOrders.Add(subOrder);

            items.Add(new MultiTenantInventoryItem(
                tenantId,
                warehouseId,
                productId,
                variantId,
                subOrderId,
                orderItemId,
                quantity));
        }

        _dbContext.CommerceHubMarketplaceOrders.Add(order);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        {
            await _dbContext.SaveChangesAsync();
        }

        return new MultiTenantInventoryGraph(orderId, items);
    }

    private async Task<InventoryGraph> SeedDeliveredGraphAsync(decimal initialQuantity, int orderQuantity)
    {
        var graph = await SeedInventoryGraphAsync(initialQuantity, orderQuantity);

        await _service.ReserveOrderItemAsync(
            new InventoryReservationRequest(
                graph.TenantId,
                graph.OrderId,
                graph.SubOrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                orderQuantity,
                null,
                null,
                InventoryEventIdFactory.Create("seed-reserve", graph.OrderItemId),
                "corr-seed-reserve",
                "test"));

        await _service.CommitDeliveryAsync(
            new InventoryDeliveryCommitRequest(
                graph.TenantId,
                graph.OrderId,
                graph.SubOrderId,
                graph.OrderItemId,
                graph.ProductId,
                graph.VariantId,
                orderQuantity,
                InventoryEventIdFactory.Create("seed-delivery", graph.OrderItemId),
                "corr-seed-delivery",
                "test"));

        var item = await _dbContext.CommerceHubSubOrderItems.SingleAsync(orderItem => orderItem.Id == graph.OrderItemId);
        item.Status = CommerceHubOrderItemStatus.Delivered;
        item.DeliveredAt = DateTime.UtcNow;
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            await _dbContext.SaveChangesAsync();
        }

        return graph;
    }

    private async Task<CommerceHubReturnRequest> SeedReturnRequestAsync(InventoryGraph graph, int requestedQuantity)
    {
        var order = _dbContext.CommerceHubMarketplaceOrders.Local
            .FirstOrDefault(item => item.Id == graph.OrderId);
        var customer = order?.Customer;

        if (order is null)
        {
            order = await _dbContext.CommerceHubMarketplaceOrders
                .Include(item => item.Customer)
                .Include(item => item.SubOrders)
                .SingleAsync(item => item.Id == graph.OrderId);
            customer = order.Customer;
        }

        if (customer is null)
        {
            throw new InvalidOperationException("Inventory return test customer was not initialized.");
        }

        var refundRequest = new CommerceHubRefundRequest
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            OrderItemId = graph.OrderItemId,
            OrderItem = await _dbContext.CommerceHubSubOrderItems.SingleAsync(item => item.Id == graph.OrderItemId),
            CustomerId = customer.Id,
            Customer = customer,
            VendorId = graph.TenantId,
            Quantity = requestedQuantity,
            UnitPriceSnapshot = 100m,
            TotalAmount = requestedQuantity * 100m,
            Reason = "Inventory test return",
            ReasonCode = RefundReasonCode.Other,
            Status = CommerceHubRefundRequestStatus.Approved,
            RefundExecutionTrigger = RefundExecutionTrigger.OnVendorConfirmation
        };

        var returnRequest = new CommerceHubReturnRequest
        {
            Id = Guid.NewGuid(),
            RefundId = refundRequest.Id,
            Refund = refundRequest,
            OrderItemId = graph.OrderItemId,
            OrderItem = refundRequest.OrderItem,
            CustomerId = customer.Id,
            Customer = customer,
            VendorId = graph.TenantId,
            Status = CommerceHubReturnStatus.DeliveredToVendor,
            DeliveredAt = DateTime.UtcNow.AddMinutes(-5),
            RequestedQuantity = requestedQuantity
        };

        refundRequest.ReturnRequestId = returnRequest.Id;
        _dbContext.CommerceHubRefundRequests.Add(refundRequest);
        _dbContext.CommerceHubReturnRequests.Add(returnRequest);
        
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            await _dbContext.SaveChangesAsync();
        }

        return returnRequest;
    }

    private sealed record InventoryGraph(
        Guid TenantId,
        Guid WarehouseId,
        Guid ProductId,
        Guid VariantId,
        Guid OrderId,
        Guid SubOrderId,
        Guid OrderItemId,
        int OrderQuantity);

    private sealed record MultiTenantInventoryGraph(
        Guid OrderId,
        IReadOnlyList<MultiTenantInventoryItem> Items);

    private sealed record MultiTenantInventoryItem(
        Guid TenantId,
        Guid WarehouseId,
        Guid ProductId,
        Guid VariantId,
        Guid SubOrderId,
        Guid OrderItemId,
        int Quantity);
}
