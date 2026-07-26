namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubDiscoveryAuthorityCertificationTests
{
    [Fact]
    public void CategoriesAndFilters_MustStaySqlLineageAndSchemaBound()
    {
        var categoriesController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubCategoriesController.cs");
        var queryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var parser = ReadRepoFile("PointOfSale.API/CommerceHub/Services/CommerceHubSearchQueryParser.cs");

        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", categoriesController, StringComparison.Ordinal);
        Assert.Contains("liveCategoryIds.Contains(c.Id)", categoriesController, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.IsLiveAsync", categoriesController, StringComparison.Ordinal);
        Assert.Contains("LoadFacetDefinitionsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("HasUnsupportedAttributeFilters", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHub search rejected unsupported attribute filters", queryService, StringComparison.Ordinal);
        Assert.Contains("MapSqlVisibleFacetsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("FilterAutocompleteCategoryDtosAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubAttributeService.NormalizeKey", parser, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailDeepLinks_MustFailClosedBeforeReturningSuppressedOrDeletedListings()
    {
        var queryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var detailsPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/ProductDetailsPage.tsx");

        Assert.Contains("GetProductByIdAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("GetPublishedProductByIdAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("IsVendorSuppressedAsync(listing.TenantId", queryService, StringComparison.Ordinal);
        Assert.Contains("EnsureTenantOwnedEntity(listing.TenantId", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("return null;", queryService, StringComparison.Ordinal);
        Assert.Contains("Current SQL listing/product/variant authority does not permit CommerceHub search visibility.", overlay, StringComparison.Ordinal);
        Assert.Contains("PackageX", detailsPage, StringComparison.Ordinal);
        Assert.Contains("InlineErrorState", detailsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryPricingAndSuppression_MustBeCustomerVisibleAndAuthorityDerived()
    {
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var productCard = ReadRepoFile("PointOfSale.CommerceHub/src/commerce/components/ProductCard.tsx");
        var variantSelector = ReadRepoFile("PointOfSale.CommerceHub/src/features/product-details/components/VariantSelector.tsx");
        var stickyAddToCart = ReadRepoFile("PointOfSale.CommerceHub/src/features/product-details/components/StickyAddToCartBar.tsx");

        Assert.Contains("document.Price = priceResolution.FinalPrice", overlay, StringComparison.Ordinal);
        Assert.Contains("document.PriceSource = priceResolution.PriceSource.ToString()", overlay, StringComparison.Ordinal);
        Assert.Contains("document.InStock", overlay, StringComparison.Ordinal);
        Assert.Contains("document.AvailableStock", overlay, StringComparison.Ordinal);
        Assert.Contains("product.inStock ? \"In stock\" : \"Out of stock\"", productCard, StringComparison.Ordinal);
        Assert.Contains("formatCurrency(product.price, product.currency)", productCard, StringComparison.Ordinal);
        Assert.Contains("inStock: boolean", variantSelector, StringComparison.Ordinal);
        Assert.Contains("getOptionState", variantSelector, StringComparison.Ordinal);
        Assert.Contains("maxQuantity", stickyAddToCart, StringComparison.Ordinal);
        Assert.Contains("disabled", stickyAddToCart, StringComparison.Ordinal);
        Assert.Contains("lowStockLabel", stickyAddToCart, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditMonitoringAndChaosControls_MustRemainPresentForDiscoveryIncidents()
    {
        var queryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var analyticsController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubController.cs");
        var analyticsBackgroundService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubSearchAnalyticsBackgroundService.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var health = ReadRepoFile("PointOfSale.API/HealthChecks/CommerceHubSearchSyncHealthCheck.cs");

        Assert.Contains("TrackQuery", queryService, StringComparison.Ordinal);
        Assert.Contains("TriggerAutoHealing", queryService, StringComparison.Ordinal);
        Assert.Contains("GetVisibilityVersionTokenAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("TrackImpression", analyticsController, StringComparison.Ordinal);
        Assert.Contains("TrackClick", analyticsController, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchImpressions.AddRangeAsync", analyticsBackgroundService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchClicks.AddRangeAsync", analyticsBackgroundService, StringComparison.Ordinal);
        Assert.Contains("StaleDeleted", reindex, StringComparison.Ordinal);
        Assert.Contains("SearchSyncStatus", reindex, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSyncHealthCheck", health, StringComparison.Ordinal);
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
