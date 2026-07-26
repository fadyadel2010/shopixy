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
        var frontendAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var frontendService = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundsService.ts");
        var frontendPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");

        Assert.Contains("public bool CanSchedulePickup", dto, StringComparison.Ordinal);
        Assert.Contains("public bool CanMarkPickedUp", dto, StringComparison.Ordinal);
        Assert.Contains("public bool CanMarkInTransit", dto, StringComparison.Ordinal);
        Assert.Contains("public bool CanMarkDelivered", dto, StringComparison.Ordinal);
        Assert.Contains("public bool CanMarkFailed", dto, StringComparison.Ordinal);
        Assert.Contains("public bool CanConfirmReceipt", dto, StringComparison.Ordinal);
        Assert.Contains("public bool CanOverrideStatus", dto, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyList<CommerceHubReturnStatus> AllowedOverrideStatuses", dto, StringComparison.Ordinal);

        Assert.Contains("GetAllowedOverrideStatuses", policy, StringComparison.Ordinal);
        Assert.Contains("CanOverrideStatusTo", policy, StringComparison.Ordinal);
        Assert.Contains("AllowedOverrideStatuses = CommerceHubReturnPolicy.GetAllowedOverrideStatuses", mapper, StringComparison.Ordinal);
        Assert.Contains("CanOverrideStatus = CommerceHubReturnPolicy.CanOverrideStatus", mapper, StringComparison.Ordinal);
        Assert.Contains("CommerceHubReturnPolicy.CanOverrideStatusTo", service, StringComparison.Ordinal);

        Assert.Contains("!allowedOverrideStatuses(returnRequest.allowedOverrideStatuses, returnRequest.canOverrideStatus)", frontendAuthority, StringComparison.Ordinal);
        Assert.Contains("canOverrideStatus: boolean", frontendService, StringComparison.Ordinal);
        Assert.Contains("allowedOverrideStatuses: SuperAdminOverrideReturnStatus[]", frontendService, StringComparison.Ordinal);
        Assert.Contains("refund.returnRequest.canOverrideStatus", frontendPage, StringComparison.Ordinal);
        Assert.Contains("statusOptions={returnModalMode === 'override' ? acceptedRefundDetails.returnRequest.allowedOverrideStatuses : undefined}", frontendPage, StringComparison.Ordinal);
        Assert.Contains("!returnRequest.allowedOverrideStatuses.includes(overrideStatus)", frontendPage, StringComparison.Ordinal);
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
        Assert.Contains("validateRefundImpacts(scopedValue.recentRefundImpacts ?? [])", vendorAuthority, StringComparison.Ordinal);
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
