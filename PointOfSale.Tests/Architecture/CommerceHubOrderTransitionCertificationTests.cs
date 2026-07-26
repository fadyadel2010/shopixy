namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubOrderTransitionCertificationTests
{
    [Fact]
    public void CommerceHubOrderTransitionGraph_MustBlockInvalidStateJumpsAndResurrection()
    {
        var stateHelper = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderStateHelper.cs");
        var orderManagement = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderManagementService.cs");

        Assert.Contains("[OrderStatus.Pending] = new[] { OrderStatus.Processing, OrderStatus.Cancelled }", stateHelper, StringComparison.Ordinal);
        Assert.Contains("[OrderStatus.Confirmed] = new[] { OrderStatus.Processing, OrderStatus.Cancelled }", stateHelper, StringComparison.Ordinal);
        Assert.Contains("[OrderStatus.Processing] = new[] { OrderStatus.Shipped, OrderStatus.Cancelled }", stateHelper, StringComparison.Ordinal);
        Assert.Contains("[OrderStatus.Shipped] = new[] { OrderStatus.Delivered, OrderStatus.Cancelled }", stateHelper, StringComparison.Ordinal);
        Assert.Contains("[OrderStatus.OnHold] = new[] { OrderStatus.Cancelled }", stateHelper, StringComparison.Ordinal);
        Assert.Contains("AllowedTransitions.TryGetValue(current", stateHelper, StringComparison.Ordinal);
        Assert.Contains("IsCompletedVendorStatus", stateHelper, StringComparison.Ordinal);
        Assert.Contains("status is OrderStatus.Delivered or OrderStatus.Completed or OrderStatus.Refunded", stateHelper, StringComparison.Ordinal);

        Assert.Contains("\"SUBORDER_ALREADY_COMPLETED\"", orderManagement, StringComparison.Ordinal);
        Assert.Contains("\"SUBORDER_STATUS_INVALID\"", orderManagement, StringComparison.Ordinal);
        Assert.Contains("\"SUBORDER_NOT_READY_FOR_FULFILLMENT\"", orderManagement, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderStatus.Pending => OrderStatus.Delivered", stateHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderStatus.Cancelled] = new[]", stateHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderStatus.Refunded] = new[]", stateHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderStatus.Delivered] = new[]", stateHelper, StringComparison.Ordinal);
    }

    [Fact]
    public void CancellationDeliveryAndRefundTransitions_MustKeepInventoryPaymentAndProjectionInSync()
    {
        var orderManagement = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderManagementService.cs");
        var refundService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs");
        var compensationWorker = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentCompensationOutboxWorker.cs");

        Assert.Contains("ReleaseOrderItemReservationsAsync", orderManagement, StringComparison.Ordinal);
        Assert.Contains("InventoryEventIdFactory.Create(\"commercehub-order-cancel-release\"", orderManagement, StringComparison.Ordinal);
        Assert.Contains("CommitDeliveryAsync", orderManagement, StringComparison.Ordinal);
        Assert.Contains("InventoryEventIdFactory.Create(\"commercehub-delivery-commit\"", orderManagement, StringComparison.Ordinal);
        Assert.Contains("ApplyDeliveredFinancialsAsync", orderManagement, StringComparison.Ordinal);
        Assert.Contains("item.DeliveredAt = item.Status == CommerceHubOrderItemStatus.Delivered", orderManagement, StringComparison.Ordinal);

        foreach (var source in new[] { refundService, compensationWorker })
        {
            Assert.Contains("ApplyRefundStatuses", source, StringComparison.Ordinal);
            Assert.Contains("CommerceHubPaymentLifecycleAuthority.ApplyRefundPaymentState", source, StringComparison.Ordinal);
            Assert.Contains("subOrder.Status = OrderStatus.Refunded", source, StringComparison.Ordinal);
            Assert.Contains("AggregateMarketplaceStatus", source, StringComparison.Ordinal);
            Assert.Contains("TouchProjectionMutationVersion", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReplayRecoveryAndDashboards_MustRemainSqlFirstAndDerivativeOnly()
    {
        var repairService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderProjectionRepairService.cs");
        var repairSupport = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderProjectionRepairSupport.cs");
        var repository = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/CommerceHubOrderReadRepository.cs");
        var superAdminOrders = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminOrdersService.cs");
        var customerOrders = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerOrderService.cs");

        Assert.Contains("EnsureOrderProjectedAsync", repairService, StringComparison.Ordinal);
        Assert.Contains("BuildProjectionRecoveryFilter", repairSupport, StringComparison.Ordinal);
        Assert.Contains("ProjectionProvesSqlAuthority", repairSupport, StringComparison.Ordinal);
        Assert.Contains("ResolveSourceMutationVersion(order)", repairSupport, StringComparison.Ordinal);
        Assert.Contains("CustomerId = order.CustomerId", repairSupport, StringComparison.Ordinal);
        Assert.Contains("LastSourceMutationTicks", repairSupport, StringComparison.Ordinal);
        Assert.Contains("GetByMarketplaceOrderIdAsync", repository, StringComparison.Ordinal);
        Assert.Contains("LastEventVersion", repository, StringComparison.Ordinal);

        Assert.Contains("CommerceHubMarketplaceOrders", superAdminOrders, StringComparison.Ordinal);
        Assert.Contains("CommerceHubMarketplaceOrders", customerOrders, StringComparison.Ordinal);
        Assert.Contains("order.CustomerId == customerId", customerOrders, StringComparison.Ordinal);
        Assert.DoesNotContain("ICommerceHubOrderReadRepository", superAdminOrders, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
