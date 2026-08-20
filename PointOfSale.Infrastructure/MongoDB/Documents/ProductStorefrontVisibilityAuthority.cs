namespace PointOfSale.Infrastructure.MongoDB.Documents;

public static class ProductStorefrontVisibilityAuthority
{
    public static decimal ResolveSimpleProductPrice(ProductDocument product) =>
        product.DefaultUnit?.SalePrice ?? 0m;

    public static StorefrontPurchasabilityResult Evaluate(ProductDocument product)
    {
        var activePricedVariants = ResolvePricedActiveVariants(product).ToList();
        if (product.HasVariants)
        {
            var selectedVariant = activePricedVariants
                .OrderBy(variant => variant.IsDefault ? 0 : 1)
                .ThenBy(variant => variant.SortOrder)
                .FirstOrDefault();
            var stockQuantity = Math.Max(
                0,
                decimal.ToInt32(decimal.Truncate(activePricedVariants.Sum(variant => variant.TotalStock))));
            var inStock = activePricedVariants.Any(variant => IsVariantOrderableByStockPolicy(product, variant));

            return new StorefrontPurchasabilityResult(
                IsAvailable: product.IsActive && product.IsOnlineVisible && selectedVariant != null,
                CanPurchase: false,
                Price: selectedVariant?.EffectivePrice ?? 0m,
                InStock: inStock,
                StockQuantity: stockQuantity,
                RequiresVariantSelection: true,
                UnavailableReason: selectedVariant == null
                    ? StorefrontPurchasabilityUnavailableReason.NoPricedVariant
                    : StorefrontPurchasabilityUnavailableReason.VariantSelectionRequired);
        }

        var price = ResolveSimpleProductPrice(product);
        var isAvailable = product.IsActive && product.IsOnlineVisible && price > 0m;
        var inStockSimpleProduct = product.InStock;
        return new StorefrontPurchasabilityResult(
            IsAvailable: isAvailable,
            CanPurchase: isAvailable && inStockSimpleProduct,
            Price: price,
            InStock: inStockSimpleProduct,
            StockQuantity: Math.Max(0, product.StockQuantity),
            RequiresVariantSelection: false,
            UnavailableReason: ResolveSimpleProductUnavailableReason(product, price, inStockSimpleProduct));
    }

    public static StorefrontPurchasabilityResult EvaluateVariant(
        ProductDocument product,
        ProductVariantDocument variant)
    {
        var price = variant.EffectivePrice;
        var stockQuantity = Math.Max(0, decimal.ToInt32(decimal.Truncate(variant.TotalStock)));
        var isAvailable = product.IsActive &&
                          product.IsOnlineVisible &&
                          product.HasVariants &&
                          variant.IsActive &&
                          price > 0m;
        var inStock = IsVariantOrderableByStockPolicy(product, variant);

        return new StorefrontPurchasabilityResult(
            IsAvailable: isAvailable,
            CanPurchase: isAvailable && inStock,
            Price: price,
            InStock: inStock,
            StockQuantity: stockQuantity,
            RequiresVariantSelection: false,
            UnavailableReason: ResolveVariantUnavailableReason(product, variant, price, inStock));
    }

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
        ResolvePricedActiveVariants(product)
        .OrderBy(variant => variant.IsDefault ? 0 : 1)
        .ThenBy(variant => variant.SortOrder)
        .FirstOrDefault();

    private static IEnumerable<ProductVariantDocument> ResolvePricedActiveVariants(ProductDocument product) =>
        (product.Variants ?? new List<ProductVariantDocument>())
        .Where(variant => variant.IsActive && variant.EffectivePrice > 0m);

    private static bool IsVariantOrderableByStockPolicy(ProductDocument product, ProductVariantDocument variant) =>
        !product.TrackStock || product.AllowBackorder || variant.TotalStock > 0m;

    private static string? ResolveSimpleProductUnavailableReason(
        ProductDocument product,
        decimal price,
        bool inStock)
    {
        if (!product.IsActive || !product.IsOnlineVisible)
        {
            return StorefrontPurchasabilityUnavailableReason.NotVisible;
        }

        if (product.DefaultUnit == null)
        {
            return StorefrontPurchasabilityUnavailableReason.MissingDefaultUnit;
        }

        if (price <= 0m)
        {
            return StorefrontPurchasabilityUnavailableReason.InvalidDefaultUnitPrice;
        }

        return inStock ? null : StorefrontPurchasabilityUnavailableReason.OutOfStock;
    }

    private static string? ResolveVariantUnavailableReason(
        ProductDocument product,
        ProductVariantDocument variant,
        decimal price,
        bool inStock)
    {
        if (!product.IsActive || !product.IsOnlineVisible || !product.HasVariants)
        {
            return StorefrontPurchasabilityUnavailableReason.NotVisible;
        }

        if (!variant.IsActive)
        {
            return StorefrontPurchasabilityUnavailableReason.VariantInactive;
        }

        if (price <= 0m)
        {
            return StorefrontPurchasabilityUnavailableReason.NoPricedVariant;
        }

        return inStock ? null : StorefrontPurchasabilityUnavailableReason.OutOfStock;
    }

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

public sealed record StorefrontPurchasabilityResult(
    bool IsAvailable,
    bool CanPurchase,
    decimal Price,
    bool InStock,
    int StockQuantity,
    bool RequiresVariantSelection,
    string? UnavailableReason);

public static class StorefrontPurchasabilityUnavailableReason
{
    public const string NotVisible = "NOT_VISIBLE";
    public const string MissingDefaultUnit = "MISSING_DEFAULT_UNIT";
    public const string InvalidDefaultUnitPrice = "INVALID_DEFAULT_UNIT_PRICE";
    public const string VariantSelectionRequired = "VARIANT_SELECTION_REQUIRED";
    public const string NoPricedVariant = "NO_PRICED_VARIANT";
    public const string VariantInactive = "VARIANT_INACTIVE";
    public const string OutOfStock = "OUT_OF_STOCK";
}
