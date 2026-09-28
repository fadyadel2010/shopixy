using System.Globalization;
using Microsoft.Extensions.Primitives;

namespace PointOfSale.API.Localization;

public static class StorefrontLanguageResolver
{
    public const string English = "en";
    public const string Arabic = "ar";

    public static string Resolve(string? explicitLanguage, StringValues acceptLanguage)
    {
        if (!string.IsNullOrWhiteSpace(explicitLanguage))
        {
            return Normalize(explicitLanguage);
        }

        var candidates = acceptLanguage
            .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select((token, index) => ParseCandidate(token, index))
            .Where(candidate => candidate.Language != null && candidate.Quality > 0)
            .OrderByDescending(candidate => candidate.Quality)
            .ThenBy(candidate => candidate.Index);

        return candidates.Select(candidate => candidate.Language).FirstOrDefault() ?? English;
    }

    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return English;
        }

        var normalized = language.Trim().ToLowerInvariant();
        return normalized == Arabic || normalized.StartsWith("ar-", StringComparison.Ordinal)
            ? Arabic
            : English;
    }

    private static (string? Language, decimal Quality, int Index) ParseCandidate(string token, int index)
    {
        var parts = token.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var language = parts[0].Trim().ToLowerInvariant();
        string? supportedLanguage = language == Arabic || language.StartsWith("ar-", StringComparison.Ordinal)
            ? Arabic
            : language == English || language.StartsWith("en-", StringComparison.Ordinal)
                ? English
                : null;
        var quality = 1m;

        foreach (var parameter in parts.Skip(1))
        {
            if (parameter.StartsWith("q=", StringComparison.OrdinalIgnoreCase) &&
                decimal.TryParse(parameter[2..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            {
                quality = Math.Clamp(parsed, 0m, 1m);
            }
        }

        return (supportedLanguage, quality, index);
    }
}
