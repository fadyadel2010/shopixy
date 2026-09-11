namespace PointOfSale.Tests.Architecture;

public sealed class ReturnLifecycleAuthorityCertificationTests
{
    [Fact]
    public void ReturnLifecycleVocabulary_MustMatchImplementedBackendAndFrontendValues()
    {
        var backendStatus = CertificationSource.ReadRepoFile("PointOfSale.Core/Enums/CommerceHubReturnStatus.cs");
        var frontendAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var frontendService = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundsService.ts");

        foreach (var status in new[]
        {
            "Pending",
            "PickupScheduled",
            "PickedUp",
            "InTransit",
            "DeliveredToVendor",
            "Confirmed",
            "Failed",
        })
        {
            Assert.Contains(status, backendStatus, StringComparison.Ordinal);
            Assert.Contains($"'{status}'", frontendAuthority, StringComparison.Ordinal);
            Assert.Contains($"'{status}'", frontendService, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Cancelled", backendStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("Rejected", backendStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminReturnLifecycle_MustBeCurrentBackendBoundAndReadOnly()
    {
        var authority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundsService.ts");
        var page = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");

        Assert.Contains("!RETURN_STATUSES.has(returnRequest.status)", authority, StringComparison.Ordinal);
        Assert.Contains("typeof returnRequest.canSchedulePickup !== 'boolean'", authority, StringComparison.Ordinal);
        Assert.Contains("typeof returnRequest.canMarkPickedUp !== 'boolean'", authority, StringComparison.Ordinal);
        Assert.Contains("typeof returnRequest.canMarkInTransit !== 'boolean'", authority, StringComparison.Ordinal);
        Assert.Contains("typeof returnRequest.canMarkDelivered !== 'boolean'", authority, StringComparison.Ordinal);
        Assert.Contains("typeof returnRequest.canMarkFailed !== 'boolean'", authority, StringComparison.Ordinal);
        Assert.Contains("typeof returnRequest.canConfirmReceipt !== 'boolean'", authority, StringComparison.Ordinal);
        Assert.Contains("typeof returnRequest.isExecutionTriggerReached !== 'boolean'", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("canOverrideStatus", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("allowedOverrideStatuses", authority, StringComparison.Ordinal);
        Assert.Contains("return requireSuperAdminReturnPresentation(response.data, returnId)", service, StringComparison.Ordinal);
        Assert.Contains("`/returns/${returnId}`", service, StringComparison.Ordinal);
        Assert.DoesNotContain("`/returns/${returnId}/mark-failed`", service, StringComparison.Ordinal);
        Assert.DoesNotContain("`/returns/${returnId}/override-status`", service, StringComparison.Ordinal);
        Assert.Contains("const acceptedRefundDetails =", page, StringComparison.Ordinal);
        Assert.Contains("!refundsQuery.isFetching", page, StringComparison.Ordinal);
        Assert.Contains("!refundsQuery.isError", page, StringComparison.Ordinal);
        Assert.Contains("!detailsQuery.isFetching", page, StringComparison.Ordinal);
        Assert.Contains("!detailsQuery.isError", page, StringComparison.Ordinal);
        Assert.Contains("async function requireCurrentRefundMutationAuthority()", page, StringComparison.Ordinal);
        Assert.Contains("const refreshedRefunds = await refundsQuery.refetch()", page, StringComparison.Ordinal);
        Assert.Contains("return getSuperAdminRefundDetails(currentRefund.refundId, currentRefund)", page, StringComparison.Ordinal);
        Assert.Contains("<ReturnTimeline returnRequest={refund.returnRequest} />", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ReturnTransitionModal", page, StringComparison.Ordinal);
        Assert.DoesNotContain("onOverrideReturn", page, StringComparison.Ordinal);
        Assert.DoesNotContain("onMarkReturnFailed", page, StringComparison.Ordinal);
        Assert.DoesNotContain("returnMutation", page, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorReturnPath_MustOwnLifecycleWhileCustomerAndLegacyPosReturnsRemainSeparate()
    {
        var customerRefundsPage = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerRefundsPage.tsx");
        var customerRefundService = CertificationSource.ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-refunds-service.ts");
        var vendorAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var vendorReturnService = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/commerceHubVendorOrdersApi.js");
        var vendorOrdersPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubOrders.jsx");
        var salesReturns = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/SalesReturns.jsx");
        var purchaseReturns = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/PurchaseReturns.jsx");

        Assert.Contains("refundsQuery.isFetching || refundsQuery.isError ? []", customerRefundsPage, StringComparison.Ordinal);
        Assert.Contains("requireCustomerCreatedRefundForCurrentOrderItem", customerRefundService, StringComparison.Ordinal);
        Assert.Contains("requireCustomerRefundVisibilityAuthority(requireCustomerRefundFinancialAuthority", customerRefundService, StringComparison.Ordinal);
        Assert.Contains("validateRefundImpacts(scopedValue.recentRefundImpacts ?? [], currentUser)", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("hasCurrentVendorAuthority(scopedValue, currentUser)", vendorAuthority, StringComparison.Ordinal);
        Assert.Contains("runCommerceHubVendorReturnAction", vendorReturnService, StringComparison.Ordinal);
        Assert.Contains("expectedStatus: acceptedReturn.status", vendorReturnService, StringComparison.Ordinal);
        Assert.Contains("requireCommerceHubVendorReturnAuthority", vendorReturnService, StringComparison.Ordinal);
        Assert.Contains("Vendor return logistics", vendorOrdersPage, StringComparison.Ordinal);
        Assert.Contains("refund.returnRequest?.canConfirmReceipt", vendorOrdersPage, StringComparison.Ordinal);
        Assert.Contains("api.post('/salesreturns', payload", salesReturns, StringComparison.Ordinal);
        Assert.Contains("api.post('/purchasereturns', payload", purchaseReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("PickupScheduled", salesReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("PickupScheduled", purchaseReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliveredToVendor", salesReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliveredToVendor", purchaseReturns, StringComparison.Ordinal);
    }
}
