using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Tests.Products;

public sealed class ProductMediaImageResolverTests
{
    [Fact]
    public void SimpleProduct_UsesPrimaryEvenWhenInternalSkuHasMedia()
    {
        var product = Product();
        product.HasVariants = false;
        Assert.Equal("/primary.png", ProductMediaImageResolver.Resolve(product, product.Variants[0].VariantId));
    }

    [Fact]
    public void VariableProduct_SelectsByVariantIdAndFallsBackForUnassignedVariant()
    {
        var product = Product();
        Assert.Equal("/a.png", ProductMediaImageResolver.Resolve(product, product.Variants[0].VariantId));
        Assert.Equal("/b.png", ProductMediaImageResolver.Resolve(product, product.Variants[1].VariantId));
        Assert.Equal("/primary.png", ProductMediaImageResolver.Resolve(product, product.Variants[2].VariantId));
        Assert.Null(ProductMediaImageResolver.Resolve(product, Guid.NewGuid()));
    }

    [Fact]
    public void ForeignOrRemovedMediaReference_CannotUseLegacyUrlOrAnotherProductsMedia()
    {
        var product = Product();
        product.Variants[0].MediaId = Guid.NewGuid();
        product.Variants[0].ImageUrl = "/foreign-tenant.png";
        product.Images = "/legacy-product.png";
        Assert.Equal("/primary.png", ProductMediaImageResolver.Resolve(product, product.Variants[0].VariantId));
        product.Media.Clear();
        Assert.Null(ProductMediaImageResolver.Resolve(product, product.Variants[0].VariantId));
    }

    private static ProductDocument Product()
    {
        var primary = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        return new ProductDocument
        {
            HasVariants = true,
            Media = [
                new() { Id = b, Type = "image", Url = "/b.png", Position = 3 },
                new() { Id = Guid.NewGuid(), Type = "video", Url = "/video.mp4", Position = 0 },
                new() { Id = primary, Type = "image", Url = "/primary.png", Position = 1 },
                new() { Id = a, Type = "image", Url = "/a.png", Position = 2 }
            ],
            Variants = [
                new() { VariantId = Guid.NewGuid(), MediaId = a },
                new() { VariantId = Guid.NewGuid(), MediaId = b },
                new() { VariantId = Guid.NewGuid() }
            ]
        };
    }
}
