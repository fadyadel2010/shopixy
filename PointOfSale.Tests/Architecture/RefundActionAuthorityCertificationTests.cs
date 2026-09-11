namespace PointOfSale.Tests.Architecture;

public sealed class RefundActionAuthorityCertificationTests
{
    [Fact]
    public void CustomerRefundIntents_MustReenterCurrentBackendOrderAndPayoutAuthority()
    {
        var orderDetailsPage = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx");
        var refundModal = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/components/RefundRequestModal.tsx");
        var refundService = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-refunds-service.ts");

        Assert.Contains("const resolveCurrentRefundItem = async (orderItemId: string)", orderDetailsPage, StringComparison.Ordinal);
        Assert.Contains("resolveCurrentItem: (orderItemId: string) => Promise<CustomerOrderItem | null>;", refundModal, StringComparison.Ordinal);
        Assert.Equal(2, refundModal.Split("const currentItem = await resolveCurrentItem(item.orderItemId);").Length - 1);
        Assert.Contains("Refund evidence upload is no longer authorized for this item.", refundModal, StringComparison.Ordinal);
        Assert.Contains("const refreshedPayoutDetails = await payoutDetailsQuery.refetch()", refundModal, StringComparison.Ordinal);
        Assert.Contains("orderItemId: currentItem.orderItemId", refundModal, StringComparison.Ordinal);
        Assert.Contains("requireCustomerRefundVisibilityAuthority(requireCustomerRefundFinancialAuthority", refundService, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorAndSuperAdminRefundActions_MustRemainCurrentBackendDerived()
    {
        var vendorFinancialAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var superAdminAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var superAdminPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");
        var refundService = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundsService.ts");

        Assert.Contains("validateRefundImpacts(scopedValue.recentRefundImpacts ?? [], currentUser)", vendorFinancialAuthority, StringComparison.Ordinal);
        Assert.Contains("hasCurrentVendorAuthority(scopedValue, currentUser)", vendorFinancialAuthority, StringComparison.Ordinal);
        Assert.Contains("async function requireCurrentRefundMutationAuthority()", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("const refreshedRefunds = await refundsQuery.refetch()", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("return getSuperAdminRefundDetails(currentRefund.refundId, currentRefund)", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("if (!currentRefund.canApprove)", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("if (!currentRefund.canReject)", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("if (!currentRefund.canRetry)", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("if (!currentRefund.canExecute", superAdminPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ReturnTransitionModal", superAdminPage, StringComparison.Ordinal);
        Assert.DoesNotContain("returnMutation", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminRefundDetailsForCurrentSelection", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("return requireSuperAdminRefundDetailsPresentation(response.data, refundId)", refundService, StringComparison.Ordinal);
        Assert.Contains("return requireSuperAdminReturnPresentation(response.data, returnId)", refundService, StringComparison.Ordinal);
    }
}
