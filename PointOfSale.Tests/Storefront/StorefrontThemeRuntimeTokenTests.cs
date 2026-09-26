using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using System.Text.Json;

namespace PointOfSale.Tests.Storefront;

public sealed class StorefrontThemeRuntimeTokenTests
{
    [Fact]
    public void RuntimeThemeRegistry_EveryThemeExposesCanonicalThemeConfig()
    {
        var ids = StorefrontThemes.All.Select(theme => theme.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var theme in StorefrontThemes.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(theme.DefaultThemeConfig.Colors.Primary));
            Assert.False(string.IsNullOrWhiteSpace(theme.DefaultThemeConfig.Colors.Secondary));
            Assert.False(string.IsNullOrWhiteSpace(theme.DefaultThemeConfig.Colors.Background));
            Assert.False(string.IsNullOrWhiteSpace(theme.DefaultThemeConfig.Colors.Text));
            Assert.False(string.IsNullOrWhiteSpace(theme.DefaultThemeConfig.Colors.Muted));
            Assert.False(string.IsNullOrWhiteSpace(theme.DefaultThemeConfig.Font.Family));
            Assert.False(string.IsNullOrWhiteSpace(theme.DefaultThemeConfig.Font.BaseSize));
            Assert.False(string.IsNullOrWhiteSpace(theme.DefaultThemeConfig.Ui.BorderRadius));
            Assert.Contains("layout.header", theme.Capabilities);
            Assert.Contains("layout.footer", theme.Capabilities);
        }
    }

    [Fact]
    public void BackendThemeRegistry_IsDerivedFromCanonicalThemeAuthority()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var manifestPath = Path.Combine(root, "theme-authority", "shopixy-theme-authority.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var rootElement = document.RootElement;
        var expectedIds = rootElement
            .GetProperty("themes")
            .EnumerateArray()
            .Where(theme =>
                theme.GetProperty("officialStatus").GetString() == "official" &&
                theme.GetProperty("activationStatus").GetString() == "active" &&
                theme.GetProperty("backendAvailability").GetBoolean())
            .Select(theme => theme.GetProperty("id").GetString())
            .ToArray();

        Assert.Equal(rootElement.GetProperty("defaultThemeId").GetString(), StorefrontThemes.Default);
        Assert.Equal(expectedIds, StorefrontThemes.All.Select(theme => theme.Id).ToArray());
        Assert.DoesNotContain("electronics", StorefrontThemes.All.Select(theme => theme.Id));

        foreach (var theme in StorefrontThemes.All)
        {
            var manifestTheme = rootElement
                .GetProperty("themes")
                .EnumerateArray()
                .Single(candidate => candidate.GetProperty("id").GetString() == theme.Id);
            var colors = manifestTheme.GetProperty("defaultThemeConfig").GetProperty("colors");

            Assert.Equal(manifestTheme.GetProperty("displayName").GetString(), theme.Name);
            Assert.Equal(manifestTheme.GetProperty("version").GetString(), theme.Version);
            Assert.Equal(colors.GetProperty("primary").GetString(), theme.DefaultThemeConfig.Colors.Primary);
            Assert.Equal(colors.GetProperty("background").GetString(), theme.DefaultThemeConfig.Colors.Background);
        }
    }

    [Theory]
    [InlineData("modern", "#2563eb", "#ffffff", "Inter", "16px", "8px")]
    [InlineData("pearl", "#DC2626", "#FBFAF7", "'Jost', sans-serif", "15px", "0px")]
    [InlineData("phoenix", "#cd1515", "#ece9e2", "'Jost', sans-serif", "15px", "0px")]
    [InlineData("hype", "#F06736", "#FFFFFF", "'Jost', sans-serif", "16px", "8px")]
    [InlineData("fashion", "#9D174D", "#ffffff", "Inter", "16px", "0px")]
    [InlineData("minimal", "#0f172a", "#ffffff", "Inter", "15px", "6px")]
    public void ThemeDefaultsApplyWhenTenantOverridesAreAbsent(
        string storefrontTheme,
        string expectedPrimary,
        string expectedBackground,
        string expectedFont,
        string expectedBaseSize,
        string expectedBorderRadius)
    {
        var runtime = StorefrontDraftConfigMapper.MapLive(
            CreateConfig(storefrontTheme),
            themePreset: null,
            currency: "EGP",
            defaultCountryCode: "EG");

        Assert.Equal(storefrontTheme, runtime.StorefrontTheme);
        Assert.Equal(expectedPrimary, runtime.Theme.Colors.Primary);
        Assert.Equal(expectedBackground, runtime.Theme.Colors.Background);
        Assert.Equal(expectedFont, runtime.Theme.Font.Family);
        Assert.Equal(expectedBaseSize, runtime.Theme.Font.BaseSize);
        Assert.Equal(expectedBorderRadius, runtime.Theme.Ui.BorderRadius);
    }

    [Fact]
    public void ThemeSwitchSequence_ChangesCssVariableProjection()
    {
        var modern = CssVariables(StorefrontDraftConfigMapper.MapLive(CreateConfig("modern"), null, "EGP", "EG").Theme);
        var pearl = CssVariables(StorefrontDraftConfigMapper.MapLive(CreateConfig("pearl"), null, "EGP", "EG").Theme);
        var fashion = CssVariables(StorefrontDraftConfigMapper.MapLive(CreateConfig("fashion"), null, "EGP", "EG").Theme);
        var minimal = CssVariables(StorefrontDraftConfigMapper.MapLive(CreateConfig("minimal"), null, "EGP", "EG").Theme);

        AssertVariablesChanged(modern, pearl);
        AssertVariablesChanged(pearl, fashion);
        AssertVariablesChanged(fashion, minimal);
    }

    [Fact]
    public void TenantOverridesTakePrecedenceOverThemeDefaults()
    {
        var config = CreateConfig(
            "pearl",
            themeLiveOverridesJson: """
            {
              "colors": {
                "primary": "#123456",
                "background": "#abcdef"
              },
              "font": "Cairo",
              "borderRadius": 22
            }
            """);

        var runtime = StorefrontDraftConfigMapper.MapLive(config, null, "EGP", "EG");

        Assert.Equal("#123456", runtime.Theme.Colors.Primary);
        Assert.Equal("#abcdef", runtime.Theme.Colors.Background);
        Assert.Equal("Cairo", runtime.Theme.Font.Family);
        Assert.Equal("22px", runtime.Theme.Ui.BorderRadius);
        Assert.Equal("#C9A863", runtime.Theme.Colors.Secondary);
        Assert.Equal("#131316", runtime.Theme.Colors.Text);
    }

    [Fact]
    public void TenantAppearanceOverridesApplyAcrossEveryActiveOfficialRuntimeTheme()
    {
        Assert.Equal(10, StorefrontThemes.All.Count);

        foreach (var theme in StorefrontThemes.All)
        {
            var config = CreateConfig(
                theme.Id,
                themeLiveOverridesJson: """
                {
                  "colors": {
                    "primary": "#123456",
                    "secondary": "#234567",
                    "background": "#fefefe",
                    "text": "#101112",
                    "muted": "#787878"
                  },
                  "typography": {
                    "fontFamily": "Cairo",
                    "baseSize": "19px"
                  },
                  "borderRadius": 13
                }
                """);

            var runtime = StorefrontDraftConfigMapper.MapLive(config, null, "EGP", "EG");

            Assert.Equal(theme.Id, runtime.StorefrontTheme);
            Assert.Equal("#123456", runtime.Theme.Colors.Primary);
            Assert.Equal("#234567", runtime.Theme.Colors.Secondary);
            Assert.Equal("#fefefe", runtime.Theme.Colors.Background);
            Assert.Equal("#101112", runtime.Theme.Colors.Text);
            Assert.Equal("#787878", runtime.Theme.Colors.Muted);
            Assert.Equal("Cairo", runtime.Theme.Font.Family);
            Assert.Equal("19px", runtime.Theme.Font.BaseSize);
            Assert.Equal("13px", runtime.Theme.Ui.BorderRadius);
        }
    }

    [Fact]
    public void DraftAppearanceDoesNotLeakIntoLiveProjection()
    {
        var config = CreateConfig("modern", """{"colors":{"primary":"#111111"},"typography":{"fontFamily":"Inter","baseSize":"16px"}}""");
        config.ThemeDraftOverridesJson = """{"colors":{"primary":"#abcdef"},"typography":{"fontFamily":"Cairo","baseSize":"22px"}}""";

        var live = StorefrontDraftConfigMapper.MapLive(config, null, "EGP", "EG");
        var draft = StorefrontDraftConfigMapper.Map(config, null, "EGP", "EG");

        Assert.Equal("#111111", live.Theme.Colors.Primary);
        Assert.Equal("Inter", live.Theme.Font.Family);
        Assert.Equal("16px", live.Theme.Font.BaseSize);
        Assert.Equal("#abcdef", draft.Theme.Colors.Primary);
        Assert.Equal("Cairo", draft.Theme.Font.Family);
        Assert.Equal("22px", draft.Theme.Font.BaseSize);
    }

    [Fact]
    public void LegacyStoreThemeCustomizationsRemainTenantOverrides()
    {
        var config = CreateConfig("fashion");
        config.Theme = new StoreThemeConfig
        {
            Colors = new ColorsConfig
            {
                Primary = "#654321",
                Secondary = new ColorsConfig().Secondary,
                Background = new ColorsConfig().Background,
                Text = new ColorsConfig().Text,
                Muted = new ColorsConfig().Muted
            },
            Font = new FontConfig
            {
                Family = "Tajawal",
                BaseSize = new FontConfig().BaseSize
            },
            Ui = new UiConfig
            {
                BorderRadius = new UiConfig().BorderRadius,
                ButtonStyle = new UiConfig().ButtonStyle
            },
            Layout = new LayoutConfig
            {
                ProductCardVariant = new LayoutConfig().ProductCardVariant,
                HeaderVariant = new LayoutConfig().HeaderVariant
            }
        };

        var runtime = StorefrontDraftConfigMapper.MapLive(config, null, "EGP", "EG");

        Assert.Equal("#654321", runtime.Theme.Colors.Primary);
        Assert.Equal("Tajawal", runtime.Theme.Font.Family);
        Assert.Equal("#ffffff", runtime.Theme.Colors.Background);
        Assert.Equal("16px", runtime.Theme.Font.BaseSize);
        Assert.Equal("0px", runtime.Theme.Ui.BorderRadius);
    }

    [Fact]
    public void RuntimeMapperUsesStorefrontThemeRegistryWithoutThemeIdSwitches()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var mapper = File.ReadAllText(Path.Combine(root, "PointOfSale.API", "Services", "Storefront", "StorefrontDraftConfigMapper.cs"));
        var registry = File.ReadAllText(Path.Combine(root, "PointOfSale.Core", "Constants", "StorefrontThemes.cs"));

        Assert.Contains("StorefrontThemes.GetThemeConfig", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("switch (storefrontTheme", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("switch (activatedTheme", mapper, StringComparison.Ordinal);
        Assert.Contains("StoreThemeConfig DefaultThemeConfig", registry, StringComparison.Ordinal);
        Assert.Equal(2, registry.Split("public static readonly IReadOnlyList<StorefrontThemeDefinition> All").Length);
    }

    private static StoreConfig CreateConfig(string storefrontTheme, string themeLiveOverridesJson = "{}")
    {
        return new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            StoreName = "Runtime Token Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            ThemeLivePresetKey = "default",
            ThemeDraftOverridesJson = "{}",
            ThemeLiveOverridesJson = themeLiveOverridesJson,
            CustomizationJson = $$"""{"storefrontTheme":"{{storefrontTheme}}"}"""
        };
    }

    private static Dictionary<string, string> CssVariables(StorefrontThemeConfigDto theme)
    {
        return new Dictionary<string, string>
        {
            ["--color-primary"] = theme.Colors.Primary,
            ["--color-secondary"] = theme.Colors.Secondary,
            ["--color-background"] = theme.Colors.Background,
            ["--color-text"] = theme.Colors.Text,
            ["--color-muted"] = theme.Colors.Muted,
            ["--font-family"] = theme.Font.Family,
            ["--font-family-ar"] = $"arabic-stack, {theme.Font.Family}",
            ["--font-base-size"] = theme.Font.BaseSize,
            ["--border-radius"] = theme.Ui.BorderRadius
        };
    }

    private static void AssertVariablesChanged(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        Assert.Equal(before.Keys.OrderBy(key => key), after.Keys.OrderBy(key => key));
        Assert.Contains(before.Keys, variable => before[variable] != after[variable]);
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
