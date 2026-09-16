using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Pricing;

/// <summary>
/// Canonical catalog pricing authority declarations.
/// </summary>
public static class CatalogPricingAuthority
{
    public static decimal ResolveSimpleSellingPrice(ProductUnit? defaultUnit) =>
        defaultUnit?.SellingPrice ?? 0m;

    public static decimal ResolveSimplePurchasePrice(ProductUnit? defaultUnit) =>
        defaultUnit?.PurchasePrice ?? 0m;

    public static decimal ResolveVariantSellingPrice(ProductVariant variant) =>
        variant.Price;

    public static decimal ResolveVariantOnlinePrice(ProductVariant variant) =>
        variant.OnlinePrice is > 0m ? variant.OnlinePrice.Value : variant.Price;

    public static decimal ResolveVariantPurchasePrice(ProductVariant variant) =>
        variant.CostPrice;

    /// <summary>
    /// WAVE 3 — simple-product online price normalization.
    /// The simple online price lives on the simple default variant's
    /// <see cref="ProductVariant.OnlinePrice"/> (same convention as multi-variant
    /// products). Null or zero means "use the POS price" (backward compatible).
    /// Negative values are rejected here as well as by authoring validation.
    /// </summary>
    public static decimal? NormalizeSimpleOnlinePrice(decimal? onlinePrice) =>
        onlinePrice switch
        {
            null => null,
            < 0m => throw new ArgumentOutOfRangeException(
                nameof(onlinePrice), onlinePrice, "Simple product storefront price cannot be negative."),
            <= 0m => null,
            _ => onlinePrice
        };

    /// <summary>
    /// WAVE 3 — resolves the effective simple-product online price:
    /// default-variant effective online price when explicitly configured,
    /// otherwise the default-unit POS selling price (legacy behavior).
    /// </summary>
    public static decimal ResolveSimpleOnlinePrice(ProductVariant? defaultVariant, ProductUnit? defaultUnit) =>
        defaultVariant?.OnlinePrice is > 0m
            ? defaultVariant.OnlinePrice.Value
            : ResolveSimpleSellingPrice(defaultUnit);

    public static bool HasPositiveSimpleSellingPrice(ProductUnit? defaultUnit) =>
        ResolveSimpleSellingPrice(defaultUnit) > 0m;

    public static bool HasPositiveVariantSellingPrice(ProductVariant variant) =>
        ResolveVariantOnlinePrice(variant) > 0m;
}
