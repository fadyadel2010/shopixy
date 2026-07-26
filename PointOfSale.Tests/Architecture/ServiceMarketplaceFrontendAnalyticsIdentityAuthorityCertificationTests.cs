namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendAnalyticsIdentityAuthorityCertificationTests
{
    [Fact]
    public void RuntimeAnalyticsIdentity_MustBindToCurrentBackendActorProviderAndMetrics()
    {
        var controller = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/MarketplaceController.cs");
        var authority = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/analyticsIdentityAuthority.ts");
        var profileTab = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/tabs/ProfileTab.tsx");
        var listingAuthority = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/listingCardAuthority.ts");
        var queryKeys = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/queryKeys.ts");

        Assert.Contains("profile.TenantId != tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_PROFILE_NOT_FOUND", controller, StringComparison.Ordinal);
        Assert.Contains("MARKETPLACE_PROVIDER_NOT_VISIBLE", controller, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", controller, StringComparison.Ordinal);
        Assert.Contains("governance.Status == UserGovernanceStatus.Suspended", controller, StringComparison.Ordinal);
        Assert.Contains("governance.Status == UserGovernanceStatus.Banned", controller, StringComparison.Ordinal);
        Assert.Contains("providerUserId = userId", controller, StringComparison.Ordinal);
        Assert.Contains("providerProfileId = profile.Id", controller, StringComparison.Ordinal);
        Assert.Contains("r.Status == Core.Enums.ServiceRequestStatus.CompletedByBuyer", controller, StringComparison.Ordinal);

        Assert.Contains("expectedScope !== `${tenantId}:${providerUserId}`", authority, StringComparison.Ordinal);
        Assert.Contains("providerProfileId !== expectedProfileId", authority, StringComparison.Ordinal);
        Assert.Contains("deriveProviderReputationStatsPresentation(stats)", authority, StringComparison.Ordinal);
        Assert.Contains("Math.abs(completionRate - expectedRate) > 1e-12", authority, StringComparison.Ordinal);
        Assert.Contains("deriveServiceAnalyticsPresentation(service)", listingAuthority, StringComparison.Ordinal);

        Assert.Contains("marketplaceKeys.myProfileStats(authorityScope)", profileTab, StringComparison.Ordinal);
        Assert.Contains("profileAuthorityRefreshing", profileTab, StringComparison.Ordinal);
        Assert.Contains("profileAuthorityRefreshFailed", profileTab, StringComparison.Ordinal);
        Assert.Contains("deriveProviderAnalyticsPresentation(statsData", profileTab, StringComparison.Ordinal);
        Assert.Contains("'my-profile-stats'", queryKeys, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeAnalyticsCacheRealtimeHistoryAndFailures_MustRemainDerivative()
    {
        var authority = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/authority.ts");
        var boundary = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");
        var profileTab = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/tabs/ProfileTab.tsx");
        var serviceCard = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/marketplace/components/ServiceCard.tsx");

        Assert.Contains("staleTime: 0", authority, StringComparison.Ordinal);
        Assert.Contains("retry: false", authority, StringComparison.Ordinal);
        Assert.Contains("refetchOnReconnect: 'always'", authority, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", boundary, StringComparison.Ordinal);
        Assert.Contains("invalidateQueries", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", profileTab, StringComparison.Ordinal);
        Assert.DoesNotContain("state={{", serviceCard, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs6A()
    {
        var findings = CertificationSource.ReadRepoFile(
            "docs/architecture/service-marketplace-frontend-analytics-identity-authority-certification-findings.md");
        var projectContext = CertificationSource.ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = CertificationSource.ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = CertificationSource.ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = CertificationSource.ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = CertificationSource.ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S6-A",
            "Analytics Identity Findings",
            "Provider Findings",
            "Metric Findings",
            "KPI Findings",
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

        Assert.Contains("PASS F-S6-A", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S6-A", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S6-A", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S6-A", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S6-A", codexRules, StringComparison.Ordinal);
    }
}
