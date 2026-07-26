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

        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", storefrontProductsController, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", discoveryService, StringComparison.Ordinal);
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
