using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Helpers;

namespace PointOfSale.API.Services.ProductAuthoring;

/// <summary>
/// Wave 2 variant-identity guardrail for the product-authoring variant matrix.
///
/// Performs deterministic intra-request validation of authored variants BEFORE
/// persistence so duplicate combinations, SKUs, and barcodes surface as stable
/// 409 conflicts instead of relying solely on commit-time unique violations.
///
/// Semantics intentionally mirror the authoritative contract:
/// - Attribute combinations use the canonical <see cref="ProductVariantAttributeCombinationKey"/>
///   (order-independent, whitespace/case-normalized). Null keys (empty attribute
///   sets) are skipped, matching the partial unique index
///   (… WHERE AttributeCombinationKey IS NOT NULL).
/// - SKU/barcode duplicates are compared trimmed with <see cref="StringComparison.Ordinal"/>,
///   matching the tenant-scoped filtered unique indexes and
///   ProductVariantCloneImportAuthority normalization. Null/blank values are
///   skipped because the indexes permit multiple NULLs.
/// Cross-request ownership plus the residual race window remain enforced by
/// ProductVariantCloneImportAuthority pre-checks and the database unique
/// indexes (mapped to the same error codes by
/// ProductAuthoringDatabaseConflictClassifier).
/// </summary>
public static class ProductAuthoringVariantIdentityAuthority
{
    public static string? FindDuplicateCombinationKey(IReadOnlyList<ProductAuthoringVariant> variants)
    {
        ArgumentNullException.ThrowIfNull(variants);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in variants)
        {
            var key = ProductVariantAttributeCombinationKey.Compute(
                variant.Attributes.Select(attribute => (attribute.Key, attribute.Value)));
            if (key is null)
            {
                continue;
            }

            if (!seen.Add(key))
            {
                return key;
            }
        }

        return null;
    }

    public static string? FindDuplicateSku(IReadOnlyList<ProductAuthoringVariant> variants)
    {
        ArgumentNullException.ThrowIfNull(variants);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in variants)
        {
            var sku = variant.Sku?.Trim();
            if (string.IsNullOrEmpty(sku))
            {
                continue;
            }

            if (!seen.Add(sku))
            {
                return sku;
            }
        }

        return null;
    }

    public static string? FindDuplicateBarcode(IReadOnlyList<ProductAuthoringVariant> variants)
    {
        ArgumentNullException.ThrowIfNull(variants);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in variants)
        {
            var barcode = variant.Barcode?.Trim();
            if (string.IsNullOrEmpty(barcode))
            {
                continue;
            }

            if (!seen.Add(barcode))
            {
                return barcode;
            }
        }

        return null;
    }
}
