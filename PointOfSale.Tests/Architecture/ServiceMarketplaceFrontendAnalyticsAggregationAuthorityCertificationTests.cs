namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendAnalyticsAggregationAuthorityCertificationTests
{
    [Fact]
    public void RuntimeAggregatesRollupsAndSummaries_MustRemainBackendDerivedAndConsistent()
    {
        var analyticsAuthority = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/analyticsIdentityAuthority.ts");
        var reputationAuthority = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/reputationPresentationAuthority.ts");
        var listingAuthority = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/listingCardAuthority.ts");
        var profileTab = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/tabs/ProfileTab.tsx");

        Assert.Contains("deriveProviderReputationStatsPresentation(stats)", analyticsAuthority, StringComparison.Ordinal);
        Assert.Contains("reputation.totalReviews > completedRequests", reputationAuthority, StringComparison.Ordinal);
        Assert.Contains("reputation.totalReviews <= analytics.completedRequests", listingAuthority, StringComparison.Ordinal);
        Assert.Contains("acceptedReputation", listingAuthority, StringComparison.Ordinal);
        Assert.Contains("acceptedAnalytics", listingAuthority, StringComparison.Ordinal);
        Assert.Contains("deriveProviderAnalyticsPresentation(statsData", profileTab, StringComparison.Ordinal);

        Assert.DoesNotContain("setQueryData", analyticsAuthority, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", listingAuthority, StringComparison.Ordinal);
        Assert.DoesNotContain("reviews.length", profileTab, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeCacheRealtimeActorHistoryAndFailurePaths_MustRevalidateOrWithholdOnly()
    {
        var authority = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/authority.ts");
        var boundary = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");
        var queryKeys = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/queryKeys.ts");
        var profileTab = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/tabs/ProfileTab.tsx");

        Assert.Contains("staleTime: 0", authority, StringComparison.Ordinal);
        Assert.Contains("retry: false", authority, StringComparison.Ordinal);
        Assert.Contains("refetchOnReconnect: 'always'", authority, StringComparison.Ordinal);
        Assert.Contains("refetchOnWindowFocus: 'always'", authority, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", boundary, StringComparison.Ordinal);
        Assert.Contains("invalidateQueries", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", boundary, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.myProfileStats(authorityScope)", profileTab, StringComparison.Ordinal);
        Assert.Contains("!statsAuthorityRefreshing", profileTab, StringComparison.Ordinal);
        Assert.Contains("!statsAuthorityRefreshFailed", profileTab, StringComparison.Ordinal);
        Assert.Contains("'my-profile-stats'", queryKeys, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs6C()
    {
        var findings = CertificationSource.ReadRepoFile(
            "docs/architecture/service-marketplace-frontend-analytics-aggregation-authority-certification-findings.md");
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S6-C",
            "Aggregate Findings",
            "Rollup Findings",
            "Summary Findings",
            "Consistency Findings",
            "Visibility Findings",
            "Cache Findings",
            "Realtime Findings",
            "Actor-Transition Findings",
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

        Assert.Contains("PASS F-S6-C", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S6-C", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S6-C", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S6-C", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S6-C", codexRules, StringComparison.Ordinal);
    }
}
