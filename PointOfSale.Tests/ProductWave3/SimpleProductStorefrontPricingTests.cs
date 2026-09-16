using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Pricing;
using PointOfSale.Infrastructure.MongoDB.Documents;
using Xunit;

namespace PointOfSale.Tests.ProductWave3;

/// <summary>
/// WAVE 3 (Part A) — simple-product storefront pricing.
///
/// Contract under test:
/// - POS authority stays the default-unit selling price (mirrored to the
///   default variant's Price/CostPrice).
/// - Storefront authority for simple products is the default variant's
///   effective online price (OnlinePrice &gt; 0 ? OnlinePrice : Price),
///   falling back to the default-unit price for products authored before
///   Wave 3 (OnlinePrice = null).
/// - Negative storefront prices are rejected; null/zero means "use POS price".
/// </summary>
public sealed class SimpleProductStorefrontPricingTests
{
    // ---------- CatalogPricingAuthority ----------

    [Fact]
    public void NormalizeSimpleOnlinePrice_Null_ReturnsNull()
    {
        Assert.Null(CatalogPricingAuthority.NormalizeSimpleOnlinePrice(null));
    }

    [Fact]
    public void NormalizeSimpleOnlinePrice_Zero_ReturnsNull_PosFallback()
    {
        Assert.Null(CatalogPricingAuthority.NormalizeSimpleOnlinePrice(0m));
    }

    [Fact]
    public void NormalizeSimpleOnlinePrice_Positive_Preserved()
    {
        Assert.Equal(80.5m, CatalogPricingAuthority.NormalizeSimpleOnlinePrice(80.5m));
    }

    [Fact]
    public void NormalizeSimpleOnlinePrice_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CatalogPricingAuthority.NormalizeSimpleOnlinePrice(-1m));
    }

    [Fact]
    public void ResolveSimpleOnlinePrice_ConfiguredOnlinePrice_WinsOverUnitPrice()
    {
        var variant = SimpleVariant(price: 100m, onlinePrice: 80m);
        var unit = SimpleUnit(sellingPrice: 100m);

        Assert.Equal(80m, CatalogPricingAuthority.ResolveSimpleOnlinePrice(variant, unit));
    }

    [Fact]
    public void ResolveSimpleOnlinePrice_NullOnlinePrice_FallsBackToUnitPrice()
    {
        var variant = SimpleVariant(price: 100m, onlinePrice: null);
        var unit = SimpleUnit(sellingPrice: 100m);

        Assert.Equal(100m, CatalogPricingAuthority.ResolveSimpleOnlinePrice(variant, unit));
    }

    [Fact]
    public void ResolveSimpleOnlinePrice_ZeroOnlinePrice_FallsBackToUnitPrice()
    {
        var variant = SimpleVariant(price: 100m, onlinePrice: 0m);
        var unit = SimpleUnit(sellingPrice: 100m);

        Assert.Equal(100m, CatalogPricingAuthority.ResolveSimpleOnlinePrice(variant, unit));
    }

    [Fact]
    public void ResolveSimpleOnlinePrice_NoVariantNoUnit_ReturnsZero()
    {
        Assert.Equal(0m, CatalogPricingAuthority.ResolveSimpleOnlinePrice(null, null));
    }

    // ---------- Compatibility mirror: POS independence + OnlinePrice preservation ----------

    [Fact]
    public void Mirror_PreservesExplicitOnlinePrice_WhileMirroringPosPrice()
    {
        var product = SimpleProduct();
        var unit = SimpleUnit(sellingPrice: 100m, purchasePrice: 60m);
        var variant = SimpleVariant(price: 0m, onlinePrice: 80m);

        var changed = ProductPricingCompatibilityMirror.MirrorSimpleDefaultVariantFromDefaultUnit(
            product, variant, unit);

        // POS truth follows the unit; the independently authored storefront
        // price must survive the mirror (pre-Wave-3 behavior nulled it).
        Assert.Equal(100m, variant.Price);
        Assert.Equal(60m, variant.CostPrice);
        Assert.Equal(80m, variant.OnlinePrice);
        Assert.DoesNotContain(nameof(ProductVariant.OnlinePrice), changed);
    }

    [Fact]
    public void Mirror_NullOnlinePrice_StaysNull_PosPriceStillMirrored()
    {
        var product = SimpleProduct();
        var unit = SimpleUnit(sellingPrice: 100m, purchasePrice: 60m);
        var variant = SimpleVariant(price: 0m, onlinePrice: null);

        ProductPricingCompatibilityMirror.MirrorSimpleDefaultVariantFromDefaultUnit(
            product, variant, unit);

        Assert.Equal(100m, variant.Price);
        Assert.Null(variant.OnlinePrice);
    }

    [Fact]
    public void Mirror_VariantProduct_DoesNothing()
    {
        var product = SimpleProduct(hasVariants: true);
        var unit = SimpleUnit(sellingPrice: 100m);
        var variant = SimpleVariant(price: 5m, onlinePrice: 7m);

        var changed = ProductPricingCompatibilityMirror.MirrorSimpleDefaultVariantFromDefaultUnit(
            product, variant, unit);

        Assert.Empty(changed);
        Assert.Equal(5m, variant.Price);
        Assert.Equal(7m, variant.OnlinePrice);
    }

    // ---------- Authoring validation ----------

    [Fact]
    public async Task Validator_NegativeSimpleOnlinePrice_Rejected()
    {
        var validator = new ProductAuthoringValidator();
        var command = SimpleCommand(hasVariants: false, onlinePrice: -5m);

        var errors = await validator.ValidateAsync(command);

        var error = Assert.Single(errors, e => e.Code == "PRODUCT_VARIANT_ONLINE_PRICE_INVALID");
        Assert.Equal("onlinePrice", error.Field);
    }

    [Fact]
    public async Task Validator_NullSimpleOnlinePrice_Accepted_PosFallback()
    {
        var validator = new ProductAuthoringValidator();
        var command = SimpleCommand(hasVariants: false, onlinePrice: null);

        var errors = await validator.ValidateAsync(command);

        Assert.DoesNotContain(errors, e => e.Code == "PRODUCT_VARIANT_ONLINE_PRICE_INVALID");
    }

    [Fact]
    public async Task Validator_ZeroSimpleOnlinePrice_Accepted_PosFallback()
    {
        var validator = new ProductAuthoringValidator();
        var command = SimpleCommand(hasVariants: false, onlinePrice: 0m);

        var errors = await validator.ValidateAsync(command);

        Assert.DoesNotContain(errors, e => e.Code == "PRODUCT_VARIANT_ONLINE_PRICE_INVALID");
    }

    [Fact]
    public async Task Validator_VariantProduct_PricingOnlinePriceIgnored()
    {
        // For variant containers the per-variant OnlinePrice rules apply;
        // the product-level onlinePrice field carries no meaning.
        var validator = new ProductAuthoringValidator();
        var command = SimpleCommand(hasVariants: true, onlinePrice: -5m);

        var errors = await validator.ValidateAsync(command);

        Assert.DoesNotContain(errors, e => e.Field == "onlinePrice");
    }

    // ---------- Storefront reader ----------

    [Fact]
    public void Reader_SimpleWithConfiguredOnlinePrice_UsesStorefrontPrice()
    {
        var product = SimpleDocument(defaultUnitPrice: 100m, variantPrice: 100m, variantOnlinePrice: 80m);

        Assert.Equal(80m, ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice(product));
        Assert.Equal(80m, ProductStorefrontVisibilityAuthority.ResolveVisiblePrice(product));

        var purchasability = ProductStorefrontVisibilityAuthority.Evaluate(product);
        Assert.Equal(80m, purchasability.Price);
        Assert.True(purchasability.IsAvailable);
        Assert.True(purchasability.CanPurchase);
    }

    [Fact]
    public void Reader_SimpleWithoutOnlinePrice_FallsBackToUnitPrice_BackwardCompatible()
    {
        var product = SimpleDocument(defaultUnitPrice: 100m, variantPrice: 100m, variantOnlinePrice: null);

        Assert.Equal(100m, ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice(product));
        Assert.Equal(100m, ProductStorefrontVisibilityAuthority.ResolveVisiblePrice(product));
    }

    [Fact]
    public void Reader_LegacySimpleWithDivergentVariantPrice_StillUsesUnitPrice()
    {
        var product = SimpleDocument(defaultUnitPrice: 100m, variantPrice: 75m, variantOnlinePrice: null);

        Assert.Equal(100m, ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice(product));
        Assert.Equal(100m, ProductStorefrontVisibilityAuthority.ResolveVisiblePrice(product));
    }

    [Fact]
    public void Reader_SimpleWithoutVariantRows_FallsBackToUnitPrice()
    {
        var product = SimpleDocument(defaultUnitPrice: 100m, variantPrice: 100m, variantOnlinePrice: null);
        product.Variants.Clear();

        Assert.Equal(100m, ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice(product));
    }

    [Fact]
    public void Reader_MultiVariant_Unchanged_UsesVariantEffectivePrice()
    {
        var product = new ProductDocument
        {
            HasVariants = true,
            IsActive = true,
            IsOnlineVisible = true,
            DefaultUnit = new ProductUnitDocument { SalePrice = 999m },
            Variants =
            [
                new ProductVariantDocument
                {
                    VariantId = Guid.NewGuid(),
                    IsDefault = true,
                    IsActive = true,
                    Price = 50m,
                    OnlinePrice = 40m
                }
            ]
        };

        // Container-level unit price must never leak into variant pricing.
        Assert.Equal(40m, ProductStorefrontVisibilityAuthority.ResolveVisiblePrice(product));
    }

    [Fact]
    public void ProductReadDto_EffectivePrice_DoesNotRevertToPosUnitPrice()
    {
        var dto = new ProductReadDto
        {
            Price = 80m,
            DefaultUnit = new ProductUnitDto { SalePrice = 100m }
        };

        Assert.Equal(80m, dto.EffectivePrice);
    }

    [Fact]
    public void ProductListItemDto_EffectivePrice_DoesNotRevertToPosUnitPrice()
    {
        var dto = new ProductListItemDto
        {
            Price = 80m,
            DefaultUnit = new ProductUnitDto { SalePrice = 100m }
        };

        Assert.Equal(80m, dto.EffectivePrice);
    }

    // ---------- Currency / rounding ----------

    [Fact]
    public void Currency_RoundsHalfAwayFromZero_TwoDecimals()
    {
        Assert.Equal(10.01m, CurrencyHelper.Round(10.005m));
        Assert.Equal(10.02m, CurrencyHelper.Round(10.015m));
        Assert.Equal(80.50m, CurrencyHelper.Round(80.5m));
    }

    // ---------- Helpers ----------

    private static Product SimpleProduct(bool hasVariants = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Name = "Simple",
            CategoryId = Guid.NewGuid(),
            HasVariants = hasVariants,
            CreatedAt = DateTime.UtcNow
        };

    private static ProductUnit SimpleUnit(decimal sellingPrice, decimal purchasePrice = 0m) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            UnitId = Guid.NewGuid(),
            ConversionFactor = 1m,
            SellingPrice = sellingPrice,
            PurchasePrice = purchasePrice,
            IsDefault = true,
            IsBaseUnit = true
        };

    private static ProductVariant SimpleVariant(decimal price, decimal? onlinePrice) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Price = price,
            OnlinePrice = onlinePrice,
            CostPrice = 10m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

    private static ProductDocument SimpleDocument(
        decimal defaultUnitPrice, decimal variantPrice, decimal? variantOnlinePrice) =>
        new()
        {
            ProductId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Name = "Simple",
            HasVariants = false,
            IsActive = true,
            IsOnlineVisible = true,
            TrackStock = false,
            DefaultUnit = new ProductUnitDocument { SalePrice = defaultUnitPrice },
            Variants =
            [
                new ProductVariantDocument
                {
                    VariantId = Guid.NewGuid(),
                    IsDefault = true,
                    IsActive = true,
                    Price = variantPrice,
                    OnlinePrice = variantOnlinePrice,
                    TotalStock = 5m
                }
            ]
        };

    private static ProductAuthoringCommand SimpleCommand(bool hasVariants, decimal? onlinePrice) =>
        new(
            ProductAuthoringOperation.Update,
            new ProductAuthoringIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SKU-W3", "BAR-W3"),
            new ProductAuthoringContent("Wave3 simple", null, null, null, null, null, [], [], []),
            new ProductAuthoringPricing(60m, 100m, onlinePrice, false, 0m),
            new ProductAuthoringPublication(PublishStatus.Published, true, true, true, false),
            new ProductAuthoringSeo(null, null, null, null),
            new ProductAuthoringShippingProfile(0m, 0m, 0m, 0m, "kg", "cm"),
            new ProductAuthoringInventoryProfile("Piece", 0m, true, hasVariants, 10, false),
            new ProductAuthoringUnits(null, null, Array.Empty<ProductAuthoringUnit>()),
            Array.Empty<ProductAuthoringVariant>(),
            new ProductAuthoringMetadata(false, null, null, "default", "wave3", null, null, new Dictionary<string, string>()));
}
