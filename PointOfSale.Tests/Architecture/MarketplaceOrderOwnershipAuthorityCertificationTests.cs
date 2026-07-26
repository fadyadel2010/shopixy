namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceOrderOwnershipAuthorityCertificationTests
{
    [Fact]
    public void FrontendCustomerOrderOwnership_MustWithholdCachedRowsDuringCurrentAuthorityRefresh()
    {
        var ordersPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerOrdersPage.tsx");
        var dashboardPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerDashboardPage.tsx");
        var customerAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/orderIdentityAuthority.ts");
        var customerOrdersService = ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-orders-service.ts");

        Assert.Contains("requireCustomerOrdersListAuthority(response.data)", customerOrdersService, StringComparison.Ordinal);
        Assert.Contains("requireCustomerOrderDetailAuthority(response.data, orderId)", customerOrdersService, StringComparison.Ordinal);
        Assert.Contains("seenOrderIds.has(order.orderId)", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("order.orderId !== expectedOrderId", customerAuthority, StringComparison.Ordinal);
        Assert.Contains("ordersQuery.isFetching || ordersQuery.isError ? []", ordersPage, StringComparison.Ordinal);
        Assert.Contains("ordersQuery.isLoading || ordersQuery.isFetching", ordersPage, StringComparison.Ordinal);
        Assert.Contains("recentOrdersQuery.isFetching || recentOrdersQuery.isError ? []", dashboardPage, StringComparison.Ordinal);
        Assert.Contains("recentOrdersQuery.isLoading || recentOrdersQuery.isFetching", dashboardPage, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendSuperAdminOrderVisibility_MustRemainDerivativeAndSelectedStateFenced()
    {
        var ordersPage = ReadRepoFile("PointOfSale.Web/src/super-admin/orders/OrdersPage.tsx");
        var ordersService = ReadRepoFile("PointOfSale.Web/src/super-admin/orders/ordersService.ts");
        var superAdminAuthority = ReadRepoFile("PointOfSale.Web/src/super-admin/orders/orderIdentityAuthority.ts");
        var superAdminAuthorityTests = ReadRepoFile("PointOfSale.Web/src/super-admin/orders/orderIdentityAuthority.test.ts");

        Assert.Contains("requireSuperAdminOrderListAuthority(response.data.orders ?? [])", ordersService, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminOrderDetailAuthority(response.data, orderId)", ordersService, StringComparison.Ordinal);
        Assert.Contains("ordersQuery.isFetching || ordersQuery.isError ? []", ordersPage, StringComparison.Ordinal);
        Assert.Contains("!orders.some((order) => order.orderId === selectedOrderId)", ordersPage, StringComparison.Ordinal);
        Assert.Contains("setSelectedOrderId(null)", ordersPage, StringComparison.Ordinal);
        Assert.Contains("seenSubOrderIds.has(subOrder.subOrderId)", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("!isNonEmptyString(subOrder.vendorId)", superAdminAuthority, StringComparison.Ordinal);
        Assert.Contains("rejects detail identity that does not match the selected order route", superAdminAuthorityTests, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceOrderOwnershipAuthorityDocumentation_MustBeFrozen()
    {
        var findings = ReadRepoFile("docs/architecture/commercehub-marketplace-order-ownership-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS M2-C Marketplace Order Ownership Authority", findings, StringComparison.Ordinal);
        Assert.Contains("Ownership findings", findings, StringComparison.Ordinal);
        Assert.Contains("Runtime hardening performed", findings, StringComparison.Ordinal);
        Assert.Contains("Marketplace Order Ownership Authority is production-certifiable", findings, StringComparison.Ordinal);
        Assert.Contains("PASS M2-C Marketplace Order Ownership Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS M2-C Marketplace Order Ownership Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS M2-C", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS M2-C Marketplace Order Ownership Authority Hardening", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS M2-C Marketplace Order Ownership Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
