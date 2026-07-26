namespace PointOfSale.Tests.Architecture;

public sealed class ReturnVisibilityAuthorityCertificationTests
{
    [Fact]
    public void CustomerReturnVisibility_MustRemainBackendRefundAndOrderDerived()
    {
        var customerRefundsPage = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerRefundsPage.tsx");
        var customerRefundModal = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/components/RefundRequestModal.tsx");
        var customerRefundService = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-refunds-service.ts");
        var customerOrderService = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-orders-service.ts");
        var customerOrderAuthority = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/orderIdentityAuthority.ts");
        var customerRefundAuthority = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/paymentVisibilityAuthority.ts");

        Assert.Contains("refundsQuery.isFetching || refundsQuery.isError ? []", customerRefundsPage, StringComparison.Ordinal);
        Assert.Contains("const activeRefund = acceptedRefunds.find((refund) => refund.refundId === activeRefundId) ?? null", customerRefundsPage, StringComparison.Ordinal);
        Assert.Contains("requireCustomerRefundVisibilityAuthority(requireCustomerRefundFinancialAuthority", customerRefundService, StringComparison.Ordinal);
        Assert.Contains("requireCustomerCreatedRefundForCurrentOrderItem", customerRefundService, StringComparison.Ordinal);
        Assert.Contains("return requireCustomerOrderDetailAuthority(response.data, orderId)", customerOrderService, StringComparison.Ordinal);
        Assert.Contains("requireCustomerExistingRefundVisibilityAuthority(refund, item.total as number)", customerOrderAuthority, StringComparison.Ordinal);
        Assert.Contains("Customer payment visibility authority rejected refund/order visibility contradiction", customerRefundAuthority, StringComparison.Ordinal);
        Assert.Contains("const currentItem = await resolveCurrentItem(item.orderItemId)", customerRefundModal, StringComparison.Ordinal);
        Assert.Contains("if (!currentItem?.canRefund)", customerRefundModal, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorReturnVisibility_MustRemainCurrentVendorFinancialImpactOnly()
    {
        var vendorFinancePage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubFinance.jsx");
        var vendorAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");

        Assert.Contains("deriveVendorFinancialDashboardPresentation(dashboardQuery.data, user)", vendorFinancePage, StringComparison.Ordinal);
        Assert.Contains("const recentRefundImpacts = dashboard?.recentRefundImpacts || []", vendorFinancePage, StringComparison.Ordinal);
        Assert.Contains("hasCurrentVendorAuthority(scopedValue, currentUser)", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("validateRefundImpacts(scopedValue.recentRefundImpacts ?? [])", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("GUID_PATTERN.test(normalizeString(row?.refundId))", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("REFUND_IMPACT_TYPES.has(normalizeString(row?.impactType))", vendorAuthority, StringComparison.Ordinal);
        Assert.DoesNotContain("returnId", vendorFinancePage, StringComparison.Ordinal);
        Assert.DoesNotContain("override-status", vendorFinancePage, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminReturnVisibility_MustRequireCurrentListDetailAndExactReturnAuthority()
    {
        var authority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundsService.ts");
        var page = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");

        Assert.Contains("requireCurrentSuperAdminRefundSelection", page, StringComparison.Ordinal);
        Assert.Contains("getSuperAdminRefundDetails(currentSelectedRefundId as string, currentSelectedRefund ?? undefined)", page, StringComparison.Ordinal);
        Assert.Contains("const acceptedRefundDetails =", page, StringComparison.Ordinal);
        Assert.Contains("!refundsQuery.isFetching", page, StringComparison.Ordinal);
        Assert.Contains("!refundsQuery.isError", page, StringComparison.Ordinal);
        Assert.Contains("!detailsQuery.isFetching", page, StringComparison.Ordinal);
        Assert.Contains("!detailsQuery.isError", page, StringComparison.Ordinal);
        Assert.Contains("returnModalMode && acceptedRefundDetails?.returnRequest", page, StringComparison.Ordinal);
        Assert.Contains("async function requireCurrentRefundMutationAuthority()", page, StringComparison.Ordinal);
        Assert.Contains("const refreshedRefunds = await refundsQuery.refetch()", page, StringComparison.Ordinal);
        Assert.Contains("returnId: returnRequest.returnId", page, StringComparison.Ordinal);
        Assert.Contains("return requireSuperAdminReturnPresentation(response.data, returnId)", service, StringComparison.Ordinal);
        Assert.Contains("returnRequest.returnId.toLowerCase() !== expectedReturnId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("returnRequest.refundId.toLowerCase() !== currentRefund.refundId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("returnRequest.customerId.toLowerCase() !== currentRefund.customer.customerId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("returnRequest.vendorId.toLowerCase() !== currentRefund.item.vendorId.toLowerCase()", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void RefundOrderPaymentAndPosEvidence_MustNotCreateReturnVisibility()
    {
        var customerRefundAuthority = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/paymentVisibilityAuthority.ts");
        var customerOrderAuthority = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/orderIdentityAuthority.ts");
        var superAdminAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var salesReturns = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/SalesReturns.jsx");
        var purchaseReturns = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/PurchaseReturns.jsx");
        var salesReturnsController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/SalesReturnsController.cs");
        var purchaseReturnsController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PurchaseReturnsController.cs");

        Assert.Contains("!guid(refund.orderId)", customerRefundAuthority, StringComparison.Ordinal);
        Assert.Contains("!guid(refund.orderItemId)", customerRefundAuthority, StringComparison.Ordinal);
        Assert.Contains("!guid(refund.vendorId)", customerRefundAuthority, StringComparison.Ordinal);
        Assert.Contains("requireCustomerExistingRefundVisibilityAuthority(refund, item.total as number)", customerOrderAuthority, StringComparison.Ordinal);
        Assert.Contains("!guid(returnRequest.refundId)", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("!guid(returnRequest.orderItemId)", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("!guid(returnRequest.customerId)", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("!guid(returnRequest.vendorId)", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("api.post('/salesreturns', payload", salesReturns, StringComparison.Ordinal);
        Assert.Contains("api.post('/purchasereturns', payload", purchaseReturns, StringComparison.Ordinal);
        Assert.Contains("LoadOriginalInvoiceForReturnMutationAsync(request.OriginalInvoiceId.Value)", salesReturnsController, StringComparison.Ordinal);
        Assert.Contains("TenantId = _tenantContext.RequiredTenantId", salesReturnsController, StringComparison.Ordinal);
        Assert.Contains("LoadOriginalPurchaseInvoiceForReturnMutationAsync(request.OriginalInvoiceId.Value)", purchaseReturnsController, StringComparison.Ordinal);
        Assert.Contains("TenantId = _tenantContext.RequiredTenantId", purchaseReturnsController, StringComparison.Ordinal);
    }
}
