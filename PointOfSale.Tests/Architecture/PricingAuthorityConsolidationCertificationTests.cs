using PointOfSale.Core.Entities;
using PointOfSale.Core.Pricing;

namespace PointOfSale.Tests.Architecture;

public sealed class PricingAuthorityConsolidationCertificationTests
{
    [Fact]
    public void CanonicalPricingAuthorities_AreDeclaredInSource()
    {
        var product = ReadRepoFile("PointOfSale.Core/Entities/Product.cs");
        var productUnit = ReadRepoFile("PointOfSale.Core/Entities/ProductUnit.cs");
        var productVariant = ReadRepoFile("PointOfSale.Core/Entities/ProductVariant.cs");
        var catalogAuthority = ReadRepoFile("PointOfSale.Core/Pricing/CatalogPricingAuthority.cs");
        var commerceHubAuthority = ReadRepoFile("PointOfSale.Core/Pricing/CommerceHubPriceAuthority.cs");

        Assert.Contains("Legacy compatibility mirror only", product, StringComparison.Ordinal);
        Assert.Contains("default ProductUnit is the canonical price authority for simple products", productUnit, StringComparison.Ordinal);
        Assert.Contains("ProductVariant.Price, OnlinePrice, CostPrice are canonical for variant products", productVariant, StringComparison.Ordinal);
        Assert.Contains("ResolveSimpleSellingPrice(ProductUnit? defaultUnit)", catalogAuthority, StringComparison.Ordinal);
        Assert.Contains("ResolveVariantOnlinePrice(ProductVariant variant)", catalogAuthority, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPriceSource.CommerceHubOverride", commerceHubAuthority, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductAuthoring_DoesNotWriteProductPricesFromCommandPricingAuthority()
    {
        var orchestrator = ReadRepoFile("PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs");
        var mapper = ReadRepoFile("PointOfSale.API/Services/ProductAuthoring/ProductAuthoringCommandMapper.cs");

        Assert.DoesNotContain("product.PurchasePrice = command.Inventory.HasVariants ? 0 : command.Pricing.PurchasePrice", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("product.SalePrice = command.Inventory.HasVariants ? 0 : command.Pricing.SalePrice", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("product.OnlinePrice = command.Inventory.HasVariants ? null : command.Pricing.OnlinePrice", orchestrator, StringComparison.Ordinal);
        Assert.Contains("ProductPricingCompatibilityMirror.MirrorSimpleProductFromDefaultUnit(product, defaultUnit)", orchestrator, StringComparison.Ordinal);
        Assert.Contains("ApplyLegacySimplePricingToDefaultUnit", mapper, StringComparison.Ordinal);
    }

    [Fact]
    public void MigratedBusinessReaders_DoNotReadProductPriceFallbacks()
    {
        var orders = ReadRepoFile("PointOfSale.API/Controllers/OrdersController.cs");
        var affiliateService = ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var adminAffiliate = ReadRepoFile("PointOfSale.API/Controllers/AdminProductAffiliateController.cs");
        var promotionLinks = ReadRepoFile("PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs");
        var referralMiddleware = ReadRepoFile("PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs");
        var products = ReadRepoFile("PointOfSale.API/Controllers/ProductsController.cs");

        var orderResolver = ExtractMethod(orders, "ResolveAuthoritativeUnitPrice");
        Assert.DoesNotContain("product.OnlinePrice", orderResolver, StringComparison.Ordinal);
        Assert.DoesNotContain("product.SalePrice", orderResolver, StringComparison.Ordinal);
        Assert.Contains("CatalogPricingAuthority.ResolveSimpleSellingPrice(configuredUnit)", orderResolver, StringComparison.Ordinal);
        Assert.Contains("CatalogPricingAuthority.ResolveVariantOnlinePrice(defaultVariant)", orderResolver, StringComparison.Ordinal);

        foreach (var source in new[] { affiliateService, adminAffiliate, promotionLinks, referralMiddleware })
        {
            Assert.DoesNotContain("product.OnlinePrice", source, StringComparison.Ordinal);
            Assert.DoesNotContain("product.SalePrice", source, StringComparison.Ordinal);
            Assert.Contains("unit.IsDefault", source, StringComparison.Ordinal);
        }

        Assert.Contains("SalePrice = CatalogPricingAuthority.ResolveSimpleSellingPrice(ResolveDefaultProductUnit(p))", products, StringComparison.Ordinal);
        Assert.Contains("PurchasePrice = CatalogPricingAuthority.ResolveSimplePurchasePrice(ResolveDefaultProductUnit(p))", products, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibilityMirror_IsCentralizedForLiveBusinessMirroring()
    {
        var mirror = ReadRepoFile("PointOfSale.Core/Pricing/ProductPricingCompatibilityMirror.cs");
        var productVariantService = ReadRepoFile("PointOfSale.API/Services/ProductVariantService.cs");
        var purchases = ReadRepoFile("PointOfSale.API/Controllers/PurchasesController.cs");
        var purchaseReturns = ReadRepoFile("PointOfSale.API/Controllers/PurchaseReturnsController.cs");
        var products = ReadRepoFile("PointOfSale.API/Controllers/ProductsController.cs");
        var productVariantsController = ReadRepoFile("PointOfSale.API/Controllers/ProductVariantsController.cs");
        var projectionFailureCompensation = ReadRepoFile("PointOfSale.Infrastructure/Eventing/ProjectionFailureCompensation.cs");
        var commerceHubSeed = ReadRepoFile("PointOfSale.API/Development/CommerceHub/DevelopmentCommerceHubSeedService.cs");

        Assert.Contains("The only live business component allowed to maintain legacy Product price fields", mirror, StringComparison.Ordinal);
        Assert.Contains("SeedDefaultUnitFromLegacyProductMirror", mirror, StringComparison.Ordinal);
        Assert.Contains("ProductPricingCompatibilityMirror.MirrorSimpleDefaultVariantFromDefaultUnit", productVariantService, StringComparison.Ordinal);
        Assert.Contains("ProductPricingCompatibilityMirror.MirrorSimpleProductFromDefaultUnit(product, defaultUnit)", productVariantService, StringComparison.Ordinal);
        Assert.Contains("ProductPricingCompatibilityMirror.MirrorSimpleProductFromDefaultUnit(product, defaultUnit)", purchases, StringComparison.Ordinal);
        Assert.Contains("ProductPricingCompatibilityMirror.MirrorSimpleProductFromDefaultUnit(productEntity, defaultUnit)", purchaseReturns, StringComparison.Ordinal);
        Assert.Contains("ProductPricingCompatibilityMirror.SeedDefaultUnitFromLegacyProductMirror(product, productUnit)", products, StringComparison.Ordinal);
        Assert.Contains("ProductPricingCompatibilityMirror.MirrorSimpleProductFromDefaultUnit(product, defaultUnit)", commerceHubSeed, StringComparison.Ordinal);
        Assert.DoesNotContain("product.PurchasePrice = variant.CostPrice", purchases, StringComparison.Ordinal);
        Assert.DoesNotContain("productEntity.PurchasePrice = variantEntity.CostPrice", purchaseReturns, StringComparison.Ordinal);
        Assert.DoesNotContain("PurchasePrice = product.PurchasePrice", products, StringComparison.Ordinal);
        Assert.DoesNotContain("SellingPrice = product.SalePrice", products, StringComparison.Ordinal);
        Assert.DoesNotContain("SalePrice = product.SalePrice", projectionFailureCompensation, StringComparison.Ordinal);
        Assert.DoesNotContain("OnlinePrice = product.OnlinePrice", projectionFailureCompensation, StringComparison.Ordinal);
        Assert.DoesNotContain("SalePrice = product.SalePrice", productVariantsController, StringComparison.Ordinal);
        Assert.DoesNotContain("OnlinePrice = product.OnlinePrice", productVariantsController, StringComparison.Ordinal);
        Assert.DoesNotContain("PurchasePrice = product.PurchasePrice", productVariantsController, StringComparison.Ordinal);
    }

    [Fact]
    public void SimplePricing_ResolvesFromDefaultProductUnit()
    {
        var product = new Product
        {
            HasVariants = false,
            PurchasePrice = 1m,
            SalePrice = 2m,
            OnlinePrice = 3m
        };
        var defaultUnit = new ProductUnit
        {
            IsDefault = true,
            PurchasePrice = 7m,
            SellingPrice = 11m
        };

        var changedFields = ProductPricingCompatibilityMirror.MirrorSimpleProductFromDefaultUnit(product, defaultUnit);

        Assert.Equal(7m, CatalogPricingAuthority.ResolveSimplePurchasePrice(defaultUnit));
        Assert.Equal(11m, CatalogPricingAuthority.ResolveSimpleSellingPrice(defaultUnit));
        Assert.Equal(7m, product.PurchasePrice);
        Assert.Equal(11m, product.SalePrice);
        Assert.Null(product.OnlinePrice);
        Assert.Contains(nameof(Product.PurchasePrice), changedFields);
        Assert.Contains(nameof(Product.SalePrice), changedFields);
        Assert.Contains(nameof(Product.OnlinePrice), changedFields);
    }

    [Fact]
    public void VariantAndCommerceHubPricing_RemainIsolatedAuthorities()
    {
        var tenantId = Guid.NewGuid();
        var variant = new ProductVariant
        {
            TenantId = tenantId,
            Price = 20m,
            OnlinePrice = 18m,
            CostPrice = 9m
        };
        CommerceHubPriceOverrideRuntimePolicy.Configure(new CommerceHubPriceOverrideRolloutOptions
        {
            Enabled = true,
            TenantAllowlist = [tenantId]
        });

        var variantOnlinePrice = CatalogPricingAuthority.ResolveVariantOnlinePrice(variant);
        var commerceHub = new CommerceHubPriceAuthority()
            .ResolveAsync(variant, overridePrice: 17m)
            .GetAwaiter()
            .GetResult();

        Assert.Equal(18m, variantOnlinePrice);
        Assert.Equal(9m, CatalogPricingAuthority.ResolveVariantPurchasePrice(variant));
        Assert.Equal(18m, commerceHub.BaseVariantPrice);
        Assert.Equal(17m, commerceHub.FinalPrice);
        Assert.Equal(CommerceHubPriceSource.CommerceHubOverride, commerceHub.PriceSource);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var marker = methodName + "(";
        var signatureIndex = source.IndexOf("private static decimal " + marker, StringComparison.Ordinal);
        if (signatureIndex < 0)
        {
            signatureIndex = source.IndexOf(marker, StringComparison.Ordinal);
        }

        Assert.True(signatureIndex >= 0, $"Could not find method {methodName}.");

        var bodyStart = source.IndexOf('{', signatureIndex);
        Assert.True(bodyStart >= 0, $"Could not find body for method {methodName}.");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[bodyStart..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method {methodName}.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
