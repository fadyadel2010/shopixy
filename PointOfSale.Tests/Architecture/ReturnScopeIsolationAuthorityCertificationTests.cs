namespace PointOfSale.Tests.Architecture;

public sealed class ReturnScopeIsolationAuthorityCertificationTests
{
    [Fact]
    public void SuperAdminReturnScope_MustRemainCurrentListDetailAndCurrentRefundBound()
    {
        var authority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var page = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");

        Assert.Contains("requireCurrentSuperAdminRefundSelection", page, StringComparison.Ordinal);
        Assert.Contains("getSuperAdminRefundDetails(currentSelectedRefundId as string, currentSelectedRefund ?? undefined)", page, StringComparison.Ordinal);
        Assert.Contains("const acceptedRefundDetails =", page, StringComparison.Ordinal);
        Assert.Contains("!refundsQuery.isFetching", page, StringComparison.Ordinal);
        Assert.Contains("!detailsQuery.isFetching", page, StringComparison.Ordinal);
        Assert.Contains("async function requireCurrentRefundMutationAuthority()", page, StringComparison.Ordinal);
        Assert.Contains("const refreshedRefunds = await refundsQuery.refetch()", page, StringComparison.Ordinal);
        Assert.Contains("return getSuperAdminRefundDetails(currentRefund.refundId, currentRefund)", page, StringComparison.Ordinal);
        Assert.Contains("const currentRefundDetails = detailsQuery.data", page, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminReturnForCurrentRefund(returnRequest, currentRefundDetails)", page, StringComparison.Ordinal);

        Assert.Contains("returnRequest.refundId.toLowerCase() !== currentRefund.refundId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("returnRequest.orderItemId.toLowerCase() !== currentRefund.item.orderItemId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("returnRequest.customerId.toLowerCase() !== currentRefund.customer.customerId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("returnRequest.vendorId.toLowerCase() !== currentRefund.item.vendorId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("refund.order.orderId.toLowerCase() !== currentSelection.orderId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("refund.item.vendorId.toLowerCase() !== currentSelection.vendorId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("refund.customer.customerId.toLowerCase() !== currentSelection.customerId.toLowerCase()", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerVendorAndPosReturnScope_MustNotBeRestoredFromFrontendState()
    {
        var customerRefundsPage = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerRefundsPage.tsx");
        var customerRefundModal = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/components/RefundRequestModal.tsx");
        var customerRefundService = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-refunds-service.ts");
        var vendorFinance = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var purchaseReturns = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/PurchaseReturns.jsx");

        Assert.Contains("refundsQuery.isFetching || refundsQuery.isError ? []", customerRefundsPage, StringComparison.Ordinal);
        Assert.Contains("const activeRefund = acceptedRefunds.find((refund) => refund.refundId === activeRefundId) ?? null", customerRefundsPage, StringComparison.Ordinal);
        Assert.Contains("const currentItem = await resolveCurrentItem(item.orderItemId)", customerRefundModal, StringComparison.Ordinal);
        Assert.Contains("requireCustomerCreatedRefundForCurrentOrderItem", customerRefundService, StringComparison.Ordinal);
        Assert.Contains("hasCurrentVendorAuthority(scopedValue, currentUser)", vendorFinance, StringComparison.Ordinal);
        Assert.Contains("validateRefundImpacts(scopedValue.recentRefundImpacts ?? [])", vendorFinance, StringComparison.Ordinal);
        Assert.Contains("purchaseInvoicesRequestIdRef.current += 1", purchaseReturns, StringComparison.Ordinal);
        Assert.Contains("invoiceRequestIdRef.current += 1", purchaseReturns, StringComparison.Ordinal);
        Assert.Contains("if (requestId !== invoiceRequestIdRef.current)", purchaseReturns, StringComparison.Ordinal);
    }
}
