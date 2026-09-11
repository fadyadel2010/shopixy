namespace PointOfSale.Tests.Architecture;

public sealed class ReturnOwnershipAuthorityCertificationTests
{
    [Fact]
    public void CustomerAndVendorReturnPresentation_MustRemainBackendDerived()
    {
        var customerRefundsPage = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerRefundsPage.tsx");
        var customerRefundService = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-refunds-service.ts");
        var customerAuthority = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/paymentVisibilityAuthority.ts");
        var vendorAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");

        Assert.Contains("refundsQuery.isFetching || refundsQuery.isError ? []", customerRefundsPage, StringComparison.Ordinal);
        Assert.Contains("requireCustomerRefundVisibilityAuthority(requireCustomerRefundFinancialAuthority", customerRefundService, StringComparison.Ordinal);
        Assert.Contains("!guid(refund.orderId)", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("!guid(refund.orderItemId)", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("!guid(refund.vendorId)", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("hasCurrentVendorAuthority(scopedValue, currentUser)", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("validateRefundImpacts(scopedValue.recentRefundImpacts ?? [], currentUser)", vendorAuthority, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminReturnOwnership_MustBeCurrentRefundBoundAndReadOnly()
    {
        var authority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundsService.ts");
        var page = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");

        Assert.Contains("returnRequest.refundId.toLowerCase() !== currentRefund.refundId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("returnRequest.orderItemId.toLowerCase() !== currentRefund.item.orderItemId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("returnRequest.customerId.toLowerCase() !== currentRefund.customer.customerId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("returnRequest.vendorId.toLowerCase() !== currentRefund.item.vendorId.toLowerCase()", authority, StringComparison.Ordinal);
        Assert.Contains("return requireSuperAdminReturnPresentation(response.data, returnId)", service, StringComparison.Ordinal);
        Assert.Contains("<ReturnTimeline returnRequest={refund.returnRequest} />", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ReturnTransitionModal", page, StringComparison.Ordinal);
        Assert.DoesNotContain("returnMutation", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PosReturnCreation_MustRemainServerDerivedAndTenantBound()
    {
        var salesReturns = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/SalesReturnsController.cs");
        var purchaseReturns = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PurchaseReturnsController.cs");
        var salesPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/SalesReturns.jsx");
        var purchasePage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/PurchaseReturns.jsx");

        Assert.Contains("LoadOriginalInvoiceForReturnMutationAsync(request.OriginalInvoiceId.Value)", salesReturns, StringComparison.Ordinal);
        Assert.Contains("TenantId = _tenantContext.RequiredTenantId", salesReturns, StringComparison.Ordinal);
        Assert.Contains("CustomerId = originalInvoice.CustomerId", salesReturns, StringComparison.Ordinal);
        Assert.Contains("LoadOriginalPurchaseInvoiceForReturnMutationAsync(request.OriginalInvoiceId.Value)", purchaseReturns, StringComparison.Ordinal);
        Assert.Contains("TenantId = _tenantContext.RequiredTenantId", purchaseReturns, StringComparison.Ordinal);
        Assert.Contains("SupplierId = originalInvoice.SupplierId", purchaseReturns, StringComparison.Ordinal);
        Assert.Contains("api.post('/salesreturns', payload", salesPage, StringComparison.Ordinal);
        Assert.Contains("api.post('/purchasereturns', payload", purchasePage, StringComparison.Ordinal);
    }
}
