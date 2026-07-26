namespace PointOfSale.Tests.Architecture;

public sealed class RefundFinalAuthorityCertificationTests
{
    [Fact]
    public void RefundFrontendRuntime_MustRequireCurrentBackendAuthorityForEveryRefundDomain()
    {
        var customerModal = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/components/RefundRequestModal.tsx");
        var customerRefunds = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerRefundsPage.tsx");
        var customerAuthority = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/paymentVisibilityAuthority.ts");
        var vendorAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var adminPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");
        var adminAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");

        Assert.Contains("resolveCurrentItem: (orderItemId: string) => Promise<CustomerOrderItem | null>;", customerModal, StringComparison.Ordinal);
        Assert.Equal(2, customerModal.Split("const currentItem = await resolveCurrentItem(item.orderItemId);").Length - 1);
        Assert.DoesNotContain("resolveCurrentItem ?", customerModal, StringComparison.Ordinal);
        Assert.Contains("refundsQuery.isFetching || refundsQuery.isError ? []", customerRefunds, StringComparison.Ordinal);
        Assert.Contains("requireCustomerCreatedRefundForCurrentOrderItem", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("hasCurrentVendorAuthority(scopedValue, currentUser)", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("const currentSelectedRefund = refundsQuery.isFetching || refundsQuery.isError", adminPage, StringComparison.Ordinal);
        Assert.Contains("async function requireCurrentRefundMutationAuthority()", adminPage, StringComparison.Ordinal);
        Assert.Contains("const acceptedRefundDetails =", adminPage, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminRefundDetailsForCurrentSelection", adminAuthority, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminReturnForCurrentRefund", adminAuthority, StringComparison.Ordinal);
        Assert.Contains("guid(execution.financialRefundId)", adminAuthority, StringComparison.Ordinal);
    }
}
