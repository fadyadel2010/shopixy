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

    public static bool HasPositiveSimpleSellingPrice(ProductUnit? defaultUnit) =>
        ResolveSimpleSellingPrice(defaultUnit) > 0m;

    public static bool HasPositiveVariantSellingPrice(ProductVariant variant) =>
        ResolveVariantOnlinePrice(variant) > 0m;
}
