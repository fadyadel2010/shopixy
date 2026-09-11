namespace PointOfSale.Tests.Architecture;

public sealed class RefundScopeIsolationAuthorityCertificationTests
{
    [Fact]
    public void CustomerRefundCreation_MustBindTheResponseToTheCurrentBackendAuthorizedOrderItem()
    {
        var modal = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/components/RefundRequestModal.tsx");
        var service = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-refunds-service.ts");
        var authority = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/paymentVisibilityAuthority.ts");

        Assert.Contains("resolveCurrentItem: (orderItemId: string) => Promise<CustomerOrderItem | null>;", modal, StringComparison.Ordinal);
        Assert.Equal(2, modal.Split("const currentItem = await resolveCurrentItem(item.orderItemId);").Length - 1);
        Assert.Contains("orderItemId: currentItem.orderItemId", modal, StringComparison.Ordinal);
        Assert.Contains("requireCustomerCreatedRefundForCurrentOrderItem", service, StringComparison.Ordinal);
        Assert.Contains("request.orderItemId", service, StringComparison.Ordinal);
        Assert.Contains("Customer refund scope authority rejected create-response order-item substitution.", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorAndAdminRefundScope_MustRemainCurrentBackendBoundAndFailClosedDuringRefresh()
    {
        var vendorAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var vendorPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubFinance.jsx");
        var adminAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var adminPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");

        Assert.Contains("hasCurrentVendorAuthority(scopedValue, currentUser)", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("validateRefundImpacts(scopedValue.recentRefundImpacts ?? [], currentUser)", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("const dashboard = dashboardQuery.isFetching", vendorPage, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminRefundDetailsForCurrentSelection", adminAuthority, StringComparison.Ordinal);
        Assert.Contains("Super Admin refund scope authority rejected detail/list scope contradiction.", adminAuthority, StringComparison.Ordinal);
        Assert.Contains("const currentSelectedRefund = refundsQuery.isFetching || refundsQuery.isError", adminPage, StringComparison.Ordinal);
        Assert.Contains("const acceptedRefundDetails =", adminPage, StringComparison.Ordinal);
        Assert.Contains("currentRefund = await requireCurrentRefundMutationAuthority()", adminPage, StringComparison.Ordinal);
    }
}
