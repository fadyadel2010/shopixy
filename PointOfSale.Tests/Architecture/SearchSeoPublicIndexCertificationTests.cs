namespace PointOfSale.Tests.Architecture;

public sealed class SearchSeoPublicIndexCertificationTests
{
    [Fact]
    public void StorefrontUnifiedAndHeaderSearchStaySqlAuthorityBound()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/StorefrontSearchController.cs");
        var productSearch = ReadRepoFile("PointOfSale.API/Services/Storefront/ProductSearchService.cs");
        var discovery = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs");

        Assert.Contains("FilterSearchItemsBySqlAuthorityAsync", controller, StringComparison.Ordinal);
        Assert.Contains("RecomposeHeaderSuggestionsBySqlAuthorityAsync", controller, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", controller, StringComparison.Ordinal);
        Assert.Contains("CategoryLineageAuthority.RequireLiveCategoryAsync", controller, StringComparison.Ordinal);
        Assert.Contains("TotalCount = totalCount", controller, StringComparison.Ordinal);
        Assert.Contains("totalCount = items.Count", productSearch, StringComparison.Ordinal);
        Assert.Contains("RecomposeDiscoveryItemsFromSqlAuthorityAsync", discovery, StringComparison.Ordinal);
        Assert.Contains("Facets = new ProductDiscoveryFacets()", discovery, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubSearchCountsFacetsAndSuggestionsStaySqlAuthorityBound()
    {
        var queryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var reindexService = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");

        Assert.Contains("SearchSqlAuthorityFirstAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("Total = result.TotalCount", queryService, StringComparison.Ordinal);
        Assert.Contains("TotalCount = result.TotalCount", queryService, StringComparison.Ordinal);
        Assert.Contains("TotalCount = totalCount", queryService, StringComparison.Ordinal);
        Assert.Contains("MapSqlVisibleFacetsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("Aggregations = new Dictionary<string, CommerceHubAggregationDto>", queryService, StringComparison.Ordinal);
        Assert.Contains("FilterLiveSuggestionTermsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", reindexService, StringComparison.Ordinal);
        Assert.Contains("CountSqlVisibleCommerceHubListingsAsync", reindexService, StringComparison.Ordinal);
        Assert.Contains("targetCount.Value == publishedCount", reindexService, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyAffiliatePortalDiscoveryStaysSqlAuthorityBound()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AffiliatePortalController.cs");

        Assert.Contains("RecomposeAffiliateIntelligenceDocumentsBySqlAuthorityAsync", controller, StringComparison.Ordinal);
        Assert.Contains("RecomposeAffiliateIntelligenceDocumentBySqlAuthorityAsync", controller, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", controller, StringComparison.Ordinal);
        Assert.Contains("AffiliateProductPolicies", controller, StringComparison.Ordinal);
        Assert.Contains("product.TryResolveAffiliatePublicPricing(out var pricing)", controller, StringComparison.Ordinal);
        Assert.Contains("LoadSqlAdmittedAffiliateIntelligenceDocumentsAsync", controller, StringComparison.Ordinal);
        Assert.Contains("var totalCount = filteredDocs.Count", controller, StringComparison.Ordinal);
        Assert.Contains("OrderAffiliateDocuments(filteredDocs, sort)", controller, StringComparison.Ordinal);
        Assert.Contains(".Skip(PaginationGuard.GetSkip(page, pageSize))", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("var candidateCount = await _mongoDbContext.AffiliateProductIntelligence", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("TotalCount = (int)Math.Min(candidateCount, int.MaxValue)", controller, StringComparison.Ordinal);
        Assert.Contains("doc = doc == null ? null : await RecomposeAffiliateIntelligenceDocumentBySqlAuthorityAsync", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void SeoSitemapAndStructuredDataStaySqlAuthorityBound()
    {
        var seo = ReadRepoFile("PointOfSale.API/Services/Storefront/SeoService.cs");

        Assert.Contains("LoadCurrentVisibleProductBySlugAsync", seo, StringComparison.Ordinal);
        Assert.Contains("ApplyCurrentSqlProductAuthorityAsync", seo, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentCategoryBySlugAsync", seo, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentPublishedCmsPagesAsync", seo, StringComparison.Ordinal);
        Assert.Contains("tenant.IsActive", seo, StringComparison.Ordinal);
        Assert.Contains("IsCurrentProductSlugAsync", seo, StringComparison.Ordinal);
        Assert.Contains("IsCurrentCategorySlugAsync", seo, StringComparison.Ordinal);
        Assert.Contains("IsCurrentPublishedCmsPageAsync", seo, StringComparison.Ordinal);
        Assert.Contains("BuildBreadcrumbJsonLd", seo, StringComparison.Ordinal);
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
