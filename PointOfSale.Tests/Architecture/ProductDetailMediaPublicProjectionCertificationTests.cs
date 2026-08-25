namespace PointOfSale.Tests.Architecture;

public sealed class ProductDetailMediaPublicProjectionCertificationTests
{
    [Fact]
    public void StorefrontProductDetailReadsStaySqlAuthorityBound()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/StorefrontProductsController.cs");

        Assert.Contains("ApplyCurrentSqlProductDetailAuthorityAsync", service, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", service, StringComparison.Ordinal);
        Assert.Contains("IsPubliclyVisibleProduct(sqlProjection)", service, StringComparison.Ordinal);
        Assert.Contains("RecomposeCurrentSqlProductListAuthorityAsync", service, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", controller, StringComparison.Ordinal);
        Assert.Contains("sqlProjection is { IsActive: true, IsOnlineVisible: true }", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("MongoDB is the read-model source for storefront reads", service, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontProductSearchAndDiscoveryHydrationStaySqlAuthorityBound()
    {
        var search = ReadRepoFile("PointOfSale.API/Services/Storefront/ProductSearchService.cs");
        var discovery = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs");

        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", search, StringComparison.Ordinal);
        Assert.Contains("sqlProjection is { IsActive: true, IsOnlineVisible: true }", search, StringComparison.Ordinal);
        Assert.Contains("RecomposeDiscoveryItemsFromSqlAuthorityAsync", discovery, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", discovery, StringComparison.Ordinal);
        Assert.Contains("product is not { IsActive: true, IsOnlineVisible: true }", discovery, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductSeoSitemapAndSlugResolutionStaySqlAuthorityBound()
    {
        var seo = ReadRepoFile("PointOfSale.API/Services/Storefront/SeoService.cs");

        Assert.Contains("LoadCurrentVisibleProductBySlugAsync", seo, StringComparison.Ordinal);
        Assert.Contains("ApplyCurrentSqlProductAuthorityAsync", seo, StringComparison.Ordinal);
        Assert.Contains("IsCurrentProductSlugAsync", seo, StringComparison.Ordinal);
        Assert.Contains("Cached product slug resolution rejected by current SQL product authority", seo, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", seo, StringComparison.Ordinal);
        Assert.Contains("product is not { IsActive: true, IsOnlineVisible: true }", seo, StringComparison.Ordinal);
        Assert.DoesNotContain("var product = await _productRepository.FindBySlugAsync(tenantId, slug, true, cancellationToken);", seo, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductMediaProjectionKeepsSqlMediaOwnershipAndFreshness()
    {
        var mediaConsumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProductMediaChangedEventConsumer.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs");

        Assert.Contains("LoadSqlMediaAuthorityAsync", mediaConsumer, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", mediaConsumer, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator.ValidateSqlVersion", mediaConsumer, StringComparison.Ordinal);
        Assert.Contains("item.TenantId == tenantId", mediaConsumer, StringComparison.Ordinal);
        Assert.Contains("item.ProductId == productId", mediaConsumer, StringComparison.Ordinal);
        Assert.Contains("EF.Functions.ILike(item.MediaType, \"image\")", mediaConsumer, StringComparison.Ordinal);
        Assert.Contains("EF.Functions.ILike(item.MediaType, \"video\")", mediaConsumer, StringComparison.Ordinal);
        Assert.Contains("OrderBy(item => item.Position)", mediaConsumer, StringComparison.Ordinal);
        Assert.Contains("product.Media = sqlProduct.Media", overlay, StringComparison.Ordinal);
        Assert.Contains("IsStorefrontProductMediaType(media.MediaType)", overlay, StringComparison.Ordinal);
        Assert.Contains("variant.MediaId", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("variant.ImageUrl,", overlay, StringComparison.Ordinal);
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
