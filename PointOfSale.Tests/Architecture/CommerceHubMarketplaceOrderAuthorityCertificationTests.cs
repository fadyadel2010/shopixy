namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubMarketplaceOrderAuthorityCertificationTests
{
    [Fact]
    public void MarketplaceOrderCreation_MustRemainSqlIdempotentAndVendorAllocated()
    {
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var subOrderConfiguration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubSubOrderConfiguration.cs");
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260610193000_CommerceHubMarketplaceOrderAuthorityHardening.cs");

        Assert.Contains("IsolationLevel.Serializable", checkout, StringComparison.Ordinal);
        Assert.Contains("LoadMarketplaceOrderByIdempotencyKeyAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("EnsureExistingCheckoutOrderReplayIsRequestHashBoundAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("BuildMarketplaceOrder", checkout, StringComparison.Ordinal);
        Assert.Contains("GroupBy(line => line.StoreId)", checkout, StringComparison.Ordinal);
        Assert.Contains("MarketplaceOrderId = marketplaceOrder.Id", checkout, StringComparison.Ordinal);
        Assert.Contains("StoreId = group.Key", checkout, StringComparison.Ordinal);
        Assert.Contains("ReserveOrderItemAsync", checkout, StringComparison.Ordinal);

        Assert.Contains("entity.HasIndex(order => order.OrderNumber).IsUnique()", dbContext, StringComparison.Ordinal);
        Assert.Contains("entity.HasIndex(order => order.IdempotencyKey)", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubSubOrders_MarketplaceOrderId_StoreId_Live", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubSubOrders_MarketplaceOrderId_StoreId_Live", subOrderConfiguration, StringComparison.Ordinal);
        Assert.Contains("duplicate live sub-orders exist", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorLifecycleOperations_MustRevalidateCurrentVendorAuthority()
    {
        var orderManagement = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderManagementService.cs");
        var governance = ReadRepoFile("PointOfSale.API/Services/Governance/UserGovernanceService.cs");
        var governanceInterface = ReadRepoFile("PointOfSale.API/Services/Governance/IUserGovernanceService.cs");

        Assert.Contains("EnsureVendorOrderOperationAllowedAsync(storeId", orderManagement, StringComparison.Ordinal);
        Assert.Contains("SUBORDER_FORBIDDEN", orderManagement, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorAuthority.LoadAsync", governance, StringComparison.Ordinal);
        Assert.Contains("EnsureMarketplaceEligibility(authority, \"operate CommerceHub orders\")", governance, StringComparison.Ordinal);
        Assert.Contains("VENDOR_SUSPENDED", governance, StringComparison.Ordinal);
        Assert.Contains("Vendor is suspended and order operations are frozen", governance, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorOrderOperationAllowedAsync", governanceInterface, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceOrderLineage_MustRemainImmutableAndProjectionReadsDerivative()
    {
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var repairService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderProjectionRepairService.cs");
        var repairSupport = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderProjectionRepairSupport.cs");
        var repository = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/CommerceHubOrderReadRepository.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubOrdersController.cs");

        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubMarketplaceOrder>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubSubOrder>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardImmutableCommerceHubEntity<CommerceHubSubOrderItem>", dbContext, StringComparison.Ordinal);
        Assert.Contains("records are immutable. Blocked fields", dbContext, StringComparison.Ordinal);
        Assert.Contains("LoadOrderAuthoritySnapshotAsync", controller, StringComparison.Ordinal);
        Assert.Contains("authenticatedCustomerId.Value == authority.CustomerId", controller, StringComparison.Ordinal);
        Assert.Contains("EnsureOrderProjectedAsync", repairService, StringComparison.Ordinal);
        Assert.Contains("ProjectionProvesSqlAuthority", repairSupport, StringComparison.Ordinal);
        Assert.Contains("ProjectionChecksum", repairSupport, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(document => document.LastSourceMutationTicks)", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceOrderAuthorityDocumentation_MustBeFrozen()
    {
        var findings = ReadRepoFile("docs/architecture/commercehub-marketplace-order-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS M2 CommerceHub Marketplace Order Authority", findings, StringComparison.Ordinal);
        Assert.Contains("Order identity findings", findings, StringComparison.Ordinal);
        Assert.Contains("Vendor-allocation findings", findings, StringComparison.Ordinal);
        Assert.Contains("Runtime hardening performed", findings, StringComparison.Ordinal);
        Assert.Contains("CommerceHub Marketplace Order Authority is production-certifiable", findings, StringComparison.Ordinal);
        Assert.Contains("PASS M2 CommerceHub Marketplace Order Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS M2 CommerceHub Marketplace Order Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS M2", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS M2 CommerceHub Marketplace Order Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS M2 CommerceHub Marketplace Order Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceOrderFinancialAuthority_FrontendMustRemainBackendDerivedAndFailClosed()
    {
        var financialAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/features/orderFinancialAuthority.ts");
        var customerOrders = ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/orderIdentityAuthority.ts");
        var checkoutAuthority = ReadRepoFile("PointOfSale.CommerceHub/src/features/checkout/orderCreationAuthority.ts");
        var cartService = ReadRepoFile("PointOfSale.CommerceHub/src/services/cart-service.ts");
        var cartPricing = ReadRepoFile("PointOfSale.CommerceHub/src/cart/useCommerceHubCartPricing.ts");
        var checkoutService = ReadRepoFile("PointOfSale.CommerceHub/src/services/checkout-service.ts");
        var checkoutPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx");
        var refundService = ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-refunds-service.ts");
        var refundPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerRefundsPage.tsx");
        var superAdminOrders = ReadRepoFile("PointOfSale.Web/src/super-admin/orders/orderIdentityAuthority.ts");

        Assert.Contains("requireMarketplaceOrderFinancialAuthority", financialAuthority, StringComparison.Ordinal);
        Assert.Contains("contradictory parent and split totals", financialAuthority, StringComparison.Ordinal);
        Assert.Contains("contradictory split item totals", financialAuthority, StringComparison.Ordinal);
        Assert.Contains("requireCommerceHubCartPricingAuthority", financialAuthority, StringComparison.Ordinal);
        Assert.Contains("requirePaymentAmountAuthority", financialAuthority, StringComparison.Ordinal);
        Assert.Contains("requireCustomerRefundFinancialAuthority", financialAuthority, StringComparison.Ordinal);

        Assert.Contains("requireMarketplaceOrderFinancialAuthority(order)", customerOrders, StringComparison.Ordinal);
        Assert.Contains("requireMarketplaceOrderFinancialAuthority(response)", checkoutAuthority, StringComparison.Ordinal);
        Assert.Contains("requireCommerceHubCartPricingAuthority(response.data)", cartService, StringComparison.Ordinal);
        Assert.DoesNotContain("keepPreviousData", cartPricing, StringComparison.Ordinal);
        Assert.Contains("expectedAmount: number, expectedCurrency: string", checkoutService, StringComparison.Ordinal);
        Assert.Contains("response.total, response.currency", checkoutPage, StringComparison.Ordinal);
        Assert.Contains("requireCustomerRefundFinancialAuthority", refundService, StringComparison.Ordinal);
        Assert.Contains("refundsQuery.isFetching || refundsQuery.isError ? []", refundPage, StringComparison.Ordinal);
        Assert.Contains("contradictory aggregate financial evidence", superAdminOrders, StringComparison.Ordinal);
        Assert.Contains("contradictory parent and split totals", superAdminOrders, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
