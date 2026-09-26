using PointOfSale.Core.Constants;

namespace PointOfSale.Tests.Storefront;

public sealed class HomepageHeroHighlightContractTests
{
    [Fact]
    public void OlderHeroDraftWithoutHighlightsRemainsAdmissible()
    {
        var settings = StorefrontSectionContracts.All.Single(section => section.Type == "hero-banner")
            .DefaultSettings.ToDictionary(entry => entry.Key, entry => entry.Value);
        settings.Remove("highlights");

        Assert.True(StorefrontSectionContracts.TryValidateSettingsStrict("hero-banner", settings, out var diagnostics),
            string.Join(" | ", diagnostics));
    }

    [Fact]
    public void CuratedProductsAcceptBackendGeneratedGuidWithoutUuidVersionBits()
    {
        var settings = StorefrontSectionContracts.All.Single(section => section.Type == "featured-products")
            .DefaultSettings.ToDictionary(entry => entry.Key, entry => entry.Value);
        settings["source"] = "curated";
        settings["productIds"] = new[] { "cc20abdf-0a4f-0278-3c59-c4f10d17bc96" };

        Assert.True(StorefrontSectionContracts.TryValidateSettingsStrict("featured-products", settings, out var diagnostics),
            string.Join(" | ", diagnostics));
    }
}
