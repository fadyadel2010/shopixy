namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontProductBoundaryCertificationTests
{
    [Fact]
    public void StorefrontProductReads_MustNotDependOnCommerceHubProjectionReadiness()
    {
        var storefrontProductsController = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/StorefrontProductsController.cs");

        Assert.DoesNotContain("ICommerceHubProjectionReadinessService", storefrontProductsController, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsurePublicProductReadModelsReadyAsync", storefrontProductsController, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectionReadModelUnavailableException", storefrontProductsController, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontProductReads_MustRecomposeCandidatesThroughSqlProductAuthority()
    {
        var storefrontProductService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var storefrontProductsController = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/StorefrontProductsController.cs");
        var discoveryService = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs");

        // List paths must recompose through set-based SQL authority (batch);
        // single-product detail paths retain ApplyAsync. Both route through
        // ProductSearchSqlAuthorityOverlay so tenant/visibility semantics hold.
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyBatchAsync", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyBatchAsync", storefrontProductsController, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyBatchAsync", discoveryService, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontProductList_CacheMissMustNotRepeatSqlRecomposition()
    {
        var service = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/StorefrontProductService.cs");

        Assert.Contains("cacheFactoryExecuted = true", service, StringComparison.Ordinal);
        Assert.Contains("if (cacheFactoryExecuted)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceReads_MayUseCommerceHubProjectionReadinessForMarketplaceSurface()
    {
        var marketplaceReadController = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/MarketplaceReadController.cs");

        Assert.Contains("ICommerceHubProjectionReadinessService", marketplaceReadController, StringComparison.Ordinal);
        Assert.Contains("EnsurePublicProductReadModelsReadyAsync", marketplaceReadController, StringComparison.Ordinal);
    }
}
