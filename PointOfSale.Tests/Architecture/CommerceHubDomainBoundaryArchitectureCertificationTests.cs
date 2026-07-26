namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubDomainBoundaryArchitectureCertificationTests
{
    [Fact]
    public void RuntimeBoundaryGraph_MustKeepDomainTruthOwnedByExactMutationScopes()
    {
        var boundary = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/CommerceHubBoundaryMutationScope.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var payment = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var refund = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs");
        var payout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubOrderProjectionConsumer.cs");
        var publication = CertificationSource.ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var searchOverlay = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");

        foreach (var marker in new[]
        {
            "BeginOrderLifecycle",
            "BeginPayment",
            "BeginRefund",
            "BeginPayout",
            "BeginFinancialWorkflow",
            "BeginPaymentLifecycle",
            "BeginShippingAssignment",
            "BeginProjection"
        })
        {
            Assert.Contains(marker, boundary, StringComparison.Ordinal);
        }

        Assert.Contains("GuardCommerceHubBoundaryOwnership", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubMarketplaceOrder>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubPayment>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubRefund>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubPayout>", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardBoundaryOwnedProperties<CommerceHubVendorEarning>", dbContext, StringComparison.Ordinal);
        Assert.Contains("IsShippingAssignmentAuthorized", dbContext, StringComparison.Ordinal);
        Assert.Contains("CommerceHub projection markers can only be changed by the CommerceHub projection boundary", dbContext, StringComparison.Ordinal);

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginShippingAssignment()", checkout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", payment, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayment()", payment, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginRefund()", refund, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", refund, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", payout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginProjection()", projection, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorListingAllowedAsync", publication, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", publication, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync", searchOverlay, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentationFreeze_MustRecordArchTwoDomainBoundaryCertification()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/commercehub-domain-boundary-review-findings.md");
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "ARCH-2 CommerceHub Domain Boundary Review",
            "Product Boundary Findings",
            "Publication Boundary Findings",
            "Catalog Boundary Findings",
            "Search Boundary Findings",
            "Cart Boundary Findings",
            "Shipping Boundary Findings",
            "Checkout Boundary Findings",
            "Split-Order Boundary Findings",
            "Order Boundary Findings",
            "Payment Boundary Findings",
            "Shipment Boundary Findings",
            "Tracking Boundary Findings",
            "Refund Boundary Findings",
            "Return Boundary Findings",
            "Earnings Boundary Findings",
            "Settlement Boundary Findings",
            "Payout Boundary Findings",
            "Reconciliation Boundary Findings",
            "Database Boundary Findings",
            "Service Boundary Findings",
            "Event Boundary Findings",
            "Authority Graph Findings",
            "Coupling Findings",
            "Tenant Boundary Findings",
            "Failure Findings",
            "Runtime Hardening Performed",
            "Exact Runtime Files Modified",
            "Regression Freezes Added",
            "Architecture Updates"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS ARCH-2 CommerceHub Domain Boundary Review", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-2 CommerceHub Domain Boundary Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-2 domain boundary runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-2 CommerceHub Domain Boundary Review", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-2 CommerceHub Domain Boundary Architecture Freeze", codexRules, StringComparison.Ordinal);
    }
}
