using System.Text.Json;
using PointOfSale.API.Services.MarketplaceCms;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.Cms;

public sealed class MarketplaceCmsPageContentValidatorTests
{
    private readonly MarketplaceCmsPageContentValidator _validator = new();

    [Fact]
    public void ValidateAndNormalize_RichText_RemovesStoredXssPayloads()
    {
        var content = JsonSerializer.SerializeToElement(
            "<div><script>alert(1)</script><img src=x onerror=alert(1) /><a href=\"javascript:alert(1)\">click</a><iframe src=\"https://evil.example\"></iframe><p>Safe</p></div>");

        var result = _validator.ValidateAndNormalize(CmsPageContentType.RichText, content);

        Assert.DoesNotContain("<script", result.NormalizedContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", result.NormalizedContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", result.NormalizedContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", result.NormalizedContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", result.NormalizedContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>Safe</p>", result.NormalizedContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateAndNormalize_ImageBlockRejectsUnsafeUrls()
    {
        var content = JsonSerializer.Deserialize<JsonElement>(
            """
            [
              {
                "type": "image",
                "data": {
                  "imageUrl": "javascript:alert(1)",
                  "alt": "Unsafe"
                }
              }
            ]
            """);

        Assert.Throws<InvalidOperationException>(() =>
            _validator.ValidateAndNormalize(CmsPageContentType.Blocks, content));
    }

    [Fact]
    public void ValidateAndNormalize_RejectsUnsupportedBlockTypes()
    {
        var content = JsonSerializer.Deserialize<JsonElement>(
            """
            [
              {
                "type": "dynamic-product-feed",
                "data": {
                  "query": "latest"
                }
              }
            ]
            """);

        Assert.Throws<InvalidOperationException>(() =>
            _validator.ValidateAndNormalize(CmsPageContentType.Blocks, content));
    }
}
