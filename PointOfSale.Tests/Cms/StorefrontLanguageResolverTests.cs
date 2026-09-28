using Microsoft.Extensions.Primitives;
using PointOfSale.API.Localization;

namespace PointOfSale.Tests.Cms;

public sealed class StorefrontLanguageResolverTests
{
    [Fact]
    public void Resolve_ExplicitLanguageOverridesAcceptLanguage()
    {
        var result = StorefrontLanguageResolver.Resolve(
            "en",
            new StringValues("ar-EG;q=1.0,en;q=0.1"));

        Assert.Equal("en", result);
    }

    [Theory]
    [InlineData("ar-EG;q=0.9,en;q=0.8", "ar")]
    [InlineData("fr;q=1.0,en-US;q=0.7,ar;q=0.6", "en")]
    [InlineData("ar;q=0,en;q=0.5", "en")]
    [InlineData("fr-FR", "en")]
    public void Resolve_UsesWeightedSupportedAcceptLanguage(string header, string expected)
    {
        Assert.Equal(expected, StorefrontLanguageResolver.Resolve(null, new StringValues(header)));
    }

    [Theory]
    [InlineData("ar", "ar")]
    [InlineData("AR-eg", "ar")]
    [InlineData("en-US", "en")]
    [InlineData("unsupported", "en")]
    [InlineData(null, "en")]
    public void Normalize_RestrictsLanguageToSupportedLocales(string? language, string expected)
    {
        Assert.Equal(expected, StorefrontLanguageResolver.Normalize(language));
    }
}
