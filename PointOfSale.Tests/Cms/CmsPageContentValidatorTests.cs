using System.Text.Json;
using PointOfSale.API.Services.Cms;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.Cms;

public sealed class CmsPageContentValidatorTests
{
    private readonly CmsPageContentValidator _validator = new();

    [Fact]
    public void ValidateAndNormalize_RichText_RemovesDangerousMarkup()
    {
        var content = JsonSerializer.SerializeToElement(
            "<div><script>alert(1)</script><img src=x onerror=alert(1) /><a href=\"javascript:alert(1)\">Click</a><p>Safe</p></div>");

        var result = _validator.ValidateAndNormalize(CmsPageContentType.RichText, content, schemaVersion: 1);

        Assert.DoesNotContain("<script", result.NormalizedContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", result.NormalizedContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", result.NormalizedContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", result.NormalizedContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>Safe</p>", result.NormalizedContent, StringComparison.Ordinal);
        Assert.Contains(">Click</a>", result.NormalizedContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateAndNormalize_RichText_PreservesSafeLinksAndHardensBlankTargets()
    {
        var content = JsonSerializer.SerializeToElement(
            "<p><a href=\"/policies/privacy\" target=\"_blank\">Privacy Policy</a></p>");

        var result = _validator.ValidateAndNormalize(CmsPageContentType.RichText, content, schemaVersion: 1);

        Assert.Contains("href=\"/policies/privacy\"", result.NormalizedContent, StringComparison.Ordinal);
        Assert.Contains("target=\"_blank\"", result.NormalizedContent, StringComparison.Ordinal);
        
        // Verify rel attribute contains both required tokens regardless of order
        Assert.Matches(@"rel=""[^""]*\bnoopener\b[^""]*""", result.NormalizedContent);
        Assert.Matches(@"rel=""[^""]*\bnoreferrer\b[^""]*""", result.NormalizedContent);
    }

    [Fact]
    public void ValidateAndNormalize_Blocks_NormalizesSupportedBlocks()
    {
        var content = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                type = "TEXT",
                data = new
                {
                    text = "  Hello world  ",
                    alignment = "Center"
                }
            },
            new
            {
                type = "faq",
                data = new
                {
                    items = new[]
                    {
                        new
                        {
                            question = " What is Shopixy? ",
                            answer = " A marketplace platform. "
                        }
                    }
                }
            }
        });

        var result = _validator.ValidateAndNormalize(CmsPageContentType.Blocks, content, schemaVersion: 2);
        var blocks = JsonSerializer.Deserialize<JsonElement>(result.NormalizedContent);
        var blockItems = blocks.EnumerateArray().ToArray();

        Assert.Equal(2, result.SchemaVersion);
        Assert.Equal(JsonValueKind.Array, blocks.ValueKind);
        Assert.Equal("text", blockItems[0].GetProperty("type").GetString());
        Assert.Equal("Hello world", blockItems[0].GetProperty("data").GetProperty("text").GetString());
        Assert.Equal("center", blockItems[0].GetProperty("data").GetProperty("alignment").GetString());
        Assert.Equal("What is Shopixy?", blockItems[1].GetProperty("data").GetProperty("items").EnumerateArray().First().GetProperty("question").GetString());
    }

    [Fact]
    public void ValidateAndNormalize_Blocks_RejectsInvalidShape()
    {
        var content = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                type = "faq",
                data = new
                {
                    items = new[]
                    {
                        new
                        {
                            question = "Missing answer"
                        }
                    }
                }
            }
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => _validator.ValidateAndNormalize(CmsPageContentType.Blocks, content, schemaVersion: 1));

        Assert.Contains("question and answer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAndNormalize_Blocks_RejectsUnknownWidgetJson()
    {
        var content = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                type = "custom-widget",
                data = new
                {
                    html = "<script>alert(1)</script>",
                    widgetAuthority = true,
                    mediaId = "forged"
                }
            }
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => _validator.ValidateAndNormalize(CmsPageContentType.Blocks, content, schemaVersion: 1));

        Assert.Contains("not supported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.test/image.png")]
    [InlineData("https://user:pass@cdn.example.com/image.png")]
    public void ValidateAndNormalize_Blocks_RejectsUnsafeImageUrls(string imageUrl)
    {
        var content = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                type = "image",
                data = new
                {
                    imageUrl,
                    alt = "Example image"
                }
            }
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => _validator.ValidateAndNormalize(CmsPageContentType.Blocks, content, schemaVersion: 1));

        Assert.Contains("safe relative path or an http(s) URL", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAndNormalize_Blocks_StripsUnknownFieldsFromSupportedBlocks()
    {
        var content = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                type = "image",
                data = new
                {
                    imageUrl = "/media/cms/hero.png",
                    alt = " Hero ",
                    caption = " Caption ",
                    html = "<script>alert(1)</script>",
                    widgetAuthority = true
                }
            }
        });

        var result = _validator.ValidateAndNormalize(CmsPageContentType.Blocks, content, schemaVersion: 1);

        Assert.Contains("\"imageUrl\":\"/media/cms/hero.png\"", result.NormalizedContent, StringComparison.Ordinal);
        Assert.DoesNotContain("widgetAuthority", result.NormalizedContent, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", result.NormalizedContent, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("About Us", "about-us")]
    [InlineData("  PRIVACY_policy  ", "privacy-policy")]
    [InlineData("contact---page", "contact-page")]
    public void NormalizeSlug_NormalizesExpectedValues(string input, string expected)
    {
        var normalized = CmsPageReadService.NormalizeSlug(input);

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("---")]
    [InlineData("___")]
    public void NormalizeSlug_RejectsValuesWithoutUsableCharacters(string input)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CmsPageReadService.NormalizeSlug(input));

        Assert.Contains("letter or number", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
