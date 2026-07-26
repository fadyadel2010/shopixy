namespace PointOfSale.Infrastructure.MongoDB.Documents;

public static class ProductStorefrontVisibilityAuthority
{
    public static decimal ResolveSimpleProductPrice(ProductDocument product) =>
        product.DefaultUnit?.SalePrice ?? 0m;

    public static decimal ResolveVisiblePrice(ProductDocument product)
    {
        var visibleVariant = ResolveVisibleVariant(product);
        if (product.HasVariants)
        {
            return visibleVariant?.EffectivePrice ?? 0m;
        }

        return ResolveSimpleProductPrice(product);
    }

    private static ProductVariantDocument? ResolveVisibleVariant(ProductDocument product) =>
        (product.Variants ?? new List<ProductVariantDocument>())
        .Where(variant => variant.IsActive && variant.EffectivePrice > 0m)
        .OrderBy(variant => variant.IsDefault ? 0 : 1)
        .ThenBy(variant => variant.SortOrder)
        .FirstOrDefault();

    public static bool TryResolveAffiliatePublicPricing(
        ProductDocument product,
        out AffiliatePublicPricingSnapshot snapshot)
    {
        var pricedActiveVariants = (product.Variants ?? new List<ProductVariantDocument>())
            .Where(variant => variant.IsActive && variant.EffectivePrice > 0m)
            .OrderBy(variant => variant.SortOrder)
            .ThenBy(variant => variant.IsDefault ? 0 : 1)
            .ToList();

        if (product.HasVariants)
        {
            if (pricedActiveVariants.Count == 0)
            {
                snapshot = default;
                return false;
            }

            snapshot = new AffiliatePublicPricingSnapshot(
                Price: null,
                MinVariantPrice: pricedActiveVariants.Min(variant => variant.EffectivePrice),
                MaxVariantPrice: pricedActiveVariants.Max(variant => variant.EffectivePrice),
                InStock: pricedActiveVariants.Any(variant => variant.TotalStock > 0),
                TotalAvailableStock: Math.Max(
                    0,
                    decimal.ToInt32(decimal.Truncate(pricedActiveVariants.Sum(variant => variant.TotalStock)))));
            return true;
        }

        var simplePurchasabilityPrice = ResolveSimpleProductPrice(product);
        if (simplePurchasabilityPrice <= 0m)
        {
            snapshot = default;
            return false;
        }

        var simplePrice = ResolveVisiblePrice(product);
        if (simplePrice <= 0m)
        {
            snapshot = default;
            return false;
        }

        var totalAvailableStock = pricedActiveVariants.Count > 0
            ? Math.Max(0, decimal.ToInt32(decimal.Truncate(pricedActiveVariants.Sum(variant => variant.TotalStock))))
            : Math.Max(0, product.StockQuantity);
        var inStock = pricedActiveVariants.Count > 0
            ? pricedActiveVariants.Any(variant => variant.TotalStock > 0)
            : product.InStock;

        snapshot = new AffiliatePublicPricingSnapshot(
            Price: simplePrice,
            MinVariantPrice: null,
            MaxVariantPrice: null,
            InStock: inStock,
            TotalAvailableStock: totalAvailableStock);
        return true;
    }

    public static bool TryResolveAffiliateDiscoveryBasePrice(
        ProductDocument product,
        out decimal basePrice)
    {
        if (product.HasVariants)
        {
            var visibleVariant = (product.Variants ?? new List<ProductVariantDocument>())
                .Where(variant => variant.IsActive && variant.EffectivePrice > 0m)
                .OrderBy(variant => variant.IsDefault ? 0 : 1)
                .ThenBy(variant => variant.SortOrder)
                .FirstOrDefault();

            if (visibleVariant == null)
            {
                basePrice = 0m;
                return false;
            }

            basePrice = visibleVariant.EffectivePrice;
            return true;
        }

        var simplePurchasabilityPrice = ResolveSimpleProductPrice(product);
        if (simplePurchasabilityPrice <= 0m)
        {
            basePrice = 0m;
            return false;
        }

        basePrice = ResolveVisiblePrice(product);
        return basePrice > 0m;
    }
}
