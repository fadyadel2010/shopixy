namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubReturnAuthorityCertificationTests
{
    [Fact]
    public void PosAdminReturnAuthority_MustStayVendorScopedLifecycleAndAudited()
    {
        var returns = ReadRepoFile("PointOfSale.API/Services/CommerceHub/ReturnManagementService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/PosAdminReturnsController.cs");
        var tests = ReadRepoFile("PointOfSale.Tests/CommerceHub/ReturnManagementServiceTests.cs");

        Assert.Contains("SchedulePickupAsync", returns, StringComparison.Ordinal);
        Assert.Contains("MarkPickedUpAsync", returns, StringComparison.Ordinal);
        Assert.Contains("MarkInTransitAsync", returns, StringComparison.Ordinal);
        Assert.Contains("MarkDeliveredAsync", returns, StringComparison.Ordinal);
        Assert.Contains("MarkFailedAsync", returns, StringComparison.Ordinal);
        Assert.Contains("ConfirmReceiptAsync", returns, StringComparison.Ordinal);
        Assert.Contains("source = \"PosAdminAPI\"", returns, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorReturnLogisticsAuthority", returns, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorOwnership(returnRequest, vendorId)", returns, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedStatus(returnRequest.Status, expectedStatus)", returns, StringComparison.Ordinal);
        Assert.Contains("LoadReturnForUpdateAsync", returns, StringComparison.Ordinal);
        Assert.Contains("LoadRefundForUpdateAsync", returns, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", returns, StringComparison.Ordinal);
        Assert.Contains("returnRequest.VendorId == vendorId", returns, StringComparison.Ordinal);
        Assert.Contains("RETURN_STATUS_CONFLICT", returns, StringComparison.Ordinal);
        Assert.Contains("RETURN_CONFIRMED", returns, StringComparison.Ordinal);
        Assert.Contains("SyncRefundAwaitingPayoutStatus", returns, StringComparison.Ordinal);
        Assert.Contains("_inventoryConsistencyService", returns, StringComparison.Ordinal);

        Assert.Contains("SchedulePickup", controller, StringComparison.Ordinal);
        Assert.Contains("MarkPickedUp", controller, StringComparison.Ordinal);
        Assert.Contains("MarkInTransit", controller, StringComparison.Ordinal);
        Assert.Contains("MarkDelivered", controller, StringComparison.Ordinal);
        Assert.Contains("MarkFailed", controller, StringComparison.Ordinal);
        Assert.Contains("ConfirmReceipt", controller, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
        Assert.Contains("request?.ExpectedStatus is null", controller, StringComparison.Ordinal);
        Assert.Contains("RETURN_EXPECTED_STATUS_REQUIRED", controller, StringComparison.Ordinal);
        Assert.Contains("ClaimTypes.NameIdentifier", controller, StringComparison.Ordinal);

        Assert.Contains("ConfirmReceiptAsync_ShouldApplyInventoryReceipt_WhenTriggerIsOnVendorConfirmation", tests, StringComparison.Ordinal);
        Assert.Contains("MarkPickedUpAsync_ShouldRejectSuperAdminLogisticsSource", tests, StringComparison.Ordinal);
        Assert.Contains("SchedulePickupAsync_ShouldHideCrossVendorReturn", tests, StringComparison.Ordinal);
        Assert.Contains("MarkFailedAsync_ShouldRejectStaleExpectedStatus", tests, StringComparison.Ordinal);
        Assert.Contains("ConfirmReceiptAsync_ShouldAllowVendorReceiptAuthority_WhenDeliveredToVendor", tests, StringComparison.Ordinal);
        Assert.Contains("RETURN_TRANSITION_NOT_ALLOWED", tests, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminReturnAuthority_MustStayReadOnlyWhilePolicyBoundsVendorTransitions()
    {
        var returns = ReadRepoFile("PointOfSale.API/Services/CommerceHub/ReturnManagementService.cs");
        var policy = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubReturnPolicy.cs");
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminReturnsController.cs");
        var superAdminRefunds = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminRefundsService.cs");

        Assert.DoesNotContain("OverrideStatusAsync", returns, StringComparison.Ordinal);
        Assert.DoesNotContain("source = \"SuperAdminAPI\"", returns, StringComparison.Ordinal);
        Assert.Contains("AppendAuditLog", returns, StringComparison.Ordinal);

        Assert.Contains("CanSchedulePickup", policy, StringComparison.Ordinal);
        Assert.Contains("CanMarkPickedUp", policy, StringComparison.Ordinal);
        Assert.Contains("CanMarkInTransit", policy, StringComparison.Ordinal);
        Assert.Contains("CanMarkDelivered", policy, StringComparison.Ordinal);
        Assert.Contains("CanMarkFailed", policy, StringComparison.Ordinal);
        Assert.Contains("IsExecutionTriggerReached", policy, StringComparison.Ordinal);

        Assert.Contains("[HttpGet(\"{returnId:guid}\")]", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPost", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("override-status", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("mark-failed", controller, StringComparison.Ordinal);

        Assert.Contains("EnsureReturnRequestForApprovedRefundAsync", superAdminRefunds, StringComparison.Ordinal);
        Assert.Contains("REFUND_APPROVED", superAdminRefunds, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
