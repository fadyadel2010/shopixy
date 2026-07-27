namespace PointOfSale.Core.Constants;

public sealed record StorefrontThemeDefinition(
    string Id,
    string Name,
    string Version,
    IReadOnlySet<string> Capabilities);

public static class StorefrontThemes
{
    public const string Default = "modern";

    public static readonly IReadOnlyList<StorefrontThemeDefinition> All =
    [
        new(
            Default,
            "Modern",
            "1.0.0",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "layout.header",
                "layout.footer",
                "section.hero-banner",
                "section.product-grid",
                "section.category-grid"
            }),
        new(
            "minimal",
            "Minimal",
            "1.0.0",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "layout.header",
                "layout.footer",
                "section.hero-banner",
                "section.product-grid",
                "section.category-grid"
            }),
        new(
            "fashion",
            "Fashion",
            "1.0.0",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "layout.header",
                "layout.footer",
                "section.hero-banner",
                "section.product-grid",
                "section.category-grid"
            }),
        new(
            "pearl",
            "Pearl",
            "1.0.0",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "layout.header",
                "layout.footer",
                "section.hero-banner",
                "section.product-grid",
                "section.category-grid"
            })
    ];

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
                theme.Capabilities.Count == 0)
            {
                throw new InvalidOperationException($"Storefront theme '{theme.Id}' has an invalid manifest.");
            }
        }
    }

    public static bool IsSupported(string? themeId) =>
        !string.IsNullOrWhiteSpace(themeId) && Supported.Contains(themeId.Trim());

    public static string NormalizeOrDefault(string? themeId) =>
        IsSupported(themeId) ? themeId!.Trim().ToLowerInvariant() : Default;

    private static HashSet<string> BuildSupportedSet()
    {
        return All
            .Select(theme => theme.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
