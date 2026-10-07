namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// Image selection for SQL-authority-composed product documents. MediaId is
/// resolved only inside this product's gallery; legacy image strings are not authority.
/// Simple products always use product primary media, including their internal default SKU.
/// </summary>
public static class ProductMediaImageResolver
{
    public static string? Resolve(PointOfSale.Core.Entities.Product product, Guid? variantId = null)
    {
        // Reuse the same selection contract for SQL-only POS response paths.
        return Resolve(new ProductDocument
        {
            HasVariants = product.HasVariants,
            Media = product.Media
                .Where(media => media.TenantId == product.TenantId && media.ProductId == product.Id)
                .Select(media => new ProductMediaDocument
                {
                    Id = media.Id, Type = media.MediaType, Url = media.Url, Position = media.Position
                }).ToList(),
            Variants = product.Variants
                .Where(variant => variant.TenantId == product.TenantId && variant.ProductId == product.Id && !variant.IsDeleted)
                .Select(variant => new ProductVariantDocument { VariantId = variant.Id, MediaId = variant.MediaId })
                .ToList()
        }, variantId);
    }

    public static string? Resolve(ProductDocument product, Guid? variantId = null)
    {
        var images = OrderedImages(product);
        if (product.HasVariants && variantId.HasValue)
        {
            var variant = product.Variants?.FirstOrDefault(item => item.VariantId == variantId.Value);
            if (variant == null)
                return null;

            var selected = images.FirstOrDefault(media => media.Id == variant.MediaId);
            if (selected != null)
                return selected.Url;
        }

        return images.FirstOrDefault()?.Url;
    }

    public static IReadOnlyList<ProductMediaDocument> OrderedImages(ProductDocument product) =>
        (product.Media ?? [])
            .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(media.Url))
            .OrderBy(media => media.Position)
            .ThenBy(media => media.Id)
            .ToList();
}
