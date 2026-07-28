using System.Reflection;
using System.Text.Json;
using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Constants;

public sealed record StorefrontThemeDefinition(
    string Id,
    string Name,
    string Version,
    IReadOnlySet<string> Capabilities,
    StoreThemeConfig DefaultThemeConfig);

public static class StorefrontThemes
{
    private const string ThemeAuthorityResourceName = "shopixy-theme-authority.json";
    private static readonly ThemeAuthorityDocument Authority = LoadAuthority();

    public static readonly string Default = Authority.DefaultThemeId;

    public static readonly IReadOnlyList<StorefrontThemeDefinition> All = BuildDefinitions(Authority);

    private static readonly HashSet<string> Supported = BuildSupportedSet();

    static StorefrontThemes()
    {
        if (All.Count == 0)
        {
            throw new InvalidOperationException("At least one storefront theme must be registered.");
        }

        var duplicate = All
            .GroupBy(theme => theme.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate != null)
        {
            throw new InvalidOperationException($"Duplicate storefront theme id registered: '{duplicate.Key}'.");
        }

        if (All.Count(theme => string.Equals(theme.Id, Default, StringComparison.OrdinalIgnoreCase)) != 1)
        {
            throw new InvalidOperationException($"Exactly one default storefront theme '{Default}' must be registered.");
        }

        foreach (var theme in All)
        {
            if (string.IsNullOrWhiteSpace(theme.Id) ||
                string.IsNullOrWhiteSpace(theme.Name) ||
                string.IsNullOrWhiteSpace(theme.Version) ||
                theme.Capabilities.Count == 0 ||
                theme.DefaultThemeConfig is null)
            {
                throw new InvalidOperationException($"Storefront theme '{theme.Id}' has an invalid manifest.");
            }
        }
    }

    public static bool IsSupported(string? themeId) =>
        !string.IsNullOrWhiteSpace(themeId) && Supported.Contains(themeId.Trim());

    public static string NormalizeOrDefault(string? themeId) =>
        IsSupported(themeId) ? themeId!.Trim().ToLowerInvariant() : Default;

    public static StorefrontThemeDefinition GetDefinition(string? themeId)
    {
        var normalizedThemeId = NormalizeOrDefault(themeId);
        return All.Single(theme => string.Equals(theme.Id, normalizedThemeId, StringComparison.OrdinalIgnoreCase));
    }

    public static StoreThemeConfig GetThemeConfig(string? themeId)
    {
        return CloneThemeConfig(GetDefinition(themeId).DefaultThemeConfig);
    }

    private static IReadOnlyList<StorefrontThemeDefinition> BuildDefinitions(ThemeAuthorityDocument authority)
    {
        return authority.Themes
            .Where(theme =>
                string.Equals(theme.OfficialStatus, "official", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(theme.ActivationStatus, "active", StringComparison.OrdinalIgnoreCase) &&
                theme.BackendAvailability)
            .Select(theme => new StorefrontThemeDefinition(
                theme.Id,
                theme.DisplayName,
                theme.Version,
                new HashSet<string>(theme.Capabilities, StringComparer.OrdinalIgnoreCase),
                ToStoreThemeConfig(theme.DefaultThemeConfig)))
            .ToArray();
    }

    private static HashSet<string> BuildSupportedSet()
    {
        return All
            .Select(theme => theme.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static ThemeAuthorityDocument LoadAuthority()
    {
        var assembly = typeof(StorefrontThemes).Assembly;
        using var stream = assembly.GetManifestResourceStream(ThemeAuthorityResourceName)
            ?? throw new InvalidOperationException($"Canonical theme authority resource '{ThemeAuthorityResourceName}' was not found.");

        var authority = JsonSerializer.Deserialize<ThemeAuthorityDocument>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (authority is null || string.IsNullOrWhiteSpace(authority.DefaultThemeId) || authority.Themes.Count == 0)
        {
            throw new InvalidOperationException("Canonical theme authority manifest is invalid.");
        }

        return authority;
    }

    private static StoreThemeConfig ToStoreThemeConfig(ThemeConfigManifest source)
    {
        return new StoreThemeConfig
        {
            Colors = new ColorsConfig
            {
                Primary = source.Colors.Primary,
                Secondary = source.Colors.Secondary,
                Background = source.Colors.Background,
                Text = source.Colors.Text,
                Muted = source.Colors.Muted
            },
            Font = new FontConfig
            {
                Family = source.Font.Family,
                BaseSize = source.Font.BaseSize
            },
            Ui = new UiConfig
            {
                BorderRadius = source.Ui.BorderRadius,
                ButtonStyle = source.Ui.ButtonStyle
            },
            Layout = new LayoutConfig
            {
                ProductCardVariant = source.Layout.ProductCardVariant,
                HeaderVariant = source.Layout.HeaderVariant
            }
        };
    }

    private static StoreThemeConfig CloneThemeConfig(StoreThemeConfig source)
    {
        return new StoreThemeConfig
        {
            Colors = new ColorsConfig
            {
                Primary = source.Colors.Primary,
                Secondary = source.Colors.Secondary,
                Background = source.Colors.Background,
                Text = source.Colors.Text,
                Muted = source.Colors.Muted
            },
            Font = new FontConfig
            {
                Family = source.Font.Family,
                BaseSize = source.Font.BaseSize
            },
            Ui = new UiConfig
            {
                BorderRadius = source.Ui.BorderRadius,
                ButtonStyle = source.Ui.ButtonStyle
            },
            Layout = new LayoutConfig
            {
                ProductCardVariant = source.Layout.ProductCardVariant,
                HeaderVariant = source.Layout.HeaderVariant
            }
        };
    }

    private sealed record ThemeAuthorityDocument(
        string DefaultThemeId,
        IReadOnlyList<ThemeAuthorityEntry> Themes);

    private sealed record ThemeAuthorityEntry(
        string Id,
        string DisplayName,
        string Version,
        string OfficialStatus,
        string ActivationStatus,
        bool BackendAvailability,
        IReadOnlyList<string> Capabilities,
        ThemeConfigManifest DefaultThemeConfig);

    private sealed record ThemeConfigManifest(
        ThemeColorsManifest Colors,
        ThemeFontManifest Font,
        ThemeUiManifest Ui,
        ThemeLayoutManifest Layout);

    private sealed record ThemeColorsManifest(
        string Primary,
        string Secondary,
        string Background,
        string Text,
        string Muted);

    private sealed record ThemeFontManifest(
        string Family,
        string BaseSize);

    private sealed record ThemeUiManifest(
        string BorderRadius,
        string ButtonStyle);

    private sealed record ThemeLayoutManifest(
        string ProductCardVariant,
        string HeaderVariant);
}
