namespace PointOfSale.Tests.Architecture;

public sealed class RefundVisibilityAuthorityCertificationTests
{
    [Fact]
    public void RefundVisibility_MustRemainCurrentBackendDerivedAcrossCustomerVendorAndAdminRuntime()
    {
        var customerRefundService = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-refunds-service.ts");
        var customerRefundPage = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerRefundsPage.tsx");
        var vendorFinanceAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var superAdminAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var superAdminPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");

        Assert.Contains("requireCustomerRefundVisibilityAuthority(requireCustomerRefundFinancialAuthority", customerRefundService, StringComparison.Ordinal);
        Assert.Contains("const acceptedRefunds = refundsQuery.isFetching || refundsQuery.isError ? []", customerRefundPage, StringComparison.Ordinal);
        Assert.Contains("const activeRefund = acceptedRefunds.find((refund) => refund.refundId === activeRefundId) ?? null", customerRefundPage, StringComparison.Ordinal);

        Assert.Contains("hasCurrentVendorAuthority(scopedValue, currentUser)", vendorFinanceAuthority, StringComparison.Ordinal);
        Assert.Contains("validateRefundImpacts(scopedValue.recentRefundImpacts ?? [])", vendorFinanceAuthority, StringComparison.Ordinal);

        Assert.Contains("requireRefundMediaPresentation", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminRefundDetailsForCurrentSelection", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("const acceptedRefundDetails =", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("!refundsQuery.isFetching", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("!detailsQuery.isFetching", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("refund={acceptedRefundDetails}", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("executeModalOpen && acceptedRefundDetails", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("returnModalMode && acceptedRefundDetails?.returnRequest", superAdminPage, StringComparison.Ordinal);
    }
}
