using PointOfSale.Core.Helpers;
using Xunit;

namespace PointOfSale.Tests.Unit.Helpers;

/// <summary>
/// WAVE 1 — Product slug authority tests.
/// Locks the deterministic server-side slug contract: normalization,
/// name-derived generation (including Arabic-only and empty names),
/// tenant-scoped collision handling, and determinism (no random values).
/// </summary>
public class ProductSlugAuthorityTests
{
    #region Normalize

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("Dell Latitude 7420", "dell-latitude-7420")]
    [InlineData("  Blue Widget XL  ", "blue-widget-xl")]
    [InlineData("UPPER-CASE_Mixed  Spaces", "upper-case-mixed-spaces")]
    [InlineData("a---b___c", "a-b-c")]
    [InlineData("!!!", null)]
    [InlineData("منتج عربي", null)]
    [InlineData("Café au lait 500ml!", "caf-au-lait-500ml")]
    public void Normalize_ReturnsExpectedCanonicalSlug(string? input, string? expected)
    {
        Assert.Equal(expected, ProductSlugAuthority.Normalize(input));
    }

    [Fact]
    public void Normalize_TruncatesToMaxLength()
    {
        var input = new string('a', ProductSlugAuthority.MaxLength + 50);

        var result = ProductSlugAuthority.Normalize(input);

        Assert.NotNull(result);
        Assert.True(result.Length <= ProductSlugAuthority.MaxLength);
    }

    [Fact]
    public void Normalize_NeverThrows()
    {
        var inputs = new string?[] { null, "", " ", "!!!", "---", "منتج", new string('x', 5000) };

        foreach (var input in inputs)
        {
            var exception = Record.Exception(() => ProductSlugAuthority.Normalize(input));
            Assert.Null(exception);
        }
    }

    #endregion

    #region GenerateBase

    [Fact]
    public void GenerateBase_PrefersExplicitSlugOverName()
    {
        var result = ProductSlugAuthority.GenerateBase("Custom Slug!", "Dell Latitude 7420", Guid.NewGuid());

        Assert.Equal("custom-slug", result);
    }

    [Fact]
    public void GenerateBase_DerivesFromNameWhenSlugMissing()
    {
        var result = ProductSlugAuthority.GenerateBase(null, "Dell Latitude 7420", Guid.NewGuid());

        Assert.Equal("dell-latitude-7420", result);
    }

    [Fact]
    public void GenerateBase_FallsBackToDeterministicProductIdSlugForArabicOnlyName()
    {
        var productId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var first = ProductSlugAuthority.GenerateBase(null, "منتج عربي", productId);
        var second = ProductSlugAuthority.GenerateBase("", "منتج عربي", productId);

        Assert.Equal("product-11111111", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GenerateBase_FallsBackToDeterministicProductIdSlugForEmptyName()
    {
        var productId = Guid.NewGuid();

        var result = ProductSlugAuthority.GenerateBase(null, "  ", productId);

        Assert.StartsWith("product-", result);
        Assert.Equal(ProductSlugAuthority.GenerateBase(null, null, productId), result);
    }

    #endregion

    #region EnsureUnique

    [Fact]
    public void EnsureUnique_ReturnsBaseWhenFree()
    {
        var result = ProductSlugAuthority.EnsureUnique("dell-latitude-7420", _ => false);

        Assert.Equal("dell-latitude-7420", result);
    }

    [Fact]
    public void EnsureUnique_AppendsSequenceOnCollision()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dell-latitude-7420" };

        var second = ProductSlugAuthority.EnsureUnique("dell-latitude-7420", taken.Contains);
        taken.Add(second);
        var third = ProductSlugAuthority.EnsureUnique("dell-latitude-7420", taken.Contains);

        Assert.Equal("dell-latitude-7420-2", second);
        Assert.Equal("dell-latitude-7420-3", third);
    }

    [Fact]
    public void EnsureUnique_IsCaseInsensitive()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Dell-Latitude-7420" };

        var result = ProductSlugAuthority.EnsureUnique("dell-latitude-7420", taken.Contains);

        Assert.Equal("dell-latitude-7420-2", result);
    }

    [Fact]
    public void EnsureUnique_RespectsMaxLengthWithSuffix()
    {
        var baseSlug = new string('a', ProductSlugAuthority.MaxLength);

        var result = ProductSlugAuthority.EnsureUnique(baseSlug, s => s == baseSlug);

        Assert.True(result.Length <= ProductSlugAuthority.MaxLength);
        Assert.EndsWith("-2", result);
    }

    #endregion
}
