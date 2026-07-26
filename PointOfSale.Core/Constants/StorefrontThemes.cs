namespace PointOfSale.Core.Constants;

public static class StorefrontThemes
{
    public const string Default = "modern";

    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        Default,
        "minimal",
        "fashion"
    };

    public static bool IsSupported(string? themeId) =>
        !string.IsNullOrWhiteSpace(themeId) && Supported.Contains(themeId.Trim());

    public static string NormalizeOrDefault(string? themeId) =>
        IsSupported(themeId) ? themeId!.Trim().ToLowerInvariant() : Default;
}
