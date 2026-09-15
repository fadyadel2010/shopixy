using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Data;
using Xunit;

namespace PointOfSale.Tests.CommerceHub;

/// <summary>
/// T-08 Part 21: CommerceHub delivery-commit → COGS event and return-receipt →
/// COGS-reversal proofs. Delivery commits stock and stages one CH_COGS event at
/// the frozen checkout snapshot; replays are idempotent; cost-unknown history
/// delivers without a COGS leg; tenant/binding mismatches fail closed.
/// </summary>
public sealed class T08CommerceHubCogsTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly InventoryConsistencyService _service;

    public T08CommerceHubCogsTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _service = new InventoryConsistencyService(
            _dbContext,
            NullLogger<InventoryConsistencyService>.Instance);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task CommitDeliveryAsync_WithSnapshot_StagesOneCogsEvent()
    {
        var graph = await SeedGraphAsync(initialQuantity: 10m, orderQuantity: 3, unitCost: 60m, captured: true);
        await ReserveAsync(graph, 3m);
        var eventId = InventoryEventIdFactory.Create("delivery", graph.OrderItemId);

        await _service.CommitDeliveryAsync(DeliveryRequest(graph, 3m, eventId));

        var message = Assert.Single(
            await _dbContext.AccountingOutboxMessages.IgnoreQueryFilters().ToListAsync());
        Assert.Equal("CommerceHubCogs", message.EventType);
        Assert.Equal(
            EcommerceCogsPolicy.BuildCommerceHubCogsKey(graph.TenantId, graph.SubOrderId, eventId.ToString("D")),
            message.IdempotencyKey);
        Assert.Equal("CommerceHub_COGS", message.SourceType);
        var payload = System.Text.Json.JsonSerializer.Deserialize<CommerceHubCogsOutboxPayload>(message.PayloadJson);
        Assert.NotNull(payload);
        Assert.Equal(60m, payload!.UnitCostSnapshot);
        Assert.Equal(180m, payload.CostAmount);

        var stock = await _dbContext.ProductVariantStocks.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(7m, stock.Quantity);
    }

    [Fact]
    public async Task CommitDeliveryAsync_Replay_IsIdempotent()
    {
        var graph = await SeedGraphAsync(initialQuantity: 10m, orderQuantity: 3, unitCost: 60m, captured: true);
        await ReserveAsync(graph, 3m);
        var eventId = InventoryEventIdFactory.Create("delivery", graph.OrderItemId);
        var request = DeliveryRequest(graph, 3m, eventId);

        await _service.CommitDeliveryAsync(request);
        await _service.CommitDeliveryAsync(request);

        Assert.Equal(
            1,
            await _dbContext.AccountingOutboxMessages.IgnoreQueryFilters().CountAsync());
        var stock = await _dbContext.ProductVariantStocks.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(7m, stock.Quantity);
    }

    [Fact]
    public async Task CommitDeliveryAsync_WithoutSnapshot_CommitsStockWithoutCogs()
    {
        var graph = await SeedGraphAsync(initialQuantity: 10m, orderQuantity: 3, unitCost: 0m, captured: false);
        await ReserveAsync(graph, 3m);

        await _service.CommitDeliveryAsync(
            DeliveryRequest(graph, 3m, InventoryEventIdFactory.Create("delivery", graph.OrderItemId)));

        Assert.Empty(await _dbContext.AccountingOutboxMessages.IgnoreQueryFilters().ToListAsync());
        var stock = await _dbContext.ProductVariantStocks.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(7m, stock.Quantity);
    }

    [Fact]
    public async Task CommitDeliveryAsync_WithCapturedZeroCost_StagesProcessedLineageEvent()
    {
        var graph = await SeedGraphAsync(initialQuantity: 10m, orderQuantity: 3, unitCost: 0m, captured: true);
        await ReserveAsync(graph, 3m);

        await _service.CommitDeliveryAsync(
            DeliveryRequest(graph, 3m, InventoryEventIdFactory.Create("delivery", graph.OrderItemId)));

        var message = Assert.Single(
            await _dbContext.AccountingOutboxMessages.IgnoreQueryFilters().ToListAsync());
        var payload = System.Text.Json.JsonSerializer.Deserialize<CommerceHubCogsOutboxPayload>(message.PayloadJson);
        Assert.NotNull(payload);
        Assert.Equal(0m, payload!.UnitCostSnapshot);
        Assert.Equal(0m, payload.CostAmount);
    }

    [Fact]
    public async Task CommitDeliveryAsync_WithInconsistentCapturedAmount_FailsClosed()
    {
        var graph = await SeedGraphAsync(initialQuantity: 10m, orderQuantity: 3, unitCost: 60m, captured: true);
        await ReserveAsync(graph, 3m);
        var item = await _dbContext.CommerceHubSubOrderItems.IgnoreQueryFilters().SingleAsync();
        item.CostAmountSnapshot = 999m;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CommitDeliveryAsync(
            DeliveryRequest(graph, 3m, InventoryEventIdFactory.Create("delivery", graph.OrderItemId))));

        Assert.Empty(await _dbContext.AccountingOutboxMessages.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task CommitDeliveryAsync_TenantMismatch_FailsClosed()
    {
        var graph = await SeedGraphAsync(initialQuantity: 10m, orderQuantity: 1, unitCost: 60m, captured: true);
        await ReserveAsync(graph, 1m);

        var badTenant = Guid.NewGuid();
        await Assert.ThrowsAsync<CommerceHubCheckoutException>(() => _service.CommitDeliveryAsync(
            new InventoryDeliveryCommitRequest(
                badTenant, graph.OrderId, graph.SubOrderId, graph.OrderItemId,
                graph.ProductId, graph.VariantId, 1m,
                InventoryEventIdFactory.Create("delivery", graph.OrderItemId),
                "corr", "test")));
    }

    [Fact]
    public async Task ReceiveReturnAsync_WithSnapshot_StagesOneReversal()
    {
        var graph = await SeedGraphAsync(initialQuantity: 10m, orderQuantity: 3, unitCost: 60m, captured: true);
        await ReserveAsync(graph, 3m);
        await _service.CommitDeliveryAsync(
            DeliveryRequest(graph, 3m, InventoryEventIdFactory.Create("delivery", graph.OrderItemId)));
        var returnId = await SeedReturnRequestAsync(graph, requested: 3);
        var refundId = (await _dbContext.CommerceHubReturnRequests.FindAsync(returnId))!.RefundId;

        await _service.ReceiveReturnAsync(new InventoryReturnReceiptRequest(
            graph.TenantId, returnId, refundId, graph.OrderId, graph.OrderItemId,
            graph.ProductId, graph.VariantId, 3m, 2m, 0m,
            InventoryEventIdFactory.Create("return", returnId), "corr-return", "test"));

        var reversal = await _dbContext.AccountingOutboxMessages.IgnoreQueryFilters()
            .SingleOrDefaultAsync(m => m.EventType == "CommerceHubCogsReversal");
        Assert.NotNull(reversal);
        Assert.Equal(
            EcommerceCogsPolicy.BuildCommerceHubCogsReversalKey(graph.TenantId, graph.SubOrderId, returnId.ToString("D")),
            reversal!.IdempotencyKey);
        var payload = System.Text.Json.JsonSerializer.Deserialize<CommerceHubCogsReversalOutboxPayload>(reversal.PayloadJson);
        Assert.NotNull(payload);
        Assert.Equal(2m, payload!.ResellableQuantity);
        Assert.Equal(120m, payload.ReversalAmount);

        var stock = await _dbContext.ProductVariantStocks.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(9m, stock.Quantity);
    }

    [Fact]
    public async Task ReceiveReturnAsync_DamagedOnly_StagesNoReversal()
    {
        var graph = await SeedGraphAsync(initialQuantity: 10m, orderQuantity: 3, unitCost: 60m, captured: true);
        await ReserveAsync(graph, 3m);
        await _service.CommitDeliveryAsync(
            DeliveryRequest(graph, 3m, InventoryEventIdFactory.Create("delivery", graph.OrderItemId)));
        var returnId = await SeedReturnRequestAsync(graph, requested: 1);
        var refundId = (await _dbContext.CommerceHubReturnRequests.FindAsync(returnId))!.RefundId;

        await _service.ReceiveReturnAsync(new InventoryReturnReceiptRequest(
            graph.TenantId, returnId, refundId, graph.OrderId, graph.OrderItemId,
            graph.ProductId, graph.VariantId, 3m, 0m, 1m,
            InventoryEventIdFactory.Create("return", returnId), "corr-return", "test"));

        Assert.Empty(await _dbContext.AccountingOutboxMessages.IgnoreQueryFilters()
            .Where(m => m.EventType == "CommerceHubCogsReversal").ToListAsync());
    }

    [Fact]
    public async Task ReceiveReturnAsync_Replay_IsIdempotent()
    {
        var graph = await SeedGraphAsync(initialQuantity: 10m, orderQuantity: 2, unitCost: 60m, captured: true);
        await ReserveAsync(graph, 2m);
        await _service.CommitDeliveryAsync(
            DeliveryRequest(graph, 2m, InventoryEventIdFactory.Create("delivery", graph.OrderItemId)));
        var returnId = await SeedReturnRequestAsync(graph, requested: 2);
        var refundId = (await _dbContext.CommerceHubReturnRequests.FindAsync(returnId))!.RefundId;
        var request = new InventoryReturnReceiptRequest(
            graph.TenantId, returnId, refundId, graph.OrderId, graph.OrderItemId,
            graph.ProductId, graph.VariantId, 2m, 2m, 0m,
            InventoryEventIdFactory.Create("return", returnId), "corr-return", "test");

        await _service.ReceiveReturnAsync(request);

        // Same return EventId replays through the inventory-event inbox and must
        // not duplicate stock, movements, or the COGS reversal.
        await _service.ReceiveReturnAsync(request);

        Assert.Equal(
            1,
            await _dbContext.AccountingOutboxMessages.IgnoreQueryFilters()
                .CountAsync(m => m.EventType == "CommerceHubCogsReversal"));
        var stock = await _dbContext.ProductVariantStocks.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(10m, stock.Quantity);
    }

    private async Task ReserveAsync(Graph graph, decimal quantity)
    {
        var reserved = await _service.ReserveOrderItemAsync(
            new InventoryReservationRequest(
                graph.TenantId, graph.OrderId, graph.SubOrderId, graph.OrderItemId,
                graph.ProductId, graph.VariantId, quantity, null, null,
                InventoryEventIdFactory.Create("reserve", graph.OrderItemId),
                "corr-reserve", "test"));
        Assert.True(reserved);
    }

    private static InventoryDeliveryCommitRequest DeliveryRequest(Graph graph, decimal quantity, Guid eventId) =>
        new(graph.TenantId, graph.OrderId, graph.SubOrderId, graph.OrderItemId,
            graph.ProductId, graph.VariantId, quantity, eventId, "corr-delivery", "test");

    private async Task<Guid> SeedReturnRequestAsync(Graph graph, int requested)
    {
        var returnId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        _dbContext.CommerceHubRefundRequests.Add(new CommerceHubRefundRequest
        {
            Id = refundId,
            OrderId = graph.OrderId,
            OrderItemId = graph.OrderItemId,
            CustomerId = customerId,
            VendorId = graph.TenantId,
            Quantity = requested,
            UnitPriceSnapshot = 100m,
            TotalAmount = requested * 100m,
            Reason = "T-08 return",
            Status = CommerceHubRefundRequestStatus.WaitingForReturn
        });
        _dbContext.CommerceHubReturnRequests.Add(new CommerceHubReturnRequest
        {
            Id = returnId,
            RefundId = refundId,
            OrderItemId = graph.OrderItemId,
            CustomerId = customerId,
            VendorId = graph.TenantId,
            Status = CommerceHubReturnStatus.Pending,
            RequestedQuantity = requested,
            ReceivedResellableQuantity = 0,
            ReceivedDamagedQuantity = 0
        });

        using (CommerceHubBoundaryMutationScope.BeginRefund())
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            await _dbContext.SaveChangesAsync();
        }

        return returnId;
    }

    private sealed record Graph(
        Guid TenantId,
        Guid OrderId,
        Guid SubOrderId,
        Guid OrderItemId,
        Guid ProductId,
        Guid VariantId);

    private async Task<Graph> SeedGraphAsync(
        decimal initialQuantity, int orderQuantity, decimal unitCost, bool captured)
    {
        var utcNow = DateTime.UtcNow;
        var tenantId = Guid.NewGuid();
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
            Name = "T-08 CH Warehouse",
            IsActive = true,
            CreatedAt = utcNow
        });
        _dbContext.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "T-08 CH Category",
            CreatedAt = utcNow
        });
        _dbContext.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "T-08 CH Product",
            CategoryId = categoryId,
            SalePrice = 100m,
            PurchasePrice = unitCost,
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
            SKU = $"T08CH-{Guid.NewGuid():N}"[..12],
            Price = 100m,
            OnlinePrice = 100m,
            CostPrice = unitCost,
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

        var marketplaceOrder = new CommerceHubMarketplaceOrder
        {
            Id = orderId,
            OrderNumber = $"CH-T08-{Guid.NewGuid():N}"[..12],
            Currency = "EGP",
            Status = OrderStatus.Shipped,
            PaymentStatus = PaymentStatus.CashOnDelivery
        };
        var subOrder = new CommerceHubSubOrder
        {
            Id = subOrderId,
            MarketplaceOrderId = orderId,
            MarketplaceOrder = marketplaceOrder,
            OrderNumber = $"SUB-T08-{Guid.NewGuid():N}"[..12],
            StoreId = tenantId,
            StoreName = "T-08 CH Store",
            Currency = "EGP",
            Status = OrderStatus.Shipped,
            PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery,
            PaymentStatus = PaymentStatus.CashOnDelivery
        };
        var item = new CommerceHubSubOrderItem
        {
            Id = orderItemId,
            SubOrderId = subOrderId,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = productId,
            VariantId = variantId,
            CategoryId = categoryId,
            ProductName = "T-08 CH Product",
            Quantity = orderQuantity,
            UnitPrice = 100m,
            Total = orderQuantity * 100m,
            UnitCostSnapshot = unitCost,
            CostAmountSnapshot = unitCost * orderQuantity,
            CostSnapshotCapturedAtUtc = captured ? utcNow : null,
            Status = CommerceHubOrderItemStatus.Shipped,
            CreatedAt = utcNow
        };
        subOrder.Items.Add(item);
        marketplaceOrder.SubOrders.Add(subOrder);
        _dbContext.CommerceHubMarketplaceOrders.Add(marketplaceOrder);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        {
            await _dbContext.SaveChangesAsync();
        }

        return new Graph(tenantId, orderId, subOrderId, orderItemId, productId, variantId);
    }
}
