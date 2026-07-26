namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubMarketplaceCheckoutOperationalReadinessCertificationTests
{
    [Fact]
    public void OperationalCertificationDocumentation_MustContainRequiredSectionsAndDecision()
    {
        var certification = ReadRepoFile("docs/architecture/COMMERCEHUB_MARKETPLACE_CHECKOUT_4_OPERATIONAL_READINESS_CERTIFICATION.md");

        foreach (var marker in new[]
        {
            "## Executive Summary",
            "## Operational Readiness Matrix",
            "## Marketplace Operations Review",
            "## Dependency Review",
            "## Security Review",
            "## Observability Review",
            "## Recovery Drills",
            "## Browser Matrix",
            "## Accessibility Evidence",
            "## Rollout Checklist",
            "## Rollback Checklist",
            "## Known Operational Gates",
            "## Certification Decision",
            "READY AFTER OPERATIONAL CHECKLIST",
            "Production Ready? **No.**",
            "Reviewed ONLY Marketplace Operational Readiness? **Yes.**"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Certification decision: NOT READY", certification, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarketplaceOperationalReadiness_MustPreserveCertifiedCheckoutPaymentShippingAndSettlementReuse()
    {
        var certification = ReadRepoFile("docs/architecture/COMMERCEHUB_MARKETPLACE_CHECKOUT_4_OPERATIONAL_READINESS_CERTIFICATION.md");
        var runtime = ReadRepoFile("docs/architecture/COMMERCEHUB_MARKETPLACE_CHECKOUT_1_RUNTIME.md");
        var api = ReadRepoFile("docs/architecture/COMMERCEHUB_MARKETPLACE_CHECKOUT_2_APIS.md");
        var frontend = ReadRepoFile("docs/architecture/COMMERCEHUB_MARKETPLACE_CHECKOUT_3_FRONTEND.md");

        foreach (var document in new[] { certification, runtime, api, frontend })
        {
            Assert.Contains("Marketplace", document, StringComparison.Ordinal);
            Assert.Contains("Checkout", document, StringComparison.Ordinal);
        }

        Assert.Contains("Marketplace Checkout operational readiness is certified to proceed to controlled rollout after the checklist gates", certification, StringComparison.Ordinal);
        Assert.Contains("checkout: \"/api/commercehub/checkout\"", ReadRepoFile("PointOfSale.CommerceHub/src/api/endpoints.ts"), StringComparison.Ordinal);
        Assert.Contains("cartPricing: \"/api/commercehub/cart/price\"", ReadRepoFile("PointOfSale.CommerceHub/src/api/endpoints.ts"), StringComparison.Ordinal);
        Assert.Contains("using var financialBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();", ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs"), StringComparison.Ordinal);
        Assert.Contains("using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();", ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs"), StringComparison.Ordinal);
        Assert.Contains("using var shippingAssignmentBoundary = CommerceHubBoundaryMutationScope.BeginShippingAssignment();", ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceOperationalReadiness_MustRecordOperationalGatesWithoutContextUpdates()
    {
        var certification = ReadRepoFile("docs/architecture/COMMERCEHUB_MARKETPLACE_CHECKOUT_4_OPERATIONAL_READINESS_CERTIFICATION.md");

        foreach (var gate in new[]
        {
            "Marketplace checkout dashboards and alert rules deployed and tested.",
            "Merchant grouping and split-order operational drills completed.",
            "Settlement candidate, commission candidate, and replay/idempotency drills completed.",
            "Payment pending, retry, provider timeout, and callback/webhook operational evidence completed.",
            "Shipping retry and provider unavailability drills completed.",
            "Browser refresh, offline, slow-network, desktop, tablet, and mobile evidence completed.",
            "Accessibility smoke evidence completed.",
            "PII/secret/token redaction verified in logs and traces.",
            "Correlation ID propagation verified across checkout/payment/shipping/order/recovery/settlement.",
            "Support runbook, incident escalation, rollout signoff, and rollback rehearsal completed."
        })
        {
            Assert.Contains(gate, certification, StringComparison.Ordinal);
        }

        Assert.Contains("The five `.ai-context` authority files were reviewed but not modified by this phase.", certification, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
