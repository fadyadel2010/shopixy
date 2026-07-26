namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendDiscoveryAuthorityCertificationTests
{
    [Fact]
    public void DiscoverySearch_MustRecomposeCurrentAuthorityBeforeCountingAndPagination()
    {
        var searchService = ReadRepoFile(
            "PointOfSale.Infrastructure/Services/MarketplaceSearchService.cs");

        Assert.Contains(".Scroll(ScrollTimeout)", searchService, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentSqlListingAuthorityAsync", searchService, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentRequestStatsAsync", searchService, StringComparison.Ordinal);
        Assert.Contains("_providerTrustService.GetTrustAggregateAsync", searchService, StringComparison.Ordinal);
        Assert.Contains("MatchesCurrentFilters", searchService, StringComparison.Ordinal);
        Assert.Contains("MatchesCurrentTrustFilters", searchService, StringComparison.Ordinal);
        Assert.Contains("ApplyCurrentAuthoritySort", searchService, StringComparison.Ordinal);
        Assert.Contains("var totalCount = orderedItems.Count", searchService, StringComparison.Ordinal);
        Assert.Contains(".Skip((request.Page - 1) * request.PageSize)", searchService, StringComparison.Ordinal);
        Assert.Contains(".Take(request.PageSize)", searchService, StringComparison.Ordinal);
        Assert.DoesNotContain("TotalCount = items.Count", searchService, StringComparison.Ordinal);
        Assert.DoesNotContain(".FieldValueFactor", searchService, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryQueryState_MustBeValidatedReplayableAndPreserveRouteState()
    {
        var discover = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/tabs/DiscoverTab.tsx");
        var shell = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/MarketplaceShell.tsx");
        var request = ReadRepoFile(
            "PointOfSale.Core/DTOs/Marketplace/MarketplaceSearchRequest.cs");

        foreach (var marker in new[]
        {
            "urlParams.get('q')",
            "urlParams.get('category')",
            "urlParams.get('sort')",
            "urlParams.get('page')",
            "normalizePage",
            "MAX_SEARCH_LENGTH",
            "isSyncingFromUrl",
            "new URLSearchParams(current)",
            "next.delete('page')",
            "marketplaceKeys.search(request, authorityScope)"
        })
        {
            Assert.Contains(marker, discover, StringComparison.Ordinal);
        }

        Assert.Contains("new URLSearchParams(current)", shell, StringComparison.Ordinal);
        Assert.Contains("next.set('tab', tab)", shell, StringComparison.Ordinal);
        Assert.Contains("Range(1, 100000", request, StringComparison.Ordinal);
        Assert.Contains("relevance|rating|newest|price_low|price_high", request, StringComparison.Ordinal);
        Assert.Contains("New|Rising|Trusted|TopRated|Standard", request, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryCacheAndFailures_MustFailClosedInsteadOfManufacturingResults()
    {
        var discover = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/pages/tabs/DiscoverTab.tsx");
        var api = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/api/marketplaceApi.ts");
        var controller = ReadRepoFile(
            "PointOfSale.API/Controllers/MarketplaceController.cs");
        var authority = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/authority.ts");

        Assert.Contains("isFetching", discover, StringComparison.Ordinal);
        Assert.Contains("isRefetchError", discover, StringComparison.Ordinal);
        Assert.Contains("const data = authorityFailed || isValidating ? undefined", discover, StringComparison.Ordinal);
        Assert.Contains("void refetch()", discover, StringComparison.Ordinal);
        Assert.DoesNotContain("Soft failure - return empty results", api, StringComparison.Ordinal);
        Assert.DoesNotContain("apiClient.post('/marketplace/search', request).catch", api, StringComparison.Ordinal);
        Assert.Contains("Status503ServiceUnavailable", controller, StringComparison.Ordinal);
        Assert.Contains("staleTime: 0", authority, StringComparison.Ordinal);
        Assert.Contains("refetchOnReconnect: 'always'", authority, StringComparison.Ordinal);
        Assert.Contains("refetchOnWindowFocus: 'always'", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryProjectionAndRealtime_MustRemainDerivativeTriggers()
    {
        var consumer = ReadRepoFile(
            "PointOfSale.Infrastructure/Consumers/ServiceDiscoveryProjectionConsumer.cs");
        var boundary = ReadRepoFile(
            "PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");

        Assert.Contains("LoadLiveServiceDocumentAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("DeleteServiceDocumentAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("current SQL service/tenant/profile authority missing", consumer, StringComparison.Ordinal);
        Assert.Contains("AUTHORITY_REVALIDATION_INTERVAL_MS", boundary, StringComparison.Ordinal);
        Assert.Contains("invalidateQueries", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs1A()
    {
        var findings = ReadRepoFile(
            "docs/architecture/service-marketplace-frontend-discovery-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S1-A",
            "Search Findings",
            "Filter Findings",
            "Sorting Findings",
            "Pagination Findings",
            "Discovery Result Findings",
            "Cache Findings",
            "Realtime Findings",
            "Tenant Isolation Findings",
            "History Findings",
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

        Assert.Contains("PASS F-S1-A Service Marketplace Frontend Discovery Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-A Service Marketplace Frontend Discovery Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-A", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-A Service Marketplace Frontend Discovery Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S1-A Service Marketplace Frontend Discovery Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) =>
        CertificationSource.ReadRepoFile(relativePath);
}
