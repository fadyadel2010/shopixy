namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubSearchVisibilityCertificationTests
{
    [Fact]
    public void SearchResultsSuggestionsAutocompleteAndPopularTerms_MustStaySqlAuthorityBound()
    {
        var queryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");

        Assert.Contains("SearchSqlAuthorityFirstAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("var totalCount = authorityItems.Count", queryService, StringComparison.Ordinal);
        Assert.Contains("OrderAuthorityItems(authorityItems, request)", queryService, StringComparison.Ordinal);
        Assert.Contains(".Skip(PaginationGuard.GetSkip(page, pageSize))", queryService, StringComparison.Ordinal);
        Assert.Contains("TotalCount = totalCount", queryService, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleSuggestionProductsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleAutocompleteProductsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("FilterLiveSuggestionTermsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("RevalidateSuggestionsResponseAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("RevalidateAutocompleteResponseAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("LoadPubliclySuppressedVendorIdsAsync", queryService, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchOverlay_MustRejectInvisibleListingProductVariantCategoryTenantAndVendorAuthority()
    {
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");

        Assert.Contains("SqlAuthorityOverlayKind.Variant", overlay, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Pricing", overlay, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Stock", overlay, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Media", overlay, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Category", overlay, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.CommerceHubListing", overlay, StringComparison.Ordinal);
        Assert.Contains("listing == null || !listing.IsPublished", overlay, StringComparison.Ordinal);
        Assert.Contains("tenant == null", overlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorAuthority.LoadAsync", overlay, StringComparison.Ordinal);
        Assert.Contains("vendorAuthority.IsMarketplaceEligible", overlay, StringComparison.Ordinal);
        Assert.Contains("product == null", overlay, StringComparison.Ordinal);
        Assert.Contains("variant == null", overlay, StringComparison.Ordinal);
        Assert.Contains("category == null", overlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceAuthority", overlay, StringComparison.Ordinal);
        Assert.Contains("WhereCurrentInventoryStockOwner", overlay, StringComparison.Ordinal);
        Assert.Contains("NormalizeAttributesAgainstCurrentSchemaAsync", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void ReindexAndHealth_MustDeleteStaleSearchDocumentsAndCountSqlVisibleListings()
    {
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var health = ReadRepoFile("PointOfSale.API/HealthChecks/CommerceHubSearchSyncHealthCheck.cs");

        Assert.Contains("ReconcileMissingSqlListingsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleSearchDocumentsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("CountSqlVisibleCommerceHubListingsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("LoadSqlVisibleListingIdentitiesAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("targetCount.Value == publishedCount", reindex, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", health, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendSearchUx_MustKeepFiltersSortingAutocompleteAndTelemetryAsApiConsumers()
    {
        var productsPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/ProductsPage.tsx");
        var autocomplete = ReadRepoFile("PointOfSale.CommerceHub/src/search/components/SearchAutocomplete.tsx");
        var discoveryQueries = ReadRepoFile("PointOfSale.CommerceHub/src/features/discovery/queries.ts");

        Assert.Contains("infiniteSearchResultsQueryOptions", productsPage, StringComparison.Ordinal);
        Assert.Contains("ProductSortSelect", productsPage, StringComparison.Ordinal);
        Assert.Contains("FiltersSidebar", productsPage, StringComparison.Ordinal);
        Assert.Contains("MobileFiltersDrawer", productsPage, StringComparison.Ordinal);
        Assert.Contains("FilterChips", productsPage, StringComparison.Ordinal);
        Assert.Contains("zeroResultSuggestions", productsPage, StringComparison.Ordinal);
        Assert.Contains("useAutocomplete", autocomplete, StringComparison.Ordinal);
        Assert.Contains("commerceHubService.trackSearchImpression", autocomplete, StringComparison.Ordinal);
        Assert.Contains("categoryTreeQueryOptions", discoveryQueries, StringComparison.Ordinal);
        Assert.Contains("commerceHubService.search(query, signal)", discoveryQueries, StringComparison.Ordinal);
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
