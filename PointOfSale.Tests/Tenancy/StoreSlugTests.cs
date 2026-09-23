using PointOfSale.Core.Tenancy;

namespace PointOfSale.Tests.Tenancy;

public sealed class StoreSlugTests
{
    [Theory]
    [InlineData("shopixy", "shopixy")]
    [InlineData("shopixy2", "shopixy2")]
    [InlineData("shopixy-store", "shopixy-store")]
    [InlineData("123", "123")]
    [InlineData(" SHOPIXY ", "shopixy")]
    public void TryNormalize_AcceptsCanonicalHostLabels(string candidate, string expected)
    {
        Assert.True(StoreSlug.TryNormalize(candidate, out var normalized, out var error), error);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("متجر")]
    [InlineData("my store")]
    [InlineData("my_store")]
    [InlineData("my.store")]
    [InlineData("my/store")]
    [InlineData("my\\store")]
    [InlineData("my@store")]
    [InlineData("my:store")]
    [InlineData("my?store")]
    [InlineData("my#store")]
    [InlineData("my%20store")]
    [InlineData("-store")]
    [InlineData("store-")]
    [InlineData("my--store")]
    [InlineData("ab")]
    [InlineData("www")]
    [InlineData("abc\r\ndef")]
    public void TryNormalize_RejectsUnsafeOrReservedValues(string candidate)
    {
        Assert.False(StoreSlug.TryNormalize(candidate, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryNormalize_RejectsValueLongerThanDnsLabelLimit()
    {
        Assert.False(StoreSlug.TryNormalize(new string('a', 64), out _, out _));
    }
}
