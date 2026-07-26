namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubReturnAuthorityCertificationTests
{
    [Fact]
    public void PosAdminReturnAuthority_MustStayVendorScopedReceiptOnlyAndAudited()
    {
        var returns = ReadRepoFile("PointOfSale.API/Services/CommerceHub/ReturnManagementService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/PosAdminReturnsController.cs");
        var tests = ReadRepoFile("PointOfSale.Tests/CommerceHub/ReturnManagementServiceTests.cs");

        Assert.Contains("ConfirmReceiptAsync", returns, StringComparison.Ordinal);
        Assert.Contains("Guid vendorId", returns, StringComparison.Ordinal);
        Assert.Contains("source = \"PosAdminAPI\"", returns, StringComparison.Ordinal);
        Assert.Contains("Vendor authority is limited to receipt confirmation after delivery", returns, StringComparison.Ordinal);
        Assert.Contains("LoadReturnForUpdateAsync", returns, StringComparison.Ordinal);
        Assert.Contains("LoadRefundForUpdateAsync", returns, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", returns, StringComparison.Ordinal);
        Assert.Contains("returnRequest.VendorId != vendorId", returns, StringComparison.Ordinal);
        Assert.Contains("RETURN_CONFIRMED", returns, StringComparison.Ordinal);
        Assert.Contains("SyncRefundAwaitingPayoutStatus", returns, StringComparison.Ordinal);
        Assert.Contains("_inventoryConsistencyService", returns, StringComparison.Ordinal);

        Assert.Contains("ConfirmReceipt", controller, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
        Assert.Contains("ClaimTypes.NameIdentifier", controller, StringComparison.Ordinal);

        Assert.Contains("ConfirmReceiptAsync_ShouldApplyInventoryReceipt_WhenTriggerIsOnVendorConfirmation", tests, StringComparison.Ordinal);
        Assert.Contains("MarkPickedUpAsync_ShouldRejectVendorLogisticsSource", tests, StringComparison.Ordinal);
        Assert.Contains("ConfirmReceiptAsync_ShouldAllowVendorReceiptAuthority_WhenDeliveredToVendor", tests, StringComparison.Ordinal);
        Assert.Contains("RETURN_TRANSITION_NOT_ALLOWED", tests, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminReturnAuthority_MustStayLogisticsOverrideOnlyAndTransitionPolicyBounded()
    {
        var returns = ReadRepoFile("PointOfSale.API/Services/CommerceHub/ReturnManagementService.cs");
        var policy = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubReturnPolicy.cs");
        var superAdminRefunds = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminRefundsService.cs");

        Assert.Contains("SchedulePickupAsync", returns, StringComparison.Ordinal);
        Assert.Contains("MarkPickedUpAsync", returns, StringComparison.Ordinal);
        Assert.Contains("MarkInTransitAsync", returns, StringComparison.Ordinal);
        Assert.Contains("MarkDeliveredAsync", returns, StringComparison.Ordinal);
        Assert.Contains("MarkFailedAsync", returns, StringComparison.Ordinal);
        Assert.Contains("OverrideStatusAsync", returns, StringComparison.Ordinal);
        Assert.Contains("source = \"SuperAdminAPI\"", returns, StringComparison.Ordinal);
        Assert.Contains("EnsurePlatformReturnLogisticsAuthority", returns, StringComparison.Ordinal);
        Assert.Contains("RETURN_LOGISTICS_WORKFLOW_REQUIRED", returns, StringComparison.Ordinal);
        Assert.Contains("AppendAuditLog", returns, StringComparison.Ordinal);

        Assert.Contains("CanSchedulePickup", policy, StringComparison.Ordinal);
        Assert.Contains("CanMarkPickedUp", policy, StringComparison.Ordinal);
        Assert.Contains("CanMarkInTransit", policy, StringComparison.Ordinal);
        Assert.Contains("CanMarkDelivered", policy, StringComparison.Ordinal);
        Assert.Contains("CanMarkFailed", policy, StringComparison.Ordinal);
        Assert.Contains("IsExecutionTriggerReached", policy, StringComparison.Ordinal);

        Assert.Contains("EnsureReturnRequestForApprovedRefundAsync", superAdminRefunds, StringComparison.Ordinal);
        Assert.Contains("REFUND_APPROVED", superAdminRefunds, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
