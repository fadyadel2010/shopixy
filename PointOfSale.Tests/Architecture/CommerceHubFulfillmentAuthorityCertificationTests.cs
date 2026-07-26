namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubFulfillmentAuthorityCertificationTests
{
    [Fact]
    public void PosAdminFulfillment_MustRemainTenantScopedSerializableAndPaymentReady()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubOrdersController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderManagementService.cs");
        var stateHelper = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderStateHelper.cs");

        Assert.Contains("[Authorize]", controller, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.TenantId", controller, StringComparison.Ordinal);
        Assert.Contains("UpdateSubOrderStatusAsync(", controller, StringComparison.Ordinal);

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", service, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(System.Data.IsolationLevel.Serializable", service, StringComparison.Ordinal);
        Assert.Contains("subOrder.StoreId != storeId", service, StringComparison.Ordinal);
        Assert.Contains("\"SUBORDER_FORBIDDEN\"", service, StringComparison.Ordinal);
        Assert.Contains("LoadSubOrderForLifecycleUpdateAsync", service, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubOrderStateHelper.CanTransition", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubOrderStateHelper.RequiresPaymentReadiness", service, StringComparison.Ordinal);
        Assert.Contains("ReleaseOrderItemReservationsAsync", service, StringComparison.Ordinal);
        Assert.Contains("CommitDeliveryAsync", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSubOrderStatusHistory", service, StringComparison.Ordinal);
        Assert.Contains("PublishStatusEventsAsync", service, StringComparison.Ordinal);

        Assert.Contains("PaymentStatus.CashOnDelivery", stateHelper, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Collected", stateHelper, StringComparison.Ordinal);
        Assert.Contains("PaymentStatus.Settled", stateHelper, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminOrderAuthority_MustRemainReadOnlyForOrderState()
    {
        var service = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminOrdersService.cs");
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminOrdersController.cs");

        Assert.Contains("AsNoTracking()", service, StringComparison.Ordinal);
        Assert.Contains("GetOrdersAsync", service, StringComparison.Ordinal);
        Assert.Contains("GetOrderDetailsAsync", service, StringComparison.Ordinal);
        Assert.Contains("ExportOrdersCsvAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateSubOrderStatusAsync", service, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"order\.Status\s*=[^=]", service);
        Assert.DoesNotMatch(@"subOrder\.Status\s*=[^=]", service);
        Assert.DoesNotContain("BeginOrderLifecycle()", service, StringComparison.Ordinal);

        Assert.Contains("ISuperAdminOrdersService", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPatch", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPost", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnAndRefundAuthority_MustRequireLineageLocksAuditAndExecutionMilestones()
    {
        var returnService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/ReturnManagementService.cs");
        var refundService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs");
        var refundExecution = ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundExecutionService.cs");

        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", returnService, StringComparison.Ordinal);
        Assert.Contains("LoadReturnForUpdateAsync", returnService, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", returnService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubReturnPolicy.CanSchedulePickup", returnService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubReturnPolicy.CanMarkPickedUp", returnService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubReturnPolicy.CanMarkInTransit", returnService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubReturnPolicy.CanMarkDelivered", returnService, StringComparison.Ordinal);
        Assert.Contains("ConfirmReceiptAsync", returnService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubReturnPolicy.CanConfirmReceipt", returnService, StringComparison.Ordinal);
        Assert.Contains("RETURN_OVERRIDE_CONFIRMED_NOT_ALLOWED", returnService, StringComparison.Ordinal);
        Assert.Contains("AppendAuditLog", returnService, StringComparison.Ordinal);

        foreach (var source in new[] { refundService, refundExecution })
        {
            Assert.Contains("CommerceHubBoundaryMutationScope.BeginRefund()", source, StringComparison.Ordinal);
            Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", source, StringComparison.Ordinal);
            Assert.Contains("CommerceHubBoundaryMutationScope.BeginPaymentLifecycle()", source, StringComparison.Ordinal);
            Assert.Contains("EnsureRefundCompletionDoesNotOverrunAsync", source, StringComparison.Ordinal);
            Assert.Contains("PublishRefundStatusEventsAsync", source, StringComparison.Ordinal);
        }

        Assert.Contains("ValidateReturnExecutionEligibility", refundExecution, StringComparison.Ordinal);
        Assert.Contains("CommerceHubReturnPolicy.CanExecuteRefund", refundExecution, StringComparison.Ordinal);
        Assert.Contains("REFUND_RETURN_TRIGGER_NOT_REACHED", refundExecution, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
