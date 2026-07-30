using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Pricing;

/// <summary>
/// The only live business component allowed to maintain legacy Product price fields.
/// Product price fields are compatibility mirrors, not pricing authorities.
/// </summary>
public static class ProductPricingCompatibilityMirror
{
    public static IReadOnlyList<string> MirrorSimpleProductFromDefaultUnit(
        Product product,
        ProductUnit? defaultUnit)
    {
        if (product.HasVariants)
        {
            return Array.Empty<string>();
        }

        var changedFields = new List<string>();
        SetIfChanged(product, product => product.PurchasePrice, (product, value) => product.PurchasePrice = value,
            CatalogPricingAuthority.ResolveSimplePurchasePrice(defaultUnit), nameof(Product.PurchasePrice), changedFields);
        SetIfChanged(product, product => product.SalePrice, (product, value) => product.SalePrice = value,
            CatalogPricingAuthority.ResolveSimpleSellingPrice(defaultUnit), nameof(Product.SalePrice), changedFields);
        SetIfChanged(product, product => product.OnlinePrice, (product, value) => product.OnlinePrice = value,
            (decimal?)null, nameof(Product.OnlinePrice), changedFields);

        return changedFields;
    }

    public static IReadOnlyList<string> MirrorSimpleDefaultVariantFromDefaultUnit(
        Product product,
        ProductVariant variant,
        ProductUnit? defaultUnit)
    {
        if (product.HasVariants)
        {
            return Array.Empty<string>();
        }

        var changedFields = new List<string>();
        SetIfChanged(variant, variant => variant.Price, (variant, value) => variant.Price = value,
            CatalogPricingAuthority.ResolveSimpleSellingPrice(defaultUnit), nameof(ProductVariant.Price), changedFields);
        SetIfChanged(variant, variant => variant.OnlinePrice, (variant, value) => variant.OnlinePrice = value,
            (decimal?)null, nameof(ProductVariant.OnlinePrice), changedFields);
        SetIfChanged(variant, variant => variant.CostPrice, (variant, value) => variant.CostPrice = value,
            CatalogPricingAuthority.ResolveSimplePurchasePrice(defaultUnit), nameof(ProductVariant.CostPrice), changedFields);

        return changedFields;
    }

    public static IReadOnlyList<string> SeedDefaultUnitFromLegacyProductMirror(
        Product product,
        ProductUnit defaultUnit)
    {
        if (product.HasVariants)
        {
            return Array.Empty<string>();
        }

        var changedFields = new List<string>();

        SetIfChanged(defaultUnit, unit => unit.PurchasePrice, (unit, value) => unit.PurchasePrice = value,
            product.PurchasePrice, nameof(ProductUnit.PurchasePrice), changedFields);
        SetIfChanged(defaultUnit, unit => unit.SellingPrice, (unit, value) => unit.SellingPrice = value,
            product.SalePrice, nameof(ProductUnit.SellingPrice), changedFields);

        return changedFields;
    }

    private static void SetIfChanged<TEntity, TValue>(
        TEntity entity,
        Func<TEntity, TValue> getter,
        Action<TEntity, TValue> setter,
        TValue value,
        string fieldName,
        ICollection<string> changedFields)
    {
        if (EqualityComparer<TValue>.Default.Equals(getter(entity), value))
        {
            return;
        }

        setter(entity, value);
        changedFields.Add(fieldName);
    }
}
