namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendServiceDetailAuthorityCertificationTests
{
    [Fact]
    public void DetailPresentation_MustRemainBoundedAndDerivative()
    {
        var authority = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/serviceDetailAuthority.ts");
        var page = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx");

        Assert.Contains("deriveServiceDetailPresentation", authority, StringComparison.Ordinal);
        Assert.Contains("service.isActive !== true", authority, StringComparison.Ordinal);
        Assert.Contains("startingPrice >= 0", authority, StringComparison.Ordinal);
        Assert.Contains("/^[A-Z]{3}$/.test(currency)", authority, StringComparison.Ordinal);
        Assert.Contains("deriveReputationDistributionPresentation(trust)", authority, StringComparison.Ordinal);
        Assert.Contains("storageKey.startsWith(serviceMediaPrefix)", authority, StringComparison.Ordinal);
        Assert.Contains("deriveServiceReviewPresentation", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("revenueAmount:", authority, StringComparison.Ordinal);
        Assert.Contains("deriveServiceDetailPresentation(serviceSnapshot)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON.parse(service.serviceImages)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("marketingEvidence.revenueAmount", page, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailCacheRefreshAndFailures_MustFailClosed()
    {
        var page = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx");
        var hook = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/hooks/useServiceDetails.ts");
        var boundary = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");

        Assert.Contains("isFetching: serviceAuthorityRefreshing", page, StringComparison.Ordinal);
        Assert.Contains("isRefetchError: serviceAuthorityRefreshFailed", page, StringComparison.Ordinal);
        Assert.Contains("const serviceAuthorityValidating = isLoading || serviceAuthorityRefreshing", page, StringComparison.Ordinal);
        Assert.Contains("if (serviceAuthorityValidating)", page, StringComparison.Ordinal);
        Assert.Contains("reviewsAuthorityRefreshing", page, StringComparison.Ordinal);
        Assert.Contains("deriveServiceReviewHistoryPresentation", page, StringComparison.Ordinal);
        Assert.Contains("reviewHistory.items.length > 0", page, StringComparison.Ordinal);
        Assert.Contains("isMarketplaceAuthorityError(reviewsError)", page, StringComparison.Ordinal);
        Assert.Contains("setRequestMessage('')", page, StringComparison.Ordinal);
        Assert.Contains("setShowRequestForm(false)", page, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.service(serviceId, authorityScope)", hook, StringComparison.Ordinal);
        Assert.Contains("marketplaceAuthorityQueryOptions", hook, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", boundary, StringComparison.Ordinal);
        Assert.Contains("invalidateQueries", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendDetailDto_MustReuseVisibilityQualifiedProviderAuthority()
    {
        var controller = ReadRepoFile(
            "PointOfSale.API/Controllers/MarketplaceController.cs");
        var catalog = ReadRepoFile(
            "PointOfSale.Infrastructure/Services/ServiceCatalogService.cs");

        Assert.Contains("var profile = service.Profile!;", controller, StringComparison.Ordinal);
        Assert.Contains("providerDisplayName = profile.DisplayName", controller, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "var profile = await _profileService.GetProfileByIdAsync(service.ProfileId);",
            controller,
            StringComparison.Ordinal);
        Assert.Contains("service.IsActive", catalog, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", catalog, StringComparison.Ordinal);
        Assert.Contains("profile.IsActive", catalog, StringComparison.Ordinal);
        Assert.Contains("profile.TenantId == service.TenantId", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs1C()
    {
        var findings = ReadRepoFile(
            "docs/architecture/service-marketplace-frontend-service-detail-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S1-C",
            "Service Identity Findings",
            "Provider Findings",
            "Pricing Findings",
            "Media Findings",
            "Rating Findings",
            "Trust Findings",
            "Analytics Findings",
            "Visibility Findings",
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

        Assert.Contains("PASS F-S1-C Service Marketplace Frontend Service Detail Presentation Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-C Service Marketplace Frontend Service Detail Presentation Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-C", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-C Service Marketplace Frontend Service Detail Presentation Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-C Service Marketplace Frontend Service Detail Presentation Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) =>
        CertificationSource.ReadRepoFile(relativePath);
}
