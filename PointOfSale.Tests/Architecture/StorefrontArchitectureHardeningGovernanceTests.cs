namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontArchitectureHardeningGovernanceTests
{
    [Fact]
    public void Arch1PermanentContracts_MustExistAndDeclareRequiredCertification()
    {
        var contract = CertificationSource.ReadRepoFile(
            "docs/architecture/storefront-arch-1-architecture-hardening-contracts.md");

        Assert.Contains("SHOPIXY ARCH-1 Storefront Architecture Hardening Contracts", contract, StringComparison.Ordinal);
        Assert.Contains("Pricing Authority Contract", contract, StringComparison.Ordinal);
        Assert.Contains("Projection Contract", contract, StringComparison.Ordinal);
        Assert.Contains("Runtime Contract", contract, StringComparison.Ordinal);
        Assert.Contains("Boundary Contract", contract, StringComparison.Ordinal);
        Assert.Contains("Invariant Catalog", contract, StringComparison.Ordinal);
        Assert.Contains("ARCHITECTURE REQUIRES HARDENING", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontEffectivePrice_MustRemainComputedAndNonPersistent()
    {
        var productDocument = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs");
        var variantDocument = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/MongoDB/Documents/ProductVariantDocument.cs");

        Assert.Contains("[BsonIgnore]", productDocument, StringComparison.Ordinal);
        Assert.Contains("StorefrontEffectivePrice => ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice(this)", productDocument, StringComparison.Ordinal);
        Assert.Contains("StorefrontVisiblePrice => ProductStorefrontVisibilityAuthority.ResolveVisiblePrice(this)", productDocument, StringComparison.Ordinal);
        Assert.Contains("public decimal EffectivePrice => (OnlinePrice.HasValue && OnlinePrice.Value > 0) ? OnlinePrice.Value : Price;", variantDocument, StringComparison.Ordinal);

        Assert.DoesNotContain("public decimal StorefrontEffectivePrice { get; set; }", productDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("[BsonRepresentation(BsonType.Decimal128)]\r\n    public decimal StorefrontEffectivePrice", productDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("[BsonRepresentation(BsonType.Decimal128)]\n    public decimal StorefrontEffectivePrice", productDocument, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeConsumers_MustDelegatePricingAndPurchasabilityToNamedAuthorities()
    {
        var authority = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/MongoDB/Documents/ProductStorefrontVisibilityAuthority.cs");
        var productService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var searchService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/ProductSearchService.cs");
        var homeService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/StorefrontHomeService.cs");
        var cartService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Cart/CartService.cs");
        var discoveryService = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs");

        Assert.Contains("public static StorefrontPurchasabilityResult Evaluate(ProductDocument product)", authority, StringComparison.Ordinal);
        Assert.Contains("public static StorefrontPurchasabilityResult EvaluateVariant(", authority, StringComparison.Ordinal);
        Assert.Contains("product.DefaultUnit?.SalePrice ?? 0m", authority, StringComparison.Ordinal);

        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", productService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.EvaluateVariant(product, v)", productService, StringComparison.Ordinal);
        Assert.Contains("StorefrontPricingPresentationMapper.Build", productService, StringComparison.Ordinal);
        Assert.Contains("StorefrontAvailabilityPresentationMapper.Build", productService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", searchService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", homeService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(product)", cartService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.EvaluateVariant(product, variant)", cartService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(product)", discoveryService, StringComparison.Ordinal);
        Assert.Contains("BuildPricingPresentation", discoveryService, StringComparison.Ordinal);
        Assert.Contains("BuildAvailabilityPresentation", discoveryService, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeProductReads_MustReenterSqlAuthorityBeforeCustomerFacingUse()
    {
        var productService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var productsController = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/StorefrontProductsController.cs");
        var searchController = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/StorefrontSearchController.cs");
        var discoveryService = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs");
        var cartService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Cart/CartService.cs");

        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", productService, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyBatchAsync", productsController, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", searchController, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyBatchAsync", discoveryService, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", cartService, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendProductRuntime_MustRemainPresentationOnly()
    {
        var productCardRuntime = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/components/products/ProductCardRuntime.tsx");
        var runtimeSectionContainer = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx");
        var productHero = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/components/sections/defaults/product-hero.tsx");
        var useVariantSelector = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/hooks/useVariantSelector.ts");
        var useProducts = CertificationSource.ReadRepoFile(
            "PointOfSale.Storefront/src/hooks/useProducts.ts");

        Assert.Contains("product.pricing.currentPrice", productCardRuntime, StringComparison.Ordinal);
        Assert.Contains("product.availability?.canAddToCart === true", productCardRuntime, StringComparison.Ordinal);
        Assert.Contains("pricing.currentPrice", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.Contains("availability?.canAddToCart === true", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.Contains("model.canAddToCart", productHero, StringComparison.Ordinal);
        Assert.Contains("const pricing = selectedVariant?.pricing ?? product?.pricing", useVariantSelector, StringComparison.Ordinal);
        Assert.Contains("const availability = item.availability;", useProducts, StringComparison.Ordinal);

        Assert.DoesNotContain("calculateDiscountedPrice", productCardRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("calculateDiscountedPrice", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.DoesNotContain("promotionDiscountValue / 100", productCardRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("promotionDiscountValue / 100", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.DoesNotContain("selectedVariant.totalStock", runtimeSectionContainer, StringComparison.Ordinal);
        Assert.DoesNotContain("product.isAvailable && isInStock", productHero, StringComparison.Ordinal);
        Assert.DoesNotContain("item.hasVariants ? true : item.inStock", useProducts, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductUnitSqlAuthority_MustFreezeCardinalityButValueConstraintsStillRequireHardening()
    {
        var dbContext = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var productsController = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/ProductsController.cs");
        var contract = CertificationSource.ReadRepoFile(
            "docs/architecture/storefront-arch-1-architecture-hardening-contracts.md");

        Assert.Contains("UX_ProductUnits_ProductId_UnitId_Active", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_ProductUnits_ProductId_Base_Active", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_ProductUnits_ProductId_Default_Active", dbContext, StringComparison.Ordinal);
        Assert.Contains("if (dto.ConversionFactor <= 0)", productsController, StringComparison.Ordinal);
        Assert.Contains("if (dto.ConversionFactor != 1)", productsController, StringComparison.Ordinal);
        Assert.Contains("Add SQL check constraints.", contract, StringComparison.Ordinal);
    }
}
