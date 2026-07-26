namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubAuthorityFlowArchitectureCertificationTests
{
    [Fact]
    public void RuntimeAuthorityFlowGraph_MustRemainEvidenceForwardAndSingleOwner()
    {
        var boundary = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/CommerceHubBoundaryMutationScope.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var publication = CertificationSource.ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var cart = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var payment = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var refund = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs");
        var payout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubOrderProjectionConsumer.cs");
        var projectionRepair = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderProjectionRepairService.cs");
        var searchOverlay = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var shipmentPolicy = CertificationSource.ReadRepoFile("PointOfSale.Core/Policies/ShipmentLifecycleTransitionPolicy.cs");
        var reconciliation = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationJob.cs");

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

        foreach (var marker in new[]
        {
            "GuardCommerceHubBoundaryOwnership",
            "GuardBoundaryOwnedProperties<CommerceHubMarketplaceOrder>",
            "GuardBoundaryOwnedProperties<CommerceHubSubOrder>",
            "GuardBoundaryOwnedProperties<CommerceHubPayment>",
            "GuardBoundaryOwnedProperties<CommerceHubRefund>",
            "GuardBoundaryOwnedProperties<CommerceHubPayout>",
            "GuardBoundaryOwnedProperties<CommerceHubVendorEarning>",
            "IsShippingAssignmentAuthorized",
            "CommerceHub projection markers can only be changed by the CommerceHub projection boundary"
        })
        {
            Assert.Contains(marker, dbContext, StringComparison.Ordinal);
        }

        Assert.Contains("EnsureVendorListingAllowedAsync", publication, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", publication, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.IsLiveAsync", publication, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage()", cart, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", checkout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginShippingAssignment()", checkout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginFinancialWorkflow()", checkout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPaymentLifecycle()", checkout, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", checkout, StringComparison.Ordinal);
        Assert.Contains("BuildCheckoutRequestHash", checkout, StringComparison.Ordinal);
        Assert.Contains("EnsureCheckoutIdempotencyRequestMatches", checkout, StringComparison.Ordinal);
        Assert.Contains("PRICE_CHANGED", checkout, StringComparison.Ordinal);
        Assert.Contains("SHIPPING_CHANGED", checkout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayment()", payment, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", payment, StringComparison.Ordinal);
        Assert.Contains("BuildPaymentLockKey", payment, StringComparison.Ordinal);
        Assert.Contains("unresolved payment webhooks", payment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginRefund()", refund, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", refund, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", payout, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferIdempotencyKey", payout, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", payout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginProjection()", projection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginProjection()", projectionRepair, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("CanTransition", shipmentPolicy, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginOrderLifecycle()", reconciliation, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentationFreeze_MustRecordArchThreeAuthorityFlowCertification()
    {
        var findings = CertificationSource.ReadRepoFile("docs/architecture/commercehub-authority-flow-review-findings.md");
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "ARCH-3 CommerceHub Authority Flow Review",
            "Authority Graph Findings",
            "Product Flow Findings",
            "Publication Flow Findings",
            "Catalog Flow Findings",
            "Search Flow Findings",
            "Cart Flow Findings",
            "Shipping Flow Findings",
            "Checkout Flow Findings",
            "Split-Order Flow Findings",
            "Order Flow Findings",
            "Payment Flow Findings",
            "Shipment Flow Findings",
            "Tracking Flow Findings",
            "Refund Flow Findings",
            "Return Flow Findings",
            "Earnings Flow Findings",
            "Settlement Flow Findings",
            "Payout Flow Findings",
            "Reconciliation Flow Findings",
            "Event Authority Findings",
            "Projection Authority Findings",
            "Cache Authority Findings",
            "Replay Authority Findings",
            "Recovery Authority Findings",
            "Lifecycle Authority Findings",
            "Concurrency Authority Findings",
            "Tenant Authority Findings",
            "Cross-Domain Authority Findings",
            "Failure Authority Findings",
            "Architectural Risks",
            "Runtime Hardening Performed",
            "Exact Runtime Files Modified",
            "Regression Freezes Added",
            "Architecture Updates"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS ARCH-3 CommerceHub Authority Flow Review", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-3 CommerceHub Authority Flow Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-3 authority flow runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-3 CommerceHub Authority Flow Review", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS ARCH-3 CommerceHub Authority Flow Architecture Freeze", codexRules, StringComparison.Ordinal);
    }
}
