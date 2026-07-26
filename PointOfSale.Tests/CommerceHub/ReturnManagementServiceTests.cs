using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.Tests.CommerceHub;

public sealed class ReturnManagementServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IInventoryConsistencyService> _inventoryConsistencyService = new();
    private readonly ReturnManagementService _service;

    public ReturnManagementServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _service = new ReturnManagementService(_dbContext, _inventoryConsistencyService.Object);
    }

    [Fact]
    public async Task EnsureReturnRequestForApprovedRefundAsync_ShouldCreatePendingReturn_AndLinkRefund()
    {
        var graph = await SeedRefundAsync(CommerceHubRefundRequestStatus.Approved, RefundExecutionTrigger.OnPickup);

        await _service.EnsureReturnRequestForApprovedRefundAsync(
            graph.RefundRequest,
            Guid.NewGuid(),
            "owner@commercehub.local");
        await _dbContext.SaveChangesAsync();

        var persistedRefund = await _dbContext.CommerceHubRefundRequests.SingleAsync();
        var persistedReturn = await _dbContext.CommerceHubReturnRequests.SingleAsync();

        Assert.Equal(persistedReturn.Id, persistedRefund.ReturnRequestId);
        Assert.Equal(graph.RefundRequest.Id, persistedReturn.RefundId);
        Assert.Equal(CommerceHubReturnStatus.Pending, persistedReturn.Status);
    }

    [Fact]
    public async Task MarkDeliveredAsync_ShouldReject_WhenReturnHasNotReachedInTransit()
    {
        var graph = await SeedRefundAsync(CommerceHubRefundRequestStatus.Approved, RefundExecutionTrigger.OnDelivery);
        var returnRequest = await SeedReturnAsync(graph, CommerceHubReturnStatus.PickupScheduled);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.MarkDeliveredAsync(
                returnRequest.Id,
                DateTime.UtcNow,
                "TRACK-123",
                "Bosta",
                "Skipped ahead",
                Guid.NewGuid(),
                "owner@commercehub.local",
                "corr-delivered"));

        Assert.Equal("RETURN_TRANSITION_NOT_ALLOWED", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Fact]
    public async Task MarkPickedUpAsync_ShouldAdvanceWorkflow_WhenTriggerIsOnPickup()
    {
        var graph = await SeedRefundAsync(CommerceHubRefundRequestStatus.Approved, RefundExecutionTrigger.OnPickup);
        var returnRequest = await SeedReturnAsync(graph, CommerceHubReturnStatus.PickupScheduled, pickupScheduledAtUtc: DateTime.UtcNow.AddHours(-2));

        var result = await _service.MarkPickedUpAsync(
            returnRequest.Id,
            DateTime.UtcNow,
            "TRACK-PICKUP",
            "Bosta",
            "Courier collected the parcel.",
            Guid.NewGuid(),
            "owner@commercehub.local",
            "corr-picked-up");

        Assert.Equal(CommerceHubReturnStatus.PickedUp, result.Status);
        Assert.True(result.IsExecutionTriggerReached);

    }

    [Fact]
    public async Task MarkPickedUpAsync_ShouldRejectVendorLogisticsSource()
    {
        var graph = await SeedRefundAsync(CommerceHubRefundRequestStatus.Approved, RefundExecutionTrigger.OnPickup);
        var returnRequest = await SeedReturnAsync(graph, CommerceHubReturnStatus.PickupScheduled, pickupScheduledAtUtc: DateTime.UtcNow.AddHours(-2));

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.MarkPickedUpAsync(
                returnRequest.Id,
                DateTime.UtcNow,
                "TRACK-PICKUP",
                "Bosta",
                "Vendor attempted to advance logistics.",
                Guid.NewGuid(),
                "vendor@store.local",
                "corr-picked-up",
                source: "PosAdminAPI"));

        Assert.Equal("RETURN_LOGISTICS_WORKFLOW_REQUIRED", exception.Code);
        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task ConfirmReceiptAsync_ShouldAllowVendorReceiptAuthority_WhenDeliveredToVendor()
    {
        var graph = await SeedRefundAsync(CommerceHubRefundRequestStatus.Approved, RefundExecutionTrigger.OnVendorConfirmation);
        var returnRequest = await SeedReturnAsync(graph, CommerceHubReturnStatus.DeliveredToVendor, deliveredAtUtc: DateTime.UtcNow.AddHours(-1));

        var result = await _service.ConfirmReceiptAsync(
            returnRequest.Id,
            graph.SubOrder.StoreId,
            DateTime.UtcNow,
            "Vendor confirmed receipt after platform delivery evidence.",
            resellableQuantity: 1,
            damagedQuantity: 0,
            Guid.NewGuid(),
            "vendor@store.local",
            "corr-confirmed",
            source: "PosAdminAPI");

        Assert.Equal(CommerceHubReturnStatus.Confirmed, result.Status);
    }

    [Fact]
    public async Task ConfirmReceiptAsync_ShouldApplyInventoryReceipt_WhenTriggerIsOnVendorConfirmation()
    {
        var graph = await SeedRefundAsync(CommerceHubRefundRequestStatus.Approved, RefundExecutionTrigger.OnVendorConfirmation);
        var returnRequest = await SeedReturnAsync(graph, CommerceHubReturnStatus.DeliveredToVendor, deliveredAtUtc: DateTime.UtcNow.AddHours(-1));

        var result = await _service.ConfirmReceiptAsync(
            returnRequest.Id,
            graph.SubOrder.StoreId,
            DateTime.UtcNow,
            "Warehouse accepted the product.",
            resellableQuantity: 1,
            damagedQuantity: 0,
            Guid.NewGuid(),
            "vendor@store.local",
            "corr-confirmed");

        Assert.Equal(CommerceHubReturnStatus.Confirmed, result.Status);
        Assert.True(result.IsExecutionTriggerReached);

        _inventoryConsistencyService.Verify(
            service => service.ReceiveReturnAsync(
                It.Is<InventoryReturnReceiptRequest>(request =>
                    request.ReturnId == returnRequest.Id &&
                    request.OrderItemId == graph.Item.Id &&
                    request.ResellableQuantity == 1 &&
                    request.DamagedQuantity == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MarkFailedAsync_ShouldMarkReturnFailed_WithoutTriggeringRefundExecution()
    {
        var graph = await SeedRefundAsync(CommerceHubRefundRequestStatus.Approved, RefundExecutionTrigger.OnDelivery);
        var returnRequest = await SeedReturnAsync(graph, CommerceHubReturnStatus.InTransit, pickupScheduledAtUtc: DateTime.UtcNow.AddHours(-4));

        var result = await _service.MarkFailedAsync(
            returnRequest.Id,
            DateTime.UtcNow,
            "TRACK-FAILED",
            "Bosta",
            "Courier failed to deliver the item back to the vendor.",
            Guid.NewGuid(),
            "owner@commercehub.local",
            "corr-failed");

        Assert.Equal(CommerceHubReturnStatus.Failed, result.Status);
        Assert.False(result.IsExecutionTriggerReached);

    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task<RefundGraph> SeedRefundAsync(
        CommerceHubRefundRequestStatus refundStatus,
        RefundExecutionTrigger executionTrigger)
    {
        var customer = CommerceHubCustomer.Create("return-tests@example.com", "Return Test", "01011111111", "hash");

        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Customer = customer,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..12],
            FullName = "Return Test",
            PhoneNumber = "01011111111",
            AddressLine = "123 Return Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on delivery",
            Subtotal = 400m,
            ShippingTotal = 20m,
            TotalAmount = 420m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Collected
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = $"SUB-{Guid.NewGuid():N}"[..12],
            StoreId = Guid.NewGuid(),
            StoreName = "Vendor Return Tests",
            Currency = "EGP",
            Subtotal = 400m,
            Shipping = 20m,
            PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery,
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on delivery",
            Total = 420m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Collected
        };

        var item = new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Return Workflow Product",
            VariantName = "Black / Medium",
            Quantity = 2,
            UnitPrice = 200m,
            PlatformCommissionAmount = 20m,
            Total = 400m,
            Status = CommerceHubOrderItemStatus.Delivered,
            DeliveredAt = DateTime.UtcNow.AddDays(-1)
        };

        var refundRequest = new CommerceHubRefundRequest
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            OrderItemId = item.Id,
            OrderItem = item,
            CustomerId = customer.Id,
            Customer = customer,
            VendorId = subOrder.StoreId,
            Quantity = 1,
            UnitPriceSnapshot = 200m,
            TotalAmount = 200m,
            Reason = "Return workflow reason",
            ReasonCode = RefundReasonCode.DamagedItem,
            Status = refundStatus,
            RefundExecutionTrigger = executionTrigger,
            PayoutMethodSnapshot = RefundPayoutMethod.VodafoneCash,
            PayoutAccountSnapshot = "ENC:return-test",
            PayoutAccountMaskedSnapshot = "010****678",
            PayoutAccountHolderNameSnapshot = "Return Test"
        };

        order.SubOrders.Add(subOrder);
        subOrder.Items.Add(item);

        _dbContext.CommerceHubCustomers.Add(customer);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubRefundRequests.Add(refundRequest);
        await SaveCommerceHubFixtureAsync();

        return new RefundGraph(customer, order, subOrder, item, refundRequest);
    }

    private async Task<CommerceHubReturnRequest> SeedReturnAsync(
        RefundGraph graph,
        CommerceHubReturnStatus status,
        DateTime? pickupScheduledAtUtc = null,
        DateTime? deliveredAtUtc = null)
    {
        var returnRequest = new CommerceHubReturnRequest
        {
            Id = Guid.NewGuid(),
            RefundId = graph.RefundRequest.Id,
            Refund = graph.RefundRequest,
            OrderItemId = graph.Item.Id,
            OrderItem = graph.Item,
            CustomerId = graph.Customer.Id,
            Customer = graph.Customer,
            VendorId = graph.SubOrder.StoreId,
            Status = status,
            PickupScheduledAt = pickupScheduledAtUtc,
            DeliveredAt = deliveredAtUtc,
            TrackingNumber = "TRACK-INITIAL",
            CourierName = "Bosta",
            Notes = "Seeded return"
        };

        graph.RefundRequest.ReturnRequestId = returnRequest.Id;
        _dbContext.CommerceHubReturnRequests.Add(returnRequest);
        await SaveCommerceHubFixtureAsync();
        return returnRequest;
    }

    private async Task SaveCommerceHubFixtureAsync()
    {
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private sealed record RefundGraph(
        CommerceHubCustomer Customer,
        CommerceHubMarketplaceOrder Order,
        CommerceHubSubOrder SubOrder,
        CommerceHubSubOrderItem Item,
        CommerceHubRefundRequest RefundRequest);
}
