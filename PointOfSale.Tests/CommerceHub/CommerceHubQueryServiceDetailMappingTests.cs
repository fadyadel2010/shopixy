using System.Reflection;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubQueryServiceDetailMappingTests
{
    [Fact]
    public void MapDetail_MustKeepMarketplaceCategoryIdentityFromListingAuthority()
    {
        var listing = new CommerceHubProductDocument
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Runner Pro",
            CategoryId = Guid.NewGuid(),
            CategoryName = "Marketplace Shoes",
            CategoryPath = "Fashion > Shoes",
            Currency = "EGP",
            Price = 150m,
            IsPublished = true,
            InStock = true,
            AvailableStock = 5m
        };
        var product = new ProductDocument
        {
            TenantId = listing.TenantId,
            ProductId = listing.ProductId,
            Name = "Runner Pro",
            CategoryId = Guid.NewGuid(),
            CategoryName = "Storefront Category Drift",
            IsActive = true,
            IsOnlineVisible = true,
            Media = new List<ProductMediaDocument>(),
            Variants = new List<ProductVariantDocument>()
        };

        var result = InvokeMapDetail(listing, product);

        Assert.Equal("Marketplace Shoes", result.CategoryName);
        Assert.Equal("Fashion > Shoes", result.CategoryPath);
    }

    [Fact]
    public void MapDetail_MustNotRestoreLegacyVariantImage_WhenAuthoritativeMediaIsMissing()
    {
        var listing = new CommerceHubProductDocument
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Runner Pro",
            CategoryId = Guid.NewGuid(),
            CategoryName = "Marketplace Shoes",
            CategoryPath = "Fashion > Shoes",
            Currency = "EGP",
            Price = 150m,
            IsPublished = true,
            InStock = true,
            AvailableStock = 5m
        };
        var product = new ProductDocument
        {
            TenantId = listing.TenantId,
            ProductId = listing.ProductId,
            Name = "Runner Pro",
            IsActive = true,
            IsOnlineVisible = true,
            HasVariants = true,
            Media = new List<ProductMediaDocument>(),
            Variants = new List<ProductVariantDocument>
            {
                new()
                {
                    VariantId = listing.VariantId,
                    Price = 150m,
                    OnlinePrice = 150m,
                    IsDefault = true,
                    IsActive = true,
                    TotalStock = 5m,
                    ImageUrl = "https://cdn.shopixy.test/stale-legacy-variant.jpg"
                }
            }
        };

        var result = InvokeMapDetail(listing, product);

        Assert.Single(result.Variants);
        Assert.Null(result.Image);
        Assert.Empty(result.Images);
        Assert.Null(result.Variants[0].ImageUrl);
    }

    [Fact]
    public void MapDetail_MustKeepListingPriceAndExcludeVariantsWithoutListingIdentity()
    {
        var listing = new CommerceHubProductDocument
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Runner Pro",
            CategoryId = Guid.NewGuid(),
            Currency = "EGP",
            Price = 125m,
            CommerceHubPriceOverride = 125m,
            OverridePresent = true,
            IsPublished = true,
            InStock = true,
            AvailableStock = 4m
        };
        var unrelatedVariantId = Guid.NewGuid();
        var product = new ProductDocument
        {
            TenantId = listing.TenantId,
            ProductId = listing.ProductId,
            Name = listing.ProductName,
            IsActive = true,
            IsOnlineVisible = true,
            HasVariants = true,
            Media = new List<ProductMediaDocument>(),
            Variants = new List<ProductVariantDocument>
            {
                new()
                {
                    VariantId = listing.VariantId,
                    Price = 190m,
                    OnlinePrice = 175m,
                    IsActive = true,
                    TotalStock = 20m
                },
                new()
                {
                    VariantId = unrelatedVariantId,
                    Price = 80m,
                    OnlinePrice = 70m,
                    IsDefault = true,
                    IsActive = true,
                    TotalStock = 30m
                }
            }
        };

        var result = InvokeMapDetail(listing, product);

        Assert.Equal(listing.Id, result.Id);
        Assert.Equal(listing.VariantId, result.InitialVariantId);
        Assert.Equal(listing.VariantId, result.DefaultVariantId);
        Assert.Equal(listing.Price, result.Price);
        Assert.False(result.HasVariants);

        var variant = Assert.Single(result.Variants);
        Assert.Equal(listing.VariantId, variant.Id);
        Assert.Equal(listing.Price, variant.Price);
        Assert.Equal(listing.Price, variant.OnlinePrice);
        Assert.Equal(listing.Price, variant.EffectivePrice);
        Assert.Equal(4, variant.TotalStock);
        Assert.True(variant.IsDefault);
        Assert.DoesNotContain(result.Variants, item => item.Id == unrelatedVariantId);
    }

    private static CommerceHubProductDetailDto InvokeMapDetail(
        CommerceHubProductDocument listing,
        ProductDocument? product)
    {
        var method = typeof(CommerceHubQueryService).GetMethod(
            "MapDetail",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        return Assert.IsType<CommerceHubProductDetailDto>(method!.Invoke(null, new object?[] { listing, product }));
    }
}
