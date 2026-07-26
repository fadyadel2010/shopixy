namespace PointOfSale.Tests.Architecture;

public sealed class RefundIdentityAuthorityCertificationTests
{
    [Fact]
    public void RefundIdentity_MustStayBackendDerivedAcrossFrontendRuntimePaths()
    {
        var customerRefundService = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-refunds-service.ts");
        var customerRefundVisibility = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/paymentVisibilityAuthority.ts");
        var customerRefundPage = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerRefundsPage.tsx");
        var superAdminRefundAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var superAdminRefundService = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundsService.ts");
        var superAdminRefundPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");

        Assert.Contains("requireCustomerRefundVisibilityAuthority(requireCustomerRefundFinancialAuthority", customerRefundService, StringComparison.Ordinal);
        Assert.Contains("!guid(refund?.refundId)", customerRefundVisibility, StringComparison.Ordinal);
        Assert.Contains("const acceptedRefunds = refundsQuery.isFetching || refundsQuery.isError ? []", customerRefundPage, StringComparison.Ordinal);
        Assert.Contains("const activeRefund = acceptedRefunds.find((refund) => refund.refundId === activeRefundId) ?? null", customerRefundPage, StringComparison.Ordinal);

        Assert.Contains("requireCurrentSuperAdminRefundSelection", superAdminRefundAuthority, StringComparison.Ordinal);
        Assert.Contains("return refunds.find((refund) => refund.refundId.toLowerCase() === refundId.toLowerCase()) ?? null", superAdminRefundAuthority, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminReturnForCurrentRefund", superAdminRefundAuthority, StringComparison.Ordinal);
        Assert.Contains("returnRequest.refundId.toLowerCase() !== currentRefund.refundId.toLowerCase()", superAdminRefundAuthority, StringComparison.Ordinal);
        Assert.Contains("returnRequest.orderItemId.toLowerCase() !== currentRefund.item.orderItemId.toLowerCase()", superAdminRefundAuthority, StringComparison.Ordinal);
        Assert.Contains("returnRequest.customerId.toLowerCase() !== currentRefund.customer.customerId.toLowerCase()", superAdminRefundAuthority, StringComparison.Ordinal);
        Assert.Contains("returnRequest.vendorId.toLowerCase() !== currentRefund.item.vendorId.toLowerCase()", superAdminRefundAuthority, StringComparison.Ordinal);

        Assert.Contains("requireSuperAdminReturnPresentation(response.data, returnId)", superAdminRefundService, StringComparison.Ordinal);
        Assert.Contains("refundsQuery.isFetching || refundsQuery.isError", superAdminRefundPage, StringComparison.Ordinal);
        Assert.Contains("handleSelectRefund(null)", superAdminRefundPage, StringComparison.Ordinal);
        Assert.Contains("queryClient.invalidateQueries({ queryKey: ['super-admin', 'refunds', 'details', currentSelectedRefund.refundId] })", superAdminRefundPage, StringComparison.Ordinal);
    }
}
