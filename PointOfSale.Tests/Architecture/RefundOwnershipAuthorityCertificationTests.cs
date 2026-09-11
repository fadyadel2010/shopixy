namespace PointOfSale.Tests.Architecture;

public sealed class RefundOwnershipAuthorityCertificationTests
{
    [Fact]
    public void CustomerRefundOwnership_MustReenterCurrentBackendOrderAndPayoutAuthorityBeforeCreation()
    {
        var orderDetailsPage = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx");
        var refundModal = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/components/RefundRequestModal.tsx");
        var refundService = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-refunds-service.ts");
        var visibilityAuthority = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/paymentVisibilityAuthority.ts");

        Assert.Contains("const resolveCurrentRefundItem = async (orderItemId: string)", orderDetailsPage, StringComparison.Ordinal);
        Assert.Contains("const refreshedOrder = await orderQuery.refetch()", orderDetailsPage, StringComparison.Ordinal);
        Assert.Contains("resolveCurrentItem={resolveCurrentRefundItem}", orderDetailsPage, StringComparison.Ordinal);
        Assert.Contains("const currentItem = await resolveCurrentItem(item.orderItemId)", refundModal, StringComparison.Ordinal);
        Assert.Contains("!currentItem?.canRefund", refundModal, StringComparison.Ordinal);
        Assert.Contains("quantity > currentItem.maxRefundableQuantity", refundModal, StringComparison.Ordinal);
        Assert.Contains("const refreshedPayoutDetails = await payoutDetailsQuery.refetch()", refundModal, StringComparison.Ordinal);
        Assert.Contains("currentSavedPayoutDetails.find((payoutDetails) => payoutDetails.id === selectedPayoutDetailsId)", refundModal, StringComparison.Ordinal);
        Assert.Contains("orderItemId: currentItem.orderItemId", refundModal, StringComparison.Ordinal);
        Assert.Contains("requireCustomerRefundVisibilityAuthority(requireCustomerRefundFinancialAuthority", refundService, StringComparison.Ordinal);
        Assert.Contains("!guid(refund.orderId)", visibilityAuthority, StringComparison.Ordinal);
        Assert.Contains("!guid(refund.orderItemId)", visibilityAuthority, StringComparison.Ordinal);
        Assert.Contains("!guid(refund.vendorId)", visibilityAuthority, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorAndSuperAdminRefundOwnership_MustRemainCurrentBackendDerived()
    {
        var vendorFinancialAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var superAdminAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var superAdminService = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundsService.ts");
        var superAdminPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");

        Assert.Contains("hasCurrentVendorAuthority(scopedValue, currentUser)", vendorFinancialAuthority, StringComparison.Ordinal);
        Assert.Contains("validateRefundImpacts(scopedValue.recentRefundImpacts ?? [], currentUser)", vendorFinancialAuthority, StringComparison.Ordinal);
        Assert.Contains("GUID_PATTERN.test(normalizeString(row?.refundId))", vendorFinancialAuthority, StringComparison.Ordinal);
        Assert.Contains("requireCurrentSuperAdminRefundSelection", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("return refunds.find((refund) => refund.refundId.toLowerCase() === refundId.toLowerCase()) ?? null", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("refund.order.orderId.toLowerCase() !== currentSelection.orderId.toLowerCase()", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("refund.item.vendorId.toLowerCase() !== currentSelection.vendorId.toLowerCase()", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("refund.customer.customerId.toLowerCase() !== currentSelection.customerId.toLowerCase()", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("return requireSuperAdminRefundDetailsForCurrentSelection(response.data, currentSelection)", superAdminService, StringComparison.Ordinal);
        Assert.Contains("currentSelectedRefund", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminRefundDetailsForCurrentSelection(result, currentSelectedRefund)", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("handleSelectRefund(null)", superAdminPage, StringComparison.Ordinal);
    }
}
