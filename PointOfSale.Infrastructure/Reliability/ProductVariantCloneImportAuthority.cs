using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Reliability;

/// <summary>
/// SQL-authoritative guardrail for clone/import style variant identity and inheritance.
/// Payloads may suggest SKU, barcode, media, stock, or active state, but current SQL
/// ownership and lineage decide what can be carried forward.
/// </summary>
public static class ProductVariantCloneImportAuthority
{
    public static async Task<string?> ResolveUniqueSkuAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        string? preferredSku,
        string? fallbackBarcode,
        Guid productId,
        Guid currentVariantId,
        CancellationToken cancellationToken = default)
    {
        var baseCandidate = FirstNonEmpty(preferredSku, fallbackBarcode);
        if (string.IsNullOrWhiteSpace(baseCandidate))
        {
            baseCandidate = $"AUTO-{productId:N}".Substring(0, 13).ToUpperInvariant();
        }

        baseCandidate = baseCandidate.Trim();
        var candidate = baseCandidate;
        var suffix = 0;

        while (await IsSkuOwnedByAnotherSemanticEntityAsync(
                   dbContext,
                   tenantId,
                   candidate,
                   productId,
                   currentVariantId,
                   cancellationToken))
        {
            suffix++;
            candidate = $"{baseCandidate}-{suffix}";
        }

        return candidate;
    }

    public static async Task<bool> IsSkuOwnedByAnotherSemanticEntityAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        string? sku,
        Guid productId,
        Guid currentVariantId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return false;
        }

        var normalizedSku = sku.Trim();

        if (dbContext.ProductVariants.Local.Any(variant =>
                variant.Id != currentVariantId &&
                variant.TenantId == tenantId &&
                !variant.IsDeleted &&
                ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(variant) &&
                string.Equals(variant.SKU, normalizedSku, StringComparison.Ordinal)))
        {
            return true;
        }

        if (dbContext.Products.Local.Any(product =>
                product.Id != productId &&
                product.TenantId == tenantId &&
                !product.IsDeleted &&
                string.Equals(product.SKU, normalizedSku, StringComparison.Ordinal)))
        {
            return true;
        }

        var variantOwnsSku = await dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .WhereCurrentSemanticOwner()
            .AnyAsync(variant =>
                variant.Id != currentVariantId &&
                variant.TenantId == tenantId &&
                !variant.IsDeleted &&
                variant.SKU == normalizedSku,
                cancellationToken);

        if (variantOwnsSku)
        {
            return true;
        }

        return await dbContext.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(product =>
                product.Id != productId &&
                product.TenantId == tenantId &&
                !product.IsDeleted &&
                product.SKU == normalizedSku,
                cancellationToken);
    }

    public static async Task<bool> IsBarcodeOwnedByAnotherSemanticEntityAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        string? barcode,
        Guid productId,
        Guid currentVariantId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return false;
        }

        var normalizedBarcode = barcode.Trim();

        if (dbContext.ProductVariants.Local.Any(variant =>
                variant.Id != currentVariantId &&
                variant.TenantId == tenantId &&
                !variant.IsDeleted &&
                ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(variant) &&
                string.Equals(variant.Barcode, normalizedBarcode, StringComparison.Ordinal)))
        {
            return true;
        }

        if (dbContext.Products.Local.Any(product =>
                product.Id != productId &&
                product.TenantId == tenantId &&
                !product.IsDeleted &&
                string.Equals(product.Barcode, normalizedBarcode, StringComparison.Ordinal)))
        {
            return true;
        }

        var variantOwnsBarcode = await dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .WhereCurrentSemanticOwner()
            .AnyAsync(variant =>
                variant.Id != currentVariantId &&
                variant.TenantId == tenantId &&
                !variant.IsDeleted &&
                variant.Barcode == normalizedBarcode,
                cancellationToken);

        if (variantOwnsBarcode)
        {
            return true;
        }

        var productOwnsBarcode = await dbContext.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(product =>
                product.Id != productId &&
                product.TenantId == tenantId &&
                !product.IsDeleted &&
                product.Barcode == normalizedBarcode,
                cancellationToken);

        if (productOwnsBarcode)
        {
            return true;
        }

        return await dbContext.ProductUnits
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(productUnit =>
                !productUnit.IsDeleted &&
                productUnit.Product.TenantId == tenantId &&
                !productUnit.Product.IsDeleted &&
                productUnit.Barcode == normalizedBarcode,
                cancellationToken);
    }

    public static async Task<bool> IsProductUnitBarcodeOwnedByAnotherSemanticEntityAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        string? barcode,
        Guid productId,
        Guid currentProductUnitId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return false;
        }

        var normalizedBarcode = barcode.Trim();

        var variantOwnsBarcode = await dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .WhereCurrentSemanticOwner()
            .AnyAsync(variant =>
                variant.TenantId == tenantId &&
                !variant.IsDeleted &&
                variant.Barcode == normalizedBarcode,
                cancellationToken);

        if (variantOwnsBarcode)
        {
            return true;
        }

        var productOwnsBarcode = await dbContext.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(product =>
                product.Id != productId &&
                product.TenantId == tenantId &&
                !product.IsDeleted &&
                product.Barcode == normalizedBarcode,
                cancellationToken);

        if (productOwnsBarcode)
        {
            return true;
        }

        return await dbContext.ProductUnits
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(productUnit =>
                productUnit.Id != currentProductUnitId &&
                !productUnit.IsDeleted &&
                productUnit.Product.TenantId == tenantId &&
                !productUnit.Product.IsDeleted &&
                productUnit.Barcode == normalizedBarcode,
                cancellationToken);
    }

    public static async Task<bool> CanInheritProductMediaAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid productId,
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.ProductMedia
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(media =>
                media.Id == mediaId &&
                media.TenantId == tenantId &&
                media.ProductId == productId &&
                media.MediaType == "image",
                cancellationToken);
    }

    public static void ApplySimpleProductIdentityFromDefaultVariant(
        Product product,
        ProductVariant defaultVariant)
    {
        if (product.HasVariants || !defaultVariant.IsDefault)
        {
            return;
        }

        product.SKU = defaultVariant.SKU;
        product.Barcode = defaultVariant.Barcode;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
