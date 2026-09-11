namespace PointOfSale.Tests.Architecture;

public sealed class ElasticsearchFreshnessSearchConvergenceCertificationTests
{
    [Fact]
    public void SearchIndexInventoryAndFreshnessMarkersRemainDocumented()
    {
        var settings = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Configuration/ElasticsearchSettings.cs");
        var unifiedIndex = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/UnifiedSearchIndexService.cs");
        var productDocument = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocument.cs");
        var categoryDocument = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Documents/CategorySearchDocument.cs");
        var cmsDocument = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Documents/CmsPageSearchDocument.cs");
        var commerceHubDocument = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Documents/CommerceHubProductSearchDocument.cs");
        var affiliateDocument = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Documents/PublicAffiliateProductSearchDocument.cs");

        Assert.Contains("ProductsIndexName", settings, StringComparison.Ordinal);
        Assert.Contains("PublicAffiliateProductsIndexName", settings, StringComparison.Ordinal);
        Assert.Contains("CommerceHubProductsIndexName", settings, StringComparison.Ordinal);
        Assert.Contains("CategoriesIndexName = \"categories\"", unifiedIndex, StringComparison.Ordinal);
        Assert.Contains("CmsPagesIndexName = \"cmspages\"", unifiedIndex, StringComparison.Ordinal);
        Assert.Contains("\"marketplace_services\"", unifiedIndex, StringComparison.Ordinal);
        Assert.Contains("EnsureIndexExistsAsync<PublicAffiliateProductSearchDocument>", unifiedIndex, StringComparison.Ordinal);

        Assert.Contains("IndexedAtUtc", productDocument, StringComparison.Ordinal);
        Assert.Contains("LastEventId", productDocument, StringComparison.Ordinal);
        Assert.Contains("ProductGeneration", productDocument, StringComparison.Ordinal);
        Assert.Contains("IsTombstone", productDocument, StringComparison.Ordinal);
        Assert.Contains("IndexedAtUtc", categoryDocument, StringComparison.Ordinal);
        Assert.Contains("IndexedAtUtc", cmsDocument, StringComparison.Ordinal);
        Assert.Contains("UpdatedAtUtc", commerceHubDocument, StringComparison.Ordinal);
        Assert.Contains("AffiliateEnabledAtUtc", affiliateDocument, StringComparison.Ordinal);
        Assert.Contains("TenantId", affiliateDocument, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductCategoryCmsAndCommerceHubSearchMutationStaysSqlDerived()
    {
        var productCreated = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProductSearchIndexConsumer.cs");
        var productUpdated = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProductUpdatedSearchIndexConsumer.cs");
        var productStockPrice = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProductStockPriceSearchIndexConsumer.cs");
        var productLifecycle = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProductDeletedSearchIndexConsumer.cs");
        var productPromotion = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProductPromotionSearchIndexConsumer.cs");
        var adminSearch = ReadRepoFile("PointOfSale.API/Controllers/AdminSearchSyncController.cs");
        var commerceHubReindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");

        foreach (var source in new[] { productCreated, productUpdated, productStockPrice, productLifecycle, productPromotion })
        {
            Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", source, StringComparison.Ordinal);
            Assert.Contains("Fence", source, StringComparison.Ordinal);
        }

        Assert.Contains("ProductSearchTombstoneWriter.WriteAsync", productLifecycle, StringComparison.Ordinal);
        Assert.Contains("DeleteProductFromSearchAsync", adminSearch, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCategoriesAsync", adminSearch, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCmsPagesAsync", adminSearch, StringComparison.Ordinal);
        Assert.Contains("CategoryLineageAuthority.LoadTenantSnapshotAsync", adminSearch, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.StorefrontCms", adminSearch, StringComparison.Ordinal);

        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("ReconcileMissingSqlListingsAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("CountSqlVisibleCommerceHubListingsAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("targetCount.Value == publishedCount", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("PromoteIndexAsync", commerceHubReindex, StringComparison.Ordinal);
    }

    [Fact]
    public void SuggestionsAutocompleteFacetsAndCountsStayCandidateOnly()
    {
        var storefrontSearch = ReadRepoFile("PointOfSale.API/Controllers/StorefrontSearchController.cs");
        var productSearch = ReadRepoFile("PointOfSale.API/Services/Storefront/ProductSearchService.cs");
        var discovery = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs");
        var commerceHubQuery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");

        Assert.Contains("FilterSearchItemsBySqlAuthorityAsync", storefrontSearch, StringComparison.Ordinal);
        Assert.Contains("RecomposeHeaderSuggestionsBySqlAuthorityAsync", storefrontSearch, StringComparison.Ordinal);
        Assert.Contains("totalCount = items.Count", productSearch, StringComparison.Ordinal);
        Assert.Contains("Facets = new ProductDiscoveryFacets()", discovery, StringComparison.Ordinal);

        Assert.Contains("RecomposeVisibleSuggestionProductsAsync", commerceHubQuery, StringComparison.Ordinal);
        Assert.Contains("FilterLiveSuggestionTermsAsync", commerceHubQuery, StringComparison.Ordinal);
        Assert.Contains("SearchSqlAuthorityFirstAsync", commerceHubQuery, StringComparison.Ordinal);
        Assert.Contains("var totalCount = authorityItems.Count", commerceHubQuery, StringComparison.Ordinal);
        Assert.Contains("CategoryBuckets = authorityItems", commerceHubQuery, StringComparison.Ordinal);
        Assert.Contains("PriceStats = authorityItems.Count == 0", commerceHubQuery, StringComparison.Ordinal);
    }

    [Fact]
    public void DriftRepairHealthAndMonitoringCompareSearchAgainstSqlAuthority()
    {
        var healthCheck = ReadRepoFile("PointOfSale.API/HealthChecks/CommerceHubSearchSyncHealthCheck.cs");
        var serviceDriftJob = ReadRepoFile("PointOfSale.Infrastructure/Jobs/ElasticsearchDriftReconciliationJob.cs");
        var telemetry = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/SearchTelemetry.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("CountSqlVisibleCommerceHubListingsAsync", healthCheck, StringComparison.Ordinal);
        Assert.Contains("sqlVisibleCount", healthCheck, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", healthCheck, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", healthCheck, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceTelemetry.RecordDriftDiagnostic", healthCheck, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceAuthority.SearchIndex", healthCheck, StringComparison.Ordinal);

        Assert.Contains("SqlTruthRebuildGuard.RequireGlobalSqlAuthority", serviceDriftJob, StringComparison.Ordinal);
        Assert.Contains("LoadLiveServiceSearchAuthorityAsync", serviceDriftJob, StringComparison.Ordinal);
        Assert.Contains("DriftDetected", serviceDriftJob, StringComparison.Ordinal);
        Assert.Contains("extraInEs", serviceDriftJob, StringComparison.Ordinal);
        Assert.Contains("missingInEs", serviceDriftJob, StringComparison.Ordinal);

        Assert.Contains("PointOfSale.Elasticsearch.Search", telemetry, StringComparison.Ordinal);
        Assert.Contains("NormalizeTagValue", telemetry, StringComparison.Ordinal);
        Assert.Contains("AddMeter(PointOfSale.Infrastructure.Elasticsearch.Services.SearchTelemetry.MeterName)", program, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentationAndGovernanceAreUpdated()
    {
        var findings = ReadRepoFile("docs/architecture/elasticsearch-freshness-search-convergence-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 10.4",
            "Search Inventory Findings",
            "Search Consumer Findings",
            "Search Freshness Findings",
            "Product Search Findings",
            "Category/CMS Search Findings",
            "Suggestion/Autocomplete Findings",
            "Facet/Count Findings",
            "Reindex Findings",
            "Repair Findings",
            "Drift Detection Findings",
            "Cross-System Convergence Findings",
            "Startup/Recovery Findings",
            "Chaos Findings",
            "Runtime Hardening Performed",
            "Monitoring Improvements",
            "Governance Updates",
            "Honest Remaining Debt"
        })
        {
            Assert.Contains(marker, findings, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("PASS 10.4 Elasticsearch Freshness & Search Convergence", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 10.4 Elasticsearch Freshness / Search Convergence Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("Elasticsearch freshness and search convergence now have a documented SQL-derived certification boundary", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 10.4 Elasticsearch Freshness / Search Convergence Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
