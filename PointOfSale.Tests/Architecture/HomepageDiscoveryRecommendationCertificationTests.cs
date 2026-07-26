namespace PointOfSale.Tests.Architecture;

public sealed class HomepageDiscoveryRecommendationCertificationTests
{
    [Fact]
    public void StorefrontHomepageShelvesStaySqlAuthorityBound()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontHomeService.cs");

        Assert.Contains("RecomposeProductCardsFromSqlAuthorityAsync", service, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", service, StringComparison.Ordinal);
        Assert.Contains("CategoryLineageAuthority.LoadTenantSnapshotAsync", service, StringComparison.Ordinal);
        Assert.Contains("AffiliateProductPolicies", service, StringComparison.Ordinal);
        Assert.Contains("ProductVariantInventoryLineageAuthority.FilterCurrentInventoryStockOwnersAsync", service, StringComparison.Ordinal);
        Assert.Contains("IsProductHomepageVisible", service, StringComparison.Ordinal);
        Assert.Contains("product.PublishStatus == PublishStatus.Published", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubHomepageProjectionAndReadsStaySqlAuthorityBound()
    {
        var projection = ReadRepoFile("PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs");
        var consumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/Marketplace/HomepageProjectionConsumer.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceReadController.cs");

        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", projection, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Homepage", projection, StringComparison.Ordinal);
        Assert.Contains("LoadLiveListingIdsAsync", projection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", projection, StringComparison.Ordinal);
        Assert.Contains("listing.IsPublished", projection, StringComparison.Ordinal);
        Assert.Contains("product.PublishStatus == PublishStatus.Published", projection, StringComparison.Ordinal);
        Assert.Contains("FilterPublicReadAsync", controller, StringComparison.Ordinal);
        Assert.Contains("FilterHomepageSectionsByVendorVisibilityAsync", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateDiscoveryReadsTreatSearchAsCandidateOnly()
    {
        var discovery = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/AffiliateProductDiscoveryService.cs");
        var publicController = ReadRepoFile("PointOfSale.API/Controllers/PublicAffiliateProductsController.cs");
        var registration = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/ElasticsearchServiceCollectionExtensions.cs");

        Assert.Contains("RecomposeDiscoveryItemsFromSqlAuthorityAsync", discovery, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", discovery, StringComparison.Ordinal);
        Assert.Contains("AffiliateProductPolicies", discovery, StringComparison.Ordinal);
        Assert.Contains("product.TryResolveAffiliatePublicPricing(out var pricing)", discovery, StringComparison.Ordinal);
        Assert.Contains("Total = items.Count", discovery, StringComparison.Ordinal);
        Assert.Contains("RecomposePublicAffiliateProductsFromSqlAuthorityAsync", publicController, StringComparison.Ordinal);
        Assert.Contains("RecomposePublicAffiliateProductFromSqlAuthorityAsync", publicController, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", publicController, StringComparison.Ordinal);
        Assert.Contains("total = docs.Count", publicController, StringComparison.Ordinal);
        Assert.Contains("services.AddScoped<IAffiliateProductDiscoveryService, AffiliateProductDiscoveryService>()", registration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductCacheInvalidationRetainsHomepageReplayCollapse()
    {
        var productInvalidation = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProductCacheInvalidationConsumer.cs");
        var redisInvalidation = ReadRepoFile("PointOfSale.API/Consumers/RedisInvalidationConsumer.cs");

        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", productInvalidation, StringComparison.Ordinal);
        Assert.Contains("invalidateHomepage", productInvalidation, StringComparison.Ordinal);
        Assert.Contains("CacheKeyBuilder.HomePatternKey", productInvalidation, StringComparison.Ordinal);
        Assert.Contains("RemoveByPatternAsync", productInvalidation, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", redisInvalidation, StringComparison.Ordinal);
        Assert.Contains("homepage", redisInvalidation, StringComparison.OrdinalIgnoreCase);
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
