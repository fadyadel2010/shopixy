namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontRuntimePurchasabilityHarmonizationTests
{
    [Fact]
    public void StorefrontRuntimeDtosMustCarryCanonicalAvailabilityAcrossDiscoveryAndCards()
    {
        var discoveryDtos = CertificationSource.ReadRepoFile("PointOfSale.Core/DTOs/ProductDiscoveryDtos.cs");
        var discoveryService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs");
        var useProducts = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/hooks/useProducts.ts");
        var useProductListDiscovery = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/hooks/useProductListDiscovery.ts");
        var useHomepageProducts = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/hooks/useHomepageSectionProducts.ts");
        var productCardRuntime = CertificationSource.ReadRepoFile("PointOfSale.Storefront/src/components/products/ProductCardRuntime.tsx");

        Assert.Contains("ProductDiscoveryAvailabilityDto", discoveryDtos, StringComparison.Ordinal);
        Assert.Contains("public ProductDiscoveryAvailabilityDto Availability { get; set; } = new();", discoveryDtos, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(product)", discoveryService, StringComparison.Ordinal);
        Assert.Contains("Availability = BuildAvailabilityPresentation(purchasability)", discoveryService, StringComparison.Ordinal);
        Assert.Contains("HasVariants = hasVariants", discoveryService, StringComparison.Ordinal);
        Assert.Contains("InStock = purchasability.InStock", discoveryService, StringComparison.Ordinal);
        Assert.Contains("Price = hasVariants ? null : price", discoveryService, StringComparison.Ordinal);

        Assert.Contains("const availability = item.availability;", useProducts, StringComparison.Ordinal);
        Assert.Contains("availability,", useProductListDiscovery, StringComparison.Ordinal);
        Assert.Contains("availability,", useHomepageProducts, StringComparison.Ordinal);
        Assert.Contains("const canQuickAdd = product.availability?.canAddToCart === true;", productCardRuntime, StringComparison.Ordinal);

        Assert.DoesNotContain("item.hasVariants ? true : item.inStock", useProducts, StringComparison.Ordinal);
        Assert.DoesNotContain("item.hasVariants ? true : item.inStock", useProductListDiscovery, StringComparison.Ordinal);
        Assert.DoesNotContain("item.hasVariants ? true : item.inStock", useHomepageProducts, StringComparison.Ordinal);
        Assert.DoesNotContain("product.isAvailable && product.inStock && !product.hasVariants", productCardRuntime, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontRuntimeApisMustDelegatePurchasabilityToCanonicalAuthority()
    {
        var productService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var searchService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/ProductSearchService.cs");
        var homeService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontHomeService.cs");
        var productsController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontProductsController.cs");
        var cartService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Cart/CartService.cs");
        var wishlistService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontWishlistService.cs");

        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", productService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.EvaluateVariant(product, v)", productService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", searchService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", homeService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(doc)", productsController, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(product)", cartService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.EvaluateVariant(product, variant)", cartService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(product)", wishlistService, StringComparison.Ordinal);

        Assert.DoesNotContain("variant.OnlinePrice ?? variant.Price", cartService, StringComparison.Ordinal);
        Assert.DoesNotContain("product.StorefrontEffectivePrice", cartService, StringComparison.Ordinal);
        Assert.DoesNotContain(".Find(p => p.TenantId == tenantId && p.ProductId == productId && p.IsActive && p.IsOnlineVisible)", wishlistService, StringComparison.Ordinal);
    }
}
