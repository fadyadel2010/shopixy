namespace PointOfSale.Tests.Architecture;

public sealed class ReturnActionAuthorityCertificationTests
{
    [Fact]
    public void ImplementedReturnActionSurfaces_MustBeCurrentBackendGrantedOnly()
    {
        var dto = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/CommerceHub/CommerceHubReturnDtos.cs");
        var policy = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubReturnPolicy.cs");
        var mapper = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubReturnMapper.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/ReturnManagementService.cs");
        var posController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PosAdminReturnsController.cs");
        var superAdminController = CertificationSource.ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminReturnsController.cs");
        var vendorService = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/commerceHubVendorOrdersApi.js");
        var vendorPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubOrders.jsx");
        var superAdminService = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundsService.ts");
        var superAdminPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");

        Assert.Contains("public bool CanSchedulePickup", dto, StringComparison.Ordinal);
        Assert.Contains("public bool CanMarkPickedUp", dto, StringComparison.Ordinal);
        Assert.Contains("public bool CanMarkInTransit", dto, StringComparison.Ordinal);
        Assert.Contains("public bool CanMarkDelivered", dto, StringComparison.Ordinal);
        Assert.Contains("public bool CanMarkFailed", dto, StringComparison.Ordinal);
        Assert.Contains("public bool CanConfirmReceipt", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("CanOverrideStatus", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowedOverrideStatuses", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAllowedOverrideStatuses", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("CanOverrideStatusTo", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowedOverrideStatuses", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("OverrideStatusAsync", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedStatus(returnRequest.Status, expectedStatus)", service, StringComparison.Ordinal);

        Assert.Contains("_tenantContext.RequiredTenantId", posController, StringComparison.Ordinal);
        Assert.Contains("RETURN_EXPECTED_STATUS_REQUIRED", posController, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPost", superAdminController, StringComparison.Ordinal);
        Assert.Contains("runCommerceHubVendorReturnAction", vendorService, StringComparison.Ordinal);
        Assert.Contains("expectedStatus: acceptedReturn.status", vendorService, StringComparison.Ordinal);
        Assert.Contains("requireCommerceHubVendorReturnAuthority", vendorService, StringComparison.Ordinal);
        Assert.Contains("Vendor return logistics", vendorPage, StringComparison.Ordinal);
        Assert.DoesNotContain("override-status", superAdminService, StringComparison.Ordinal);
        Assert.DoesNotContain("mark-failed", superAdminService, StringComparison.Ordinal);
        Assert.DoesNotContain("ReturnTransitionModal", superAdminPage, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerVendorPosOrderRefundPaymentPaths_MustNotMintCommerceHubReturnActions()
    {
        var customerRefundModal = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/components/RefundRequestModal.tsx");
        var customerRefundService = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-refunds-service.ts");
        var vendorAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var salesReturns = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/SalesReturns.jsx");
        var purchaseReturns = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/PurchaseReturns.jsx");
        var salesReturnsController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/SalesReturnsController.cs");
        var purchaseReturnsController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PurchaseReturnsController.cs");
        var paymentActionFreeze = CertificationSource.ReadRepoFile("PointOfSale.Tests/Architecture/PaymentActionAuthorityCertificationTests.cs");
        var refundActionFreeze = CertificationSource.ReadRepoFile("PointOfSale.Tests/Architecture/RefundActionAuthorityCertificationTests.cs");

        Assert.Contains("const currentItem = await resolveCurrentItem(item.orderItemId)", customerRefundModal, StringComparison.Ordinal);
        Assert.Contains("requireCustomerCreatedRefundForCurrentOrderItem", customerRefundService, StringComparison.Ordinal);
        Assert.Contains("validateRefundImpacts(scopedValue.recentRefundImpacts ?? [], currentUser)", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("hasCurrentVendorAuthority(scopedValue, currentUser)", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("api.post('/salesreturns', payload", salesReturns, StringComparison.Ordinal);
        Assert.Contains("api.post('/purchasereturns', payload", purchaseReturns, StringComparison.Ordinal);
        Assert.Contains("LoadOriginalInvoiceForReturnMutationAsync(request.OriginalInvoiceId.Value)", salesReturnsController, StringComparison.Ordinal);
        Assert.Contains("TenantId = _tenantContext.RequiredTenantId", salesReturnsController, StringComparison.Ordinal);
        Assert.Contains("LoadOriginalPurchaseInvoiceForReturnMutationAsync(request.OriginalInvoiceId.Value)", purchaseReturnsController, StringComparison.Ordinal);
        Assert.Contains("TenantId = _tenantContext.RequiredTenantId", purchaseReturnsController, StringComparison.Ordinal);
        Assert.DoesNotContain("/returns/${returnId}/", customerRefundModal, StringComparison.Ordinal);
        Assert.DoesNotContain("/returns/${returnId}/", salesReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("/returns/${returnId}/", purchaseReturns, StringComparison.Ordinal);
        Assert.Contains("CustomerRefundActions_MustRebindModalCandidatesToCurrentBackendOrderAndPayoutAuthority", paymentActionFreeze, StringComparison.Ordinal);
        Assert.Contains("VendorAndSuperAdminRefundActions_MustRemainCurrentBackendDerived", refundActionFreeze, StringComparison.Ordinal);
    }
}
