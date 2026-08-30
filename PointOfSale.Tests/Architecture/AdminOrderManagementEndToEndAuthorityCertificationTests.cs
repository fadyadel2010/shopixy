namespace PointOfSale.Tests.Architecture;

public sealed class AdminOrderManagementEndToEndAuthorityCertificationTests
{
    [Fact]
    public void AdminOrderManagementRuntime_MustComposePresentationOnlyAuthorityBoundaries()
    {
        var ordersPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");
        var ordersApi = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/adminOrdersApi.js");
        var lifecycleAdmission = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/marketplaceOrderLifecycleAuthority.js");
        var vendorAdmission = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js");

        Assert.Contains("getAdminOrders(filters, signal)", ordersPage, StringComparison.Ordinal);
        Assert.Contains("getAdminOrderDetails(selectedOrderId, signal)", ordersPage, StringComparison.Ordinal);
        Assert.Contains("updateAdminOrderStatus(selectedOrderId, candidate)", ordersPage, StringComparison.Ordinal);
        Assert.Contains("authorityScope = ['pos-admin-orders'", ordersPage, StringComparison.Ordinal);
        Assert.Contains("requireCurrentVendorOrderVisibilityListAuthority(ordersQuery.data, user)", ordersPage, StringComparison.Ordinal);
        Assert.Contains("requireCurrentVendorOrderVisibilityAuthority(detailsQuery.data, user, selectedOrderId)", ordersPage, StringComparison.Ordinal);
        Assert.Contains("orderListAuthorityError", ordersPage, StringComparison.Ordinal);
        Assert.Contains("selectedOrderAuthorityError", ordersPage, StringComparison.Ordinal);
        Assert.Contains("rowVersion: order.rowVersion", ordersPage, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", ordersApi, StringComparison.Ordinal);
        Assert.Contains("'If-Match': rowVersion", ordersApi, StringComparison.Ordinal);
        Assert.Contains("requireMarketplaceOrderLifecycleListAuthority", ordersApi, StringComparison.Ordinal);
        Assert.Contains("requireVendorOrderVisibilityListAuthority", ordersApi, StringComparison.Ordinal);
        Assert.Contains("requireVendorOrderVisibilityAuthority", ordersApi, StringComparison.Ordinal);
        Assert.Contains("missing status concurrency evidence", lifecycleAdmission, StringComparison.Ordinal);
        Assert.Contains("seenOrderIds", vendorAdmission, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", ordersPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", ordersPage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackendAdmission_MustOwnOrderReadLifecyclePaymentFulfillmentShipmentRecovery()
    {
        var adminOrdersController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminOrdersController.cs");
        var adminOrderService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");
        var fulfillmentController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/OrderFulfillmentController.cs");
        var fulfillmentService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs");
        var adminShipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminShipmentService.cs");
        var shipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var recoveryService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin,Manager\")]", adminOrdersController, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", adminOrdersController, StringComparison.Ordinal);
        Assert.Contains("ParseRequiredIfMatch", adminOrdersController, StringComparison.Ordinal);
        Assert.Contains("VerifyOrderOwnershipAsync", adminOrderService, StringComparison.Ordinal);
        Assert.Contains("PaymentIntent", adminOrderService, StringComparison.Ordinal);
        Assert.Contains("Shipments", adminOrderService, StringComparison.Ordinal);
        Assert.Contains("LoadOrderForAdminStatusMutationAsync", adminOrderService, StringComparison.Ordinal);
        Assert.Contains("ORDER_VERSION_CONFLICT", CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/IAdminOrderService.cs"), StringComparison.Ordinal);
        Assert.Contains("ORDER_CANCELLATION_LIFECYCLE_REQUIRED", fulfillmentController, StringComparison.Ordinal);
        Assert.Contains("REFUND_LIFECYCLE_REQUIRED", fulfillmentController, StringComparison.Ordinal);
        Assert.Contains("TransitionAsync", fulfillmentService, StringComparison.Ordinal);
        Assert.Contains("FulfillmentStatusSaveChangesInterceptor", fulfillmentService, StringComparison.Ordinal);
        Assert.Contains("OrderOperationalFinancialAuthority", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition", adminShipmentService, StringComparison.Ordinal);
        Assert.Contains("CreateShipmentAsync", shipmentService, StringComparison.Ordinal);
        Assert.Contains("ReconcileShipmentAuthorityAsync", shipmentService, StringComparison.Ordinal);
        Assert.Contains("RecoverMissingShipmentsAsync", recoveryService, StringComparison.Ordinal);
        Assert.Contains("RecoverShipmentAuthorityGapsAsync", recoveryService, StringComparison.Ordinal);
        Assert.Contains("tenantContext.SetTenantId", recoveryService, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalCertificationArtifacts_MustFreezeEndToEndOrderManagementAuthority()
    {
        var requiredFiles = new[]
        {
            "docs/architecture/admin-order-management-end-to-end-authority-certification-findings.md",
            ".ai-context/PROJECT_CONTEXT.md",
            ".ai-context/ARCHITECTURE_TRUTH.md",
            ".ai-context/KNOWN_RISKS.md",
            ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
            ".ai-context/CODEX_RULES.md"
        };

        foreach (var path in requiredFiles)
        {
            var contents = CertificationSource.ReadRepoFile(path);
            Assert.Contains("PASS SF-ORD.FINAL", contents, StringComparison.Ordinal);
            Assert.Contains("Presentation Only", contents, StringComparison.Ordinal);
            Assert.Contains("SQL", contents, StringComparison.Ordinal);
            Assert.Contains("SF-ORD1", contents, StringComparison.Ordinal);
            Assert.Contains("SF-ORD2", contents, StringComparison.Ordinal);
            Assert.Contains("SF-ORD3", contents, StringComparison.Ordinal);
            Assert.Contains("SF-ORD4", contents, StringComparison.Ordinal);
        }
    }
}
