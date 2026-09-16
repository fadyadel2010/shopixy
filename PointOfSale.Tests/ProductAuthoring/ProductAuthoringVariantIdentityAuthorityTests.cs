using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Helpers;

namespace PointOfSale.Tests.ProductAuthoring;

/// <summary>
/// Wave 2 variant-persistence / SKU-integrity contracts.
///
/// The intra-request authority must reject duplicate attribute combinations,
/// SKUs, and barcodes deterministically (server-side, before persistence) so
/// multi-variant saves can never persist two variants under one identity.
/// </summary>
public sealed class ProductAuthoringVariantIdentityAuthorityTests
{
    private static ProductAuthoringVariant MatrixVariant(
        string? sku,
        string? barcode,
        params (string Name, string Value)[] attributes) =>
        new(
            VariantId: null,
            Sku: sku,
            Barcode: barcode,
            Price: 10m,
            OnlinePrice: null,
            CostPrice: 5m,
            IsActive: true,
            IsDefault: false,
            Attributes: attributes.ToDictionary(a => a.Name, a => a.Value));

    [Fact]
    public void DuplicateCombination_AcrossAttributeOrdering_IsDetected()
    {
        var variants = new[]
        {
            MatrixVariant("SKU-1", null, ("Style", "Plain")),
            MatrixVariant("SKU-2", null, ("Style", "Medium")),
            // Same canonical combination as row 1, different key order/casing.
            MatrixVariant("SKU-3", null, ("STYLE", "  plain ")),
        };

        Assert.NotNull(ProductAuthoringVariantIdentityAuthority.FindDuplicateCombinationKey(variants));
    }

    [Fact]
    public void DistinctCombinations_AreNotFlagged()
    {
        var variants = new[]
        {
            MatrixVariant("SKU-1", null, ("Style", "Plain")),
            MatrixVariant("SKU-2", null, ("Style", "Medium")),
            MatrixVariant("SKU-3", null, ("Style", "Mojahed")),
        };

        Assert.Null(ProductAuthoringVariantIdentityAuthority.FindDuplicateCombinationKey(variants));
    }

    [Fact]
    public void MultiGroupCombination_Ordering_DoesNotDuplicate()
    {
        var variants = new[]
        {
            MatrixVariant("SKU-1", null, ("Color", "Red"), ("Size", "M")),
            MatrixVariant("SKU-2", null, ("Size", "M"), ("Color", "Red")),
        };

        // "Plain + Medium" vs "Medium + Plain" canonicalize identically, so the
        // second row is a duplicate of the first — never a new variant.
        Assert.NotNull(ProductAuthoringVariantIdentityAuthority.FindDuplicateCombinationKey(variants));
    }

    [Fact]
    public void CanonicalKey_IsOrderIndependent_AndSeparatesDistinctCombos()
    {
        var plainMedium = ProductVariantAttributeCombinationKey.Compute(
            new[] { ("Style", "Plain"), ("Finish", "Medium") });
        var mediumPlain = ProductVariantAttributeCombinationKey.Compute(
            new[] { ("Finish", "Medium"), ("Style", "Plain") });
        var mojahed = ProductVariantAttributeCombinationKey.Compute(
            new[] { ("Style", "Mojahed"), ("Finish", "Medium") });

        Assert.NotNull(plainMedium);
        Assert.Equal(plainMedium, mediumPlain);
        Assert.NotEqual(plainMedium, mojahed);
    }

    [Fact]
    public void DuplicateSku_WithinOneSave_IsDetected()
    {
        var variants = new[]
        {
            MatrixVariant("W2-001", null, ("Style", "Plain")),
            MatrixVariant("W2-002", null, ("Style", "Medium")),
            MatrixVariant("W2-001", null, ("Style", "Mojahed")),
        };

        Assert.Equal("W2-001", ProductAuthoringVariantIdentityAuthority.FindDuplicateSku(variants));
    }

    [Fact]
    public void UniqueSkus_AreNotFlagged_AndNullsAreUnlimited()
    {
        var variants = new[]
        {
            MatrixVariant("W2-001", null, ("Style", "Plain")),
            MatrixVariant("W2-002", null, ("Style", "Medium")),
            MatrixVariant(null, null, ("Style", "Dark")),
            MatrixVariant("", null, ("Style", "Mojahed")),
            MatrixVariant("   ", null, ("Style", "Raw")),
        };

        Assert.Null(ProductAuthoringVariantIdentityAuthority.FindDuplicateSku(variants));
    }

    [Fact]
    public void SkuComparison_UsesOrdinalTenantContract()
    {
        // The database unique index is case-sensitive: "ABC" and "abc" are
        // distinct persisted identities. The frontend blocks case-insensitive
        // collisions earlier; the server enforces the actual stored contract.
        var variants = new[]
        {
            MatrixVariant("ABC-1", null, ("Style", "Plain")),
            MatrixVariant("abc-1", null, ("Style", "Medium")),
        };

        Assert.Null(ProductAuthoringVariantIdentityAuthority.FindDuplicateSku(variants));
    }

    [Fact]
    public void DuplicateBarcode_WithinOneSave_IsDetected()
    {
        var variants = new[]
        {
            MatrixVariant("W2-001", "BC-1", ("Style", "Plain")),
            MatrixVariant("W2-002", "BC-1", ("Style", "Medium")),
        };

        Assert.Equal("BC-1", ProductAuthoringVariantIdentityAuthority.FindDuplicateBarcode(variants));
    }

    [Fact]
    public void EmptyMatrix_HasNoDuplicates()
    {
        Assert.Null(ProductAuthoringVariantIdentityAuthority.FindDuplicateCombinationKey([]));
        Assert.Null(ProductAuthoringVariantIdentityAuthority.FindDuplicateSku([]));
        Assert.Null(ProductAuthoringVariantIdentityAuthority.FindDuplicateBarcode([]));
    }
}
