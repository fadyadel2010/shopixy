namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendVisibilityLifecycleAuthorityCertificationTests
{
    [Fact]
    public void DiscoveryDetailAndRequestEntry_MustRequireCurrentLifecycleAuthority()
    {
        var search = ReadRepoFile(
            "PointOfSale.Infrastructure/Services/MarketplaceSearchService.cs");
        var catalog = ReadRepoFile(
            "PointOfSale.Infrastructure/Services/ServiceCatalogService.cs");
        var requests = ReadRepoFile(
            "PointOfSale.Infrastructure/Services/ServiceRequestService.cs");

        foreach (var source in new[] { search, catalog, requests })
        {
            Assert.Contains("TenantStatus.Active", source, StringComparison.Ordinal);
            Assert.Contains("UserGovernanceRole.Vendor", source, StringComparison.Ordinal);
            Assert.Contains("UserGovernanceStatus.Suspended", source, StringComparison.Ordinal);
            Assert.Contains("UserGovernanceStatus.Banned", source, StringComparison.Ordinal);
        }

        Assert.Contains("!service.IsDeleted", search, StringComparison.Ordinal);
        Assert.Contains("service.IsActive", search, StringComparison.Ordinal);
        Assert.Contains("!profile.IsDeleted", search, StringComparison.Ordinal);
        Assert.Contains("profile.IsActive", search, StringComparison.Ordinal);
        Assert.Contains("!service.IsDeleted", catalog, StringComparison.Ordinal);
        Assert.Contains("service.IsActive", catalog, StringComparison.Ordinal);
        Assert.Contains("!profile.IsDeleted", catalog, StringComparison.Ordinal);
        Assert.Contains("profile.IsActive", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionRepairAndReindex_MustNotResurrectLifecycleInvalidServices()
    {
        var projection = ReadRepoFile(
            "PointOfSale.Infrastructure/Consumers/ServiceDiscoveryProjectionConsumer.cs");
        var drift = ReadRepoFile(
            "PointOfSale.Infrastructure/Jobs/ElasticsearchDriftReconciliationJob.cs");
        var controller = ReadRepoFile(
            "PointOfSale.API/Controllers/MarketplaceController.cs");

        foreach (var source in new[] { projection, drift, controller })
        {
            Assert.Contains("TenantStatus.Active", source, StringComparison.Ordinal);
            Assert.Contains("UserGovernanceRole.Vendor", source, StringComparison.Ordinal);
            Assert.Contains("UserGovernanceStatus.Suspended", source, StringComparison.Ordinal);
            Assert.Contains("UserGovernanceStatus.Banned", source, StringComparison.Ordinal);
        }

        Assert.Contains("DeleteServiceDocumentAsync", projection, StringComparison.Ordinal);
        Assert.Contains("current SQL service/tenant/profile authority missing", projection, StringComparison.Ordinal);
        Assert.Contains("extraInEs", drift, StringComparison.Ordinal);
        Assert.Contains("var staleServiceIds = existingTenantServiceIds", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendCacheHistoryAndRealtime_MustRemainDerivative()
    {
        var boundary = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");
        var discover = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/tabs/DiscoverTab.tsx");
        var detail = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx");
        var activate = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/hooks/useActivateService.ts");
        var deactivate = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/hooks/useDeactivateService.ts");

        Assert.Contains("AUTHORITY_REVALIDATION_INTERVAL_MS", boundary, StringComparison.Ordinal);
        Assert.Contains("isMarketplaceAuthorityInvalidation(event.data, scope)", boundary, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", boundary, StringComparison.Ordinal);
        Assert.Contains("authorityFailed || isValidating ? undefined", discover, StringComparison.Ordinal);
        Assert.Contains("if (serviceAuthorityValidating)", detail, StringComparison.Ordinal);

        foreach (var mutation in new[] { activate, deactivate })
        {
            Assert.Contains("publishMarketplaceAuthorityInvalidation(authorityScope, serviceId)", mutation, StringComparison.Ordinal);
            Assert.Contains("marketplaceKeys.scope(authorityScope)", mutation, StringComparison.Ordinal);
            Assert.DoesNotContain("setQueryData", mutation, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs1F()
    {
        var findings = ReadRepoFile(
            "docs/architecture/service-marketplace-frontend-visibility-lifecycle-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S1-F",
            "Listing Visibility Findings",
            "Provider Lifecycle Findings",
            "Profile Lifecycle Findings",
            "Tenant Lifecycle Findings",
            "Discovery Lifecycle Findings",
            "Card Lifecycle Findings",
            "Detail Lifecycle Findings",
            "Request-Entry Lifecycle Findings",
            "Cache Findings",
            "Realtime Findings",
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

        Assert.Contains("PASS F-S1-F", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-F", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-F", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-F", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-F", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) =>
        CertificationSource.ReadRepoFile(relativePath);
}
