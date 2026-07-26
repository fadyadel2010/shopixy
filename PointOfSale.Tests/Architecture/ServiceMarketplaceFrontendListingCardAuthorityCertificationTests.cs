namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendListingCardAuthorityCertificationTests
{
    [Fact]
    public void ListingCards_MustDeriveBoundedPresentationFromBackendDtos()
    {
        var authority = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/listingCardAuthority.ts");
        var analyticsAuthority = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/analyticsIdentityAuthority.ts");
        var reputationAuthority = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/reputationPresentationAuthority.ts");
        var card = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/components/ServiceCard.tsx");

        Assert.Contains("deriveListingCardPresentation", authority, StringComparison.Ordinal);
        Assert.Contains("if (!serviceId || !providerProfileId || !title || !providerDisplayName)", authority, StringComparison.Ordinal);
        Assert.Contains("deriveReputationSummaryPresentation(service)", authority, StringComparison.Ordinal);
        Assert.Contains("deriveServiceAnalyticsPresentation(service)", authority, StringComparison.Ordinal);
        Assert.Contains("averageRating < 0", reputationAuthority, StringComparison.Ordinal);
        Assert.Contains("averageRating > 5", reputationAuthority, StringComparison.Ordinal);
        Assert.Contains("completedRequests > totalRequests", analyticsAuthority, StringComparison.Ordinal);
        Assert.Contains("completionRate > 1", analyticsAuthority, StringComparison.Ordinal);
        Assert.Contains("Math.abs(completionRate - expectedRate) > 1e-12", analyticsAuthority, StringComparison.Ordinal);
        Assert.Contains("startingPrice >= 0", authority, StringComparison.Ordinal);
        Assert.Contains("/^[A-Z]{3}$/.test(currency)", authority, StringComparison.Ordinal);
        Assert.Contains("deriveListingCardPresentation(service)", card, StringComparison.Ordinal);
        Assert.Contains("if (!card) return null", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Number(service.averageRating", card, StringComparison.Ordinal);
    }

    [Fact]
    public void ListingCards_MustKeepTrustAnalyticsAndMediaDerivative()
    {
        var authority = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/listingCardAuthority.ts");
        var reputationAuthority = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/reputationPresentationAuthority.ts");
        var card = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/components/ServiceCard.tsx");

        Assert.Contains("reputationBadgeSet.has(summary.trustBadge)", reputationAuthority, StringComparison.Ordinal);
        Assert.Contains("serviceMediaPrefix", authority, StringComparison.Ordinal);
        Assert.Contains("storageKey.includes('..')", authority, StringComparison.Ordinal);
        Assert.Contains("primaryImage: parsePrimaryImage(service.serviceImages)", authority, StringComparison.Ordinal);
        Assert.Contains("card.averageRating !== null", card, StringComparison.Ordinal);
        Assert.Contains("card.completedRequests !== null", card, StringComparison.Ordinal);
        Assert.Contains("card.startingPrice !== null", card, StringComparison.Ordinal);
        Assert.Contains("onError={(event)", card, StringComparison.Ordinal);
        Assert.DoesNotContain("dangerouslySetInnerHTML", card, StringComparison.Ordinal);
    }

    [Fact]
    public void ListingCardNavigationAndCache_MustReenterCurrentAuthority()
    {
        var card = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/components/ServiceCard.tsx");
        var discover = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/tabs/DiscoverTab.tsx");
        var details = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/hooks/useServiceDetails.ts");
        var boundary = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");

        Assert.Contains("encodeURIComponent(card.serviceId)", card, StringComparison.Ordinal);
        Assert.DoesNotContain("state={{", card, StringComparison.Ordinal);
        Assert.Contains("const data = authorityFailed || isValidating ? undefined", discover, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.service(serviceId, authorityScope)", details, StringComparison.Ordinal);
        Assert.Contains("marketplaceAuthorityQueryOptions", details, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", boundary, StringComparison.Ordinal);
        Assert.Contains("invalidateQueries", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs1B()
    {
        var findings = ReadRepoFile(
            "docs/architecture/service-marketplace-frontend-listing-card-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S1-B",
            "Service Identity Findings",
            "Provider Findings",
            "Pricing Findings",
            "Rating Findings",
            "Trust Findings",
            "Analytics Findings",
            "Media Findings",
            "Cache Findings",
            "Navigation Findings",
            "Realtime Findings",
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

        Assert.Contains("PASS F-S1-B Service Marketplace Frontend Listing Card Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-B Service Marketplace Frontend Listing Card Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-B", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-B Service Marketplace Frontend Listing Card Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-B Service Marketplace Frontend Listing Card Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) =>
        CertificationSource.ReadRepoFile(relativePath);
}
