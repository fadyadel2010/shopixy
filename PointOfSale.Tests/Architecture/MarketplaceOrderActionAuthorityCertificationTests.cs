namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceOrderActionAuthorityCertificationTests
{
    [Fact]
    public void FrontendCustomerOrderActions_MustValidateBackendActionEvidenceBeforePresentation()
    {
        var customerAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/orderIdentityAuthority.ts");
        var actionAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/orderActionAuthority.ts");
        var detailsPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx");
        var recoveryPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/OrderPaymentRecoveryPage.tsx");

        Assert.Contains("requireCustomerOrderActionAuthority(order)", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("requireRecoverableOrderActionAuthority(order)", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("order.canRetryPayment && (!RETRY_ORDER_STATUSES.has(order.status) || !RETRY_PAYMENT_STATUSES.has(order.paymentStatus))", actionAuthority, StringComparison.Ordinal);
        Assert.Contains("item.canRefund && (!REFUND_ITEM_STATUSES.has(item.status) || item.maxRefundableQuantity <= 0)", actionAuthority, StringComparison.Ordinal);
        Assert.Contains("disabled={!item.canRefund}", detailsPage, StringComparison.Ordinal);
        Assert.Contains("if (!order.canRetryPayment)", recoveryPage, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendVendorFulfillmentActions_MustRequireCurrentBackendFulfillmentActionAuthority()
    {
        var shipmentActions = ReadRepoFile("PointOfSale.Web/src/modules/shipping/components/ShipmentActions.jsx");
        var shipmentHooks = ReadRepoFile("PointOfSale.Web/src/modules/shipping/hooks/useShipmentsApi.js");
        var shipmentAuthority = ReadRepoFile("PointOfSale.Web/src/modules/shipping/shipmentActionAuthority.js");
        var shipmentAuthorityTests = ReadRepoFile("PointOfSale.Web/src/modules/shipping/shipmentActionAuthority.test.ts");

        Assert.Contains("useShipmentActionAuthority", shipmentHooks, StringComparison.Ordinal);
        Assert.Contains("/orders/${orderId}/fulfillment/actions", shipmentHooks, StringComparison.Ordinal);
        Assert.Contains("requireShipmentActionAuthority(response.data, orderId)", shipmentHooks, StringComparison.Ordinal);
        Assert.Contains("hasShipmentActionAuthority(actionAuthority, shipment, 'cancel')", shipmentActions, StringComparison.Ordinal);
        Assert.Contains("if (!canCancel) return", shipmentActions, StringComparison.Ordinal);
        Assert.Contains("normalizeId(authority.orderId) !== normalizeId(expectedOrderId)", shipmentAuthority, StringComparison.Ordinal);
        Assert.Contains("FULFILLMENT_ACTION_CODES", shipmentAuthority, StringComparison.Ordinal);
        Assert.Contains("rejects route or selected-shipment target mismatches", shipmentAuthorityTests, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendSuperAdminOrderActions_MustRemainReadOnlyMonitoringUnlessBackendGovernanceActionEvidenceExists()
    {
        var ordersPage = ReadRepoFile("PointOfSale.Web/src/super-admin/orders/OrdersPage.tsx");
        var ordersService = ReadRepoFile("PointOfSale.Web/src/super-admin/orders/ordersService.ts");
        var detailsDrawer = ReadRepoFile("PointOfSale.Web/src/super-admin/orders/OrderDetailsDrawer.tsx");

        Assert.Contains("getSuperAdminOrderDetails", ordersPage, StringComparison.Ordinal);
        Assert.Contains("downloadSuperAdminOrdersCsv", ordersPage, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminOrderDetailAuthority(response.data, orderId)", ordersService, StringComparison.Ordinal);
        Assert.DoesNotContain("approve", detailsDrawer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("override", detailsDrawer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mark paid", detailsDrawer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarketplaceOrderActionAuthorityDocumentation_MustBeFrozen()
    {
        var findings = ReadRepoFile("docs/architecture/commercehub-marketplace-order-action-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS M2-G Marketplace Order Action Authority", findings, StringComparison.Ordinal);
        Assert.Contains("Action findings", findings, StringComparison.Ordinal);
        Assert.Contains("Runtime hardening performed", findings, StringComparison.Ordinal);
        Assert.Contains("Marketplace Order Action Authority is production-certifiable", findings, StringComparison.Ordinal);
        Assert.Contains("PASS M2-G Marketplace Order Action Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS M2-G Marketplace Order Action Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS M2-G", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS M2-G Marketplace Order Action Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS M2-G Marketplace Order Action Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
