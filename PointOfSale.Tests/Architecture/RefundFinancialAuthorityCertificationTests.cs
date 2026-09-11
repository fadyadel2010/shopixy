namespace PointOfSale.Tests.Architecture;

public sealed class RefundFinancialAuthorityCertificationTests
{
    [Fact]
    public void RefundFinancialPresentation_MustRemainBackendDerivedAcrossCustomerVendorAndAdminRuntime()
    {
        var customerRefundModal = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/components/RefundRequestModal.tsx");
        var customerRefundService = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-refunds-service.ts");
        var vendorFinancialAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var superAdminAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var superAdminPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");

        Assert.DoesNotContain("const totalAmount = useMemo", customerRefundModal, StringComparison.Ordinal);
        Assert.DoesNotContain("Refund total", customerRefundModal, StringComparison.Ordinal);
        Assert.Contains("Confirmed by the backend after review", customerRefundModal, StringComparison.Ordinal);
        Assert.DoesNotContain("amount:", customerRefundModal, StringComparison.Ordinal);
        Assert.Contains("requireCustomerRefundFinancialAuthority", customerRefundService, StringComparison.Ordinal);

        Assert.Contains("validateRefundImpacts(scopedValue.recentRefundImpacts ?? [], currentUser)", vendorFinancialAuthority, StringComparison.Ordinal);
        Assert.Contains("hasCurrentVendorAuthority(scopedValue, currentUser)", vendorFinancialAuthority, StringComparison.Ordinal);

        Assert.Contains("function requireRefundFinancialPresentation", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("financialRefundId", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("paymentRefundedAmountAfter < preview.paymentRefundedAmountBefore", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("Super Admin refund financial authority rejected malformed financial preview evidence", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("Super Admin refund financial authority rejected malformed execution evidence", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("async function requireCurrentRefundMutationAuthority()", superAdminPage, StringComparison.Ordinal);
        Assert.Contains("currentRefund = await requireCurrentRefundMutationAuthority()", superAdminPage, StringComparison.Ordinal);
    }
}
