namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontArchitectureMutationTests
{
    [Fact]
    public void Mutation_PersistStorefrontEffectivePrice_MustBeKilled()
    {
        var productDocument = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs");
        var admission = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionAdmissionPolicy.cs");

        Assert.Contains("[BsonIgnore]", productDocument, StringComparison.Ordinal);
        Assert.Contains("public decimal StorefrontEffectivePrice =>", productDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("public decimal StorefrontEffectivePrice { get; set; }", productDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("BsonElement(\"StorefrontEffectivePrice\")", productDocument, StringComparison.Ordinal);
        Assert.Contains("STOREFRONT_EFFECTIVE_PRICE_MUST_NOT_BE_PERSISTABLE", admission, StringComparison.Ordinal);
        Assert.Contains("STOREFRONT_EFFECTIVE_PRICE_MUST_BE_BSON_IGNORED", admission, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutation_DuplicateAvailabilityLogic_MustBeKilled()
    {
        var authority = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/MongoDB/Documents/ProductStorefrontVisibilityAuthority.cs");
        var productService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var homeService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/StorefrontHomeService.cs");
        var searchService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/ProductSearchService.cs");

        Assert.Contains("public static StorefrontPurchasabilityResult Evaluate(ProductDocument product)", authority, StringComparison.Ordinal);
        Assert.Contains("public static StorefrontPurchasabilityResult EvaluateVariant(", authority, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", productService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", homeService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", searchService, StringComparison.Ordinal);

        Assert.DoesNotContain("document.IsActive && document.IsOnlineVisible && document.InStock", productService, StringComparison.Ordinal);
        Assert.DoesNotContain("document.IsActive && document.IsOnlineVisible && document.InStock", homeService, StringComparison.Ordinal);
        Assert.DoesNotContain("document.IsActive && document.IsOnlineVisible && document.InStock", searchService, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutation_FrontendDerivesAvailability_MustBeKilled()
    {
        var productCardRuntime = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/components/products/ProductCardRuntime.tsx");
        var useProductListDiscovery = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/hooks/useProductListDiscovery.ts");
        var useHomepageProducts = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/hooks/useHomepageSectionProducts.ts");

        Assert.Contains("product.availability?.canAddToCart === true", productCardRuntime, StringComparison.Ordinal);
        Assert.Contains("availability,", useProductListDiscovery, StringComparison.Ordinal);
        Assert.Contains("availability,", useHomepageProducts, StringComparison.Ordinal);

        Assert.DoesNotContain("product.inStock &&", productCardRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("product.totalStock > 0", productCardRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("item.hasVariants ? true : item.inStock", useProductListDiscovery, StringComparison.Ordinal);
        Assert.DoesNotContain("item.hasVariants ? true : item.inStock", useHomepageProducts, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutation_BypassProductStorefrontVisibilityAuthority_MustBeKilled()
    {
        var cartService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Cart/CartService.cs");
        var wishlistService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/StorefrontWishlistService.cs");
        var discoveryService = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs");

        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(product)", cartService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.EvaluateVariant(product, variant)", cartService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(product)", wishlistService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(product)", discoveryService, StringComparison.Ordinal);

        Assert.DoesNotContain("product.StorefrontEffectivePrice", cartService, StringComparison.Ordinal);
        Assert.DoesNotContain("variant.OnlinePrice ?? variant.Price", cartService, StringComparison.Ordinal);
        Assert.DoesNotContain(".Find(p => p.TenantId == tenantId && p.ProductId == productId && p.IsActive && p.IsOnlineVisible)", wishlistService, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutation_ProjectionBypassesSqlOverlay_MustBeKilled()
    {
        var productCreated = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Consumers/ProductCreatedEventConsumer.cs");
        var productUpdated = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Consumers/ProductUpdatedEventConsumer.cs");
        var lifecycle = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Consumers/ProductVisibilityLifecycleProjectionConsumer.cs");
        var writer = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs");
        var boundary = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs");

        Assert.Contains("IProductSqlAuthorityProjectionWriter", productCreated, StringComparison.Ordinal);
        Assert.Contains("IProductSqlAuthorityProjectionWriter", productUpdated, StringComparison.Ordinal);
        Assert.Contains("IProductSqlAuthorityProjectionWriter", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", writer, StringComparison.Ordinal);
        Assert.Contains("_admissionPolicy.Validate(mutation)", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("_repository.UpsertAsync", productCreated, StringComparison.Ordinal);
        Assert.DoesNotContain("_repository.UpsertAsync", productUpdated, StringComparison.Ordinal);
        Assert.DoesNotContain("_repository.UpsertAsync", lifecycle, StringComparison.Ordinal);
    }
}
