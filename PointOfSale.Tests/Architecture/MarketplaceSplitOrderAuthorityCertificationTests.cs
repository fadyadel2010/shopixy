namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceSplitOrderAuthorityCertificationTests
{
    [Fact]
    public void FrontendSplitOrderRuntime_MustDeriveCompositionFromBackendSplitEvidenceOnly()
    {
        var customerAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/orderIdentityAuthority.ts");
        var creationAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/features/checkout/orderCreationAuthority.ts");
        var superAdminAuthority = ReadRepoFile("PointOfSale.Web/src/super-admin/orders/orderIdentityAuthority.ts");
        var superAdminDrawer = ReadRepoFile("PointOfSale.Web/src/super-admin/orders/OrderDetailsDrawer.tsx");

        Assert.Contains("order.subOrders.length === 0", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("seenSubOrderIds.has(subOrder.subOrderId)", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("assertCustomerOrderItems(subOrder.items)", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("response.subOrders.length === 0", creationAuthority, StringComparison.Ordinal);
        Assert.Contains("seenOrderItemKeys.has(itemKey)", creationAuthority, StringComparison.Ordinal);
        Assert.Contains("order.vendorCount !== order.subOrders.length", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("!subOrderIds.has(item.subOrderId)", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("deriveSubOrderItemPresentation", superAdminDrawer, StringComparison.Ordinal);
        Assert.Contains("item.subOrderId === subOrder.subOrderId", superAdminDrawer, StringComparison.Ordinal);
        Assert.DoesNotContain("groupOrderItemsByVendor", superAdminDrawer, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendSplitOrderRuntime_MustWithholdStaleSplitAuthorityOnRefreshAndFailure()
    {
        var customerOrdersPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerOrdersPage.tsx");
        var customerDashboard = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerDashboardPage.tsx");
        var customerDetails = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx");
        var recoveryPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/OrderPaymentRecoveryPage.tsx");
        var vendorOrders = ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");
        var superAdminOrders = ReadRepoFile("PointOfSale.Web/src/super-admin/orders/OrdersPage.tsx");

        Assert.Contains("ordersQuery.isFetching || ordersQuery.isError ? []", customerOrdersPage, StringComparison.Ordinal);
        Assert.Contains("recentOrdersQuery.isFetching || recentOrdersQuery.isError ? []", customerDashboard, StringComparison.Ordinal);
        Assert.Contains("orderQuery.isLoading || orderQuery.isFetching", customerDetails, StringComparison.Ordinal);
        Assert.Contains("orderQuery.isLoading || orderQuery.isFetching", recoveryPage, StringComparison.Ordinal);
        Assert.Contains("ordersQuery.isFetching", vendorOrders, StringComparison.Ordinal);
        Assert.Contains("detailsQuery.isFetching", vendorOrders, StringComparison.Ordinal);
        Assert.Contains("ordersQuery.isFetching || ordersQuery.isError ? []", superAdminOrders, StringComparison.Ordinal);
        Assert.Contains("cached order rows are withheld", superAdminOrders, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceSplitOrderAuthorityDocumentation_MustBeFrozen()
    {
        var findings = ReadRepoFile("docs/architecture/commercehub-marketplace-split-order-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS M2-D Marketplace Split Order Authority", findings, StringComparison.Ordinal);
        Assert.Contains("Split findings", findings, StringComparison.Ordinal);
        Assert.Contains("Composition findings", findings, StringComparison.Ordinal);
        Assert.Contains("Runtime hardening performed", findings, StringComparison.Ordinal);
        Assert.Contains("Marketplace Split Order Authority is production-certifiable", findings, StringComparison.Ordinal);
        Assert.Contains("PASS M2-D Marketplace Split Order Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS M2-D Marketplace Split Order Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS M2-D", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS M2-D Marketplace Split Order Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS M2-D Marketplace Split Order Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
