namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendKpiMetricsPresentationAuthorityCertificationTests
{
    [Fact]
    public void RuntimeKpiMetricsPresentation_MustRemainBackendDerivedAndCrossMetricConsistent()
    {
        var controller = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/MarketplaceController.cs");
        var analyticsAuthority = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/analyticsIdentityAuthority.ts");
        var reputationAuthority = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/reputationPresentationAuthority.ts");
        var detailPage = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx");
        var profileTab = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/tabs/ProfileTab.tsx");
        var serviceCard = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/components/ServiceCard.tsx");

        Assert.Contains("BuildRatingPercentages", controller, StringComparison.Ordinal);
        Assert.Contains("MidpointRounding.AwayFromZero", controller, StringComparison.Ordinal);
        Assert.Contains("ratingPercentages", controller, StringComparison.Ordinal);

        Assert.Contains("completedRequests > totalRequests", analyticsAuthority, StringComparison.Ordinal);
        Assert.Contains("Math.abs(completionRate - expectedRate) > 1e-12", analyticsAuthority, StringComparison.Ordinal);
        Assert.Contains("totalRatings !== reputation.totalReviews", reputationAuthority, StringComparison.Ordinal);
        Assert.Contains("distributionAverage !== reputation.averageRating", reputationAuthority, StringComparison.Ordinal);
        Assert.Contains("Math.abs(percentage - expectedPercentage) > 1e-9", reputationAuthority, StringComparison.Ordinal);

        Assert.Contains("style={{ width: `${percentage}%` }}", detailPage, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "(count / service.trust.totalReviews) * 100",
            detailPage,
            StringComparison.Ordinal);
        Assert.Contains("serviceReviewCount === null", detailPage, StringComparison.Ordinal);
        Assert.Contains("deriveProviderAnalyticsPresentation(statsData", profileTab, StringComparison.Ordinal);
        Assert.Contains("deriveListingCardPresentation(service)", serviceCard, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeKpiCachesRealtimeHistoryVisibilityAndFailures_MustRemainDerivative()
    {
        var authority = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/authority.ts");
        var boundary = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");
        var profileTab = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/tabs/ProfileTab.tsx");
        var discoverTab = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/tabs/DiscoverTab.tsx");

        Assert.Contains("staleTime: 0", authority, StringComparison.Ordinal);
        Assert.Contains("retry: false", authority, StringComparison.Ordinal);
        Assert.Contains("refetchOnReconnect: 'always'", authority, StringComparison.Ordinal);
        Assert.Contains("refetchOnWindowFocus: 'always'", authority, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", boundary, StringComparison.Ordinal);
        Assert.Contains("invalidateQueries", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", boundary, StringComparison.Ordinal);
        Assert.Contains("!statsAuthorityRefreshing", profileTab, StringComparison.Ordinal);
        Assert.Contains("!statsAuthorityRefreshFailed", profileTab, StringComparison.Ordinal);
        Assert.Contains("authorityFailed || isValidating ? undefined : searchData", discoverTab, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs6B()
    {
        var findings = CertificationSource.ReadRepoFile(
            "docs/architecture/service-marketplace-frontend-kpi-metrics-presentation-authority-certification-findings.md");
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S6-B",
            "Metric Findings",
            "Count Findings",
            "Rate Findings",
            "Consistency Findings",
            "Badge Findings",
            "Visibility Findings",
            "Cache Findings",
            "Realtime Findings",
            "History Findings",
            "Failure Findings",
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

        Assert.Contains("PASS F-S6-B", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S6-B", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S6-B", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S6-B", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S6-B", codexRules, StringComparison.Ordinal);
    }
}
