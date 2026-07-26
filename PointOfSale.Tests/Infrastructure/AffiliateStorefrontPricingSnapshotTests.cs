using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Tests.Infrastructure;

public sealed class AffiliateStorefrontPricingSnapshotTests
{
    [Fact]
    public void TryResolveAffiliatePublicPricing_SimpleProductUsesStorefrontVisiblePriceAndVariantStockFallback()
    {
        var product = new ProductDocument
        {
            HasVariants = false,
            DefaultUnit = new ProductUnitDocument
            {
                UnitId = Guid.NewGuid(),
                Name = "Piece",
                SalePrice = 25m
            },
            Variants = new List<ProductVariantDocument>
            {
                new()
                {
                    VariantId = Guid.NewGuid(),
                    IsActive = true,
                    IsDefault = true,
                    OnlinePrice = 999m,
                    TotalStock = 7m
                }
            }
        };

        var resolved = product.TryResolveAffiliatePublicPricing(out var snapshot);

        Assert.True(resolved);
        Assert.Equal(999m, snapshot.Price);
        Assert.Null(snapshot.MinVariantPrice);
        Assert.Null(snapshot.MaxVariantPrice);
        Assert.True(snapshot.InStock);
        Assert.Equal(7, snapshot.TotalAvailableStock);
    }

    [Fact]
    public void TryResolveAffiliateDiscoveryBasePrice_SimpleProductUsesStorefrontVisiblePriceButRequiresDefaultUnit()
    {
        var product = new ProductDocument
        {
            HasVariants = false,
            DefaultUnit = new ProductUnitDocument
            {
                UnitId = Guid.NewGuid(),
                Name = "Piece",
                SalePrice = 25m
            },
            Variants = new List<ProductVariantDocument>
            {
                new()
                {
                    VariantId = Guid.NewGuid(),
                    IsActive = true,
                    IsDefault = true,
                    OnlinePrice = 60m,
                    TotalStock = 4m
                }
            }
        };

        var resolved = product.TryResolveAffiliateDiscoveryBasePrice(out var basePrice);

        Assert.True(resolved);
        Assert.Equal(60m, basePrice);
    }

    [Fact]
    public void TryResolveAffiliatePublicPricing_SimpleProductWithoutDefaultUnitFailsClosed()
    {
        var product = new ProductDocument
        {
            HasVariants = false,
            Price = 999m,
            OnlinePrice = 999m,
            StockQuantity = 8
        };

        var resolved = product.TryResolveAffiliatePublicPricing(out var snapshot);

        Assert.False(resolved);
        Assert.Equal(default, snapshot);
        Assert.False(product.TryResolveAffiliateDiscoveryBasePrice(out var basePrice));
        Assert.Equal(0m, basePrice);
    }

    [Fact]
    public void TryResolveAffiliatePublicPricing_VariantProductRequiresPricedActiveVariants()
    {
        var product = new ProductDocument
        {
            HasVariants = true,
            Variants = new List<ProductVariantDocument>
            {
                new()
                {
                    VariantId = Guid.NewGuid(),
                    IsActive = true,
                    IsDefault = true,
                    OnlinePrice = 0m,
                    Price = 0m,
                    TotalStock = 4m
                }
            }
        };

        var resolved = product.TryResolveAffiliatePublicPricing(out var snapshot);

        Assert.False(resolved);
        Assert.Equal(default, snapshot);
    }

    [Fact]
    public void TryResolveAffiliateDiscoveryBasePrice_UsesDefaultPricedVariant()
    {
        var defaultVariantId = Guid.NewGuid();
        var product = new ProductDocument
        {
            HasVariants = true,
            Variants = new List<ProductVariantDocument>
            {
                new()
                {
                    VariantId = Guid.NewGuid(),
                    IsActive = true,
                    OnlinePrice = 15m,
                    TotalStock = 3m,
                    SortOrder = 1
                },
                new()
                {
                    VariantId = defaultVariantId,
                    IsActive = true,
                    IsDefault = true,
                    OnlinePrice = 20m,
                    TotalStock = 5m,
                    SortOrder = 0
                }
            }
        };

        var resolved = product.TryResolveAffiliateDiscoveryBasePrice(out var basePrice);

        Assert.True(resolved);
        Assert.Equal(20m, basePrice);
    }
}
