using System.Reflection;
using System.Text.Json;
using PointOfSale.API.Services.Admin;

namespace PointOfSale.Tests.Architecture;

public sealed class StoreBuilderRuntimeContractCertificationTests
{
    [Fact]
    public void CanonicalSectionContract_MustInventoryEveryConfigurableProperty()
    {
        using var document = JsonDocument.Parse(CertificationSource.ReadRepoFile("theme-authority/shopixy-theme-authority.json"));
        var contracts = document.RootElement.GetProperty("sectionContracts").EnumerateArray().ToArray();

        Assert.NotEmpty(contracts);

        foreach (var contract in contracts)
        {
            var sectionType = contract.GetProperty("type").GetString();
            var defaults = contract.GetProperty("defaultSettings");
            var fields = contract.GetProperty("fields").EnumerateArray().ToArray();
            var fieldKeys = fields.Select(field => field.GetProperty("key").GetString()).ToArray();

            Assert.False(string.IsNullOrWhiteSpace(sectionType));
            Assert.NotEmpty(fields);
            Assert.Equal(fieldKeys.Length, fieldKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            foreach (var field in fields)
            {
                var key = field.GetProperty("key").GetString()!;
                Assert.True(defaults.TryGetProperty(key, out _), $"{sectionType}.{key} must have a canonical default.");
                Assert.False(string.IsNullOrWhiteSpace(field.GetProperty("type").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(field.GetProperty("rendererConsumer").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(field.GetProperty("visualEffect").GetString()));

                if (field.TryGetProperty("options", out var options))
                {
                    Assert.Contains(
                        defaults.GetProperty(key).GetString(),
                        options.EnumerateArray().Select(option => option.GetString()),
                        StringComparer.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void BackendBuilderPersistence_MustDeriveAllowedSettingsFromCanonicalContract()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminStoreConfigService.cs");

        Assert.Contains("StorefrontSectionContracts.SupportedTypes", service, StringComparison.Ordinal);
        Assert.Contains("StorefrontSectionContracts.TryValidateSettingsStrict", service, StringComparison.Ordinal);
        Assert.Contains("StorefrontSectionContracts.GetFields(sectionType)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeSectionSettingValue", service, StringComparison.Ordinal);
        Assert.DoesNotContain("productSource", service, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendPageLayoutMutation_MustPreserveDisplayStyleAndFailContractDrift()
    {
        var normalized = NormalizePage(BuildPage(BuildFeaturedProductsSettings()));

        Assert.Equal("grid", normalized.RootElement
            .GetProperty("sections")[0]
            .GetProperty("settings")
            .GetProperty("displayStyle")
            .GetString());

        var canonicalizedEnum = BuildFeaturedProductsSettings();
        canonicalizedEnum["displayStyle"] = "GRID";
        using var normalizedEnum = NormalizePage(BuildPage(canonicalizedEnum));
        Assert.Equal("grid", normalizedEnum.RootElement
            .GetProperty("sections")[0]
            .GetProperty("settings")
            .GetProperty("displayStyle")
            .GetString());

        var unknown = BuildFeaturedProductsSettings();
        unknown["unexpectedRendererOnlyField"] = true;
        AssertContractFailure(BuildPage(unknown), "not supported");

        var missing = BuildFeaturedProductsSettings();
        missing.Remove("displayStyle");
        AssertContractFailure(BuildPage(missing), "required");

        var enumMismatch = BuildFeaturedProductsSettings();
        enumMismatch["displayStyle"] = "slider";
        AssertContractFailure(BuildPage(enumMismatch), "displayStyle");

        var typeMismatch = BuildFeaturedProductsSettings();
        typeMismatch["maxItems"] = "8";
        AssertContractFailure(BuildPage(typeMismatch), "must be number");

        var rangeMismatch = BuildFeaturedProductsSettings();
        rangeMismatch["maxItems"] = 99;
        AssertContractFailure(BuildPage(rangeMismatch), "above maximum");
    }

    [Fact]
    public void ProductTemplateRuntimeConsumedFields_MustBeCanonicalAndBackendAdmitted()
    {
        var normalized = NormalizePage(new Dictionary<string, object>
        {
            ["templateId"] = "default",
            ["name"] = "Default Template",
            ["sections"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "section_product_hero_contract",
                    ["type"] = "product-hero",
                    ["enabled"] = true,
                    ["settings"] = new Dictionary<string, object?>
                    {
                        ["layoutVariant"] = "fashion"
                    }
                }
            }
        }, authoritativeTemplateId: "default");

        var settings = normalized.RootElement
            .GetProperty("sections")[0]
            .GetProperty("settings");

        Assert.Equal("fashion", settings.GetProperty("layoutVariant").GetString());
        Assert.Equal(1, settings.EnumerateObject().Count());
    }

    private static JsonDocument NormalizePage(
        Dictionary<string, object> page,
        string? authoritativeTemplateId = null)
    {
        var method = typeof(AdminStoreConfigService).GetMethod(
            "NormalizeBuilderPageComposition",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("NormalizeBuilderPageComposition was not found.");

        var result = method.Invoke(null, new object?[] { page, null, authoritativeTemplateId })
            ?? throw new InvalidOperationException("NormalizeBuilderPageComposition returned null.");

        return JsonDocument.Parse(JsonSerializer.Serialize(result));
    }

    private static void AssertContractFailure(Dictionary<string, object> page, string expectedMessageFragment)
    {
        var method = typeof(AdminStoreConfigService).GetMethod(
            "NormalizeBuilderPageComposition",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("NormalizeBuilderPageComposition was not found.");

        var exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null, new object?[] { page, null, null }));

        var inner = Assert.IsType<ArgumentException>(exception.InnerException);
        Assert.Contains(expectedMessageFragment, inner.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object> BuildPage(Dictionary<string, object?> settings) =>
        new()
        {
            ["name"] = "Home",
            ["sections"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "section_existing_display_style_contract",
                    ["type"] = "featured-products",
                    ["enabled"] = true,
                    ["settings"] = settings
                }
            }
        };

    private static Dictionary<string, object?> BuildFeaturedProductsSettings() =>
        new(StringComparer.Ordinal)
        {
            ["title"] = "Featured Products",
            ["subtitle"] = string.Empty,
            ["source"] = "all",
            ["productIds"] = Array.Empty<string>(),
            ["maxItems"] = 8,
            ["displayStyle"] = "grid",
            ["showBadge"] = true,
            ["badgeText"] = "Featured"
        };
}
