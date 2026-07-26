namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendTrustReputationPresentationAuthorityCertificationTests
{
    [Fact]
    public void RuntimeTrustPresentation_MustRemainBackendDerived()
    {
        var detailAuthority = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/serviceDetailAuthority.ts");
        var listingAuthority = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/listingCardAuthority.ts");
        var reputationAuthority = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/reputationPresentationAuthority.ts");
        var detailPage = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx");

        Assert.Contains("deriveReputationDistributionPresentation(trust)", detailAuthority, StringComparison.Ordinal);
        Assert.Contains("trust: parseTrust(service.trust)", detailAuthority, StringComparison.Ordinal);
        Assert.Contains("deriveServiceReviewPresentation", detailAuthority, StringComparison.Ordinal);
        Assert.Contains("evidenceTypes.has(evidenceType)", detailAuthority, StringComparison.Ordinal);
        Assert.DoesNotContain("revenueAmount:", detailAuthority, StringComparison.Ordinal);

        Assert.Contains("deriveReputationSummaryPresentation(service)", listingAuthority, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.round(service.averageRating", listingAuthority, StringComparison.Ordinal);

        Assert.Contains("isBadgeConsistent", reputationAuthority, StringComparison.Ordinal);
        Assert.Contains("totalRatings !== reputation.totalReviews", reputationAuthority, StringComparison.Ordinal);
        Assert.Contains("distributionAverage !== reputation.averageRating", reputationAuthority, StringComparison.Ordinal);

        Assert.Contains("const authoritativeReviewCount = service.trust?.totalReviews ?? 0", detailPage, StringComparison.Ordinal);
        Assert.Contains("formatNumber(authoritativeReviewCount)", detailPage, StringComparison.Ordinal);
        Assert.DoesNotContain("formatNumber(reviewsAvailable ? reviews.length", detailPage, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeTrustCachesAndRealtime_MustInvalidateNotPublishAuthority()
    {
        var serviceHook = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/hooks/useServiceDetails.ts");
        var searchHook = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/hooks/useMarketplaceSearch.ts");
        var boundary = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");
        var queryKeys = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/queryKeys.ts");

        Assert.Contains("marketplaceKeys.service(serviceId, authorityScope)", serviceHook, StringComparison.Ordinal);
        Assert.Contains("marketplaceAuthorityQueryOptions", serviceHook, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.search", searchHook, StringComparison.Ordinal);
        Assert.Contains("marketplaceAuthorityQueryOptions", searchHook, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", boundary, StringComparison.Ordinal);
        Assert.Contains("invalidateQueries", boundary, StringComparison.Ordinal);
        Assert.Contains("scope: (scope = getMarketplaceAuthorityScope())", queryKeys, StringComparison.Ordinal);
        Assert.Contains("service: (serviceId: string, scope = getMarketplaceAuthorityScope())", queryKeys, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs1D()
    {
        var findings = ReadRepoFile(
            "docs/architecture/service-marketplace-frontend-trust-reputation-presentation-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S1-D",
            "Rating Findings",
            "Review Findings",
            "Distribution Findings",
            "Trust Badge Findings",
            "Campaign Evidence Findings",
            "Provider Reputation Findings",
            "Cache Findings",
            "Realtime Findings",
            "Lifecycle Findings",
            "History Findings",
            "Tenant Isolation Findings",
            "Failure-Handling Findings",
            "Adversarial Findings",
            "Runtime Hardening Performed",
            "Exact Runtime Files Modified",
            "Regression Freezes Added",
            "Architecture Updates",
            "Explicit Answers"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS F-S1-D Service Marketplace Frontend Trust & Reputation Presentation Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-D Service Marketplace Frontend Trust & Reputation Presentation Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-D", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-D Service Marketplace Frontend Trust & Reputation Presentation Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-D Service Marketplace Frontend Trust & Reputation Presentation Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) =>
        CertificationSource.ReadRepoFile(relativePath);
}
