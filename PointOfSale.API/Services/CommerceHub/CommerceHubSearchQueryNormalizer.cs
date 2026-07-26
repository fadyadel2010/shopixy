using System.Text.RegularExpressions;
using PointOfSale.Infrastructure.Elasticsearch.Documents;

namespace PointOfSale.API.Services.CommerceHub;

internal static partial class CommerceHubSearchQueryNormalizer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "an",
        "and",
        "at",
        "by",
        "for",
        "from",
        "in",
        "of",
        "on",
        "or",
        "the",
        "to",
        "with"
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = NonAlphaNumericRegex().Replace(value.Trim(), " ");
        var normalized = CommerceHubSearchDocumentMapper.NormalizeText(cleaned);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var filteredTokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !StopWords.Contains(token))
            .ToList();

        if (filteredTokens.Count == 0)
        {
            return normalized;
        }

        return string.Join(' ', filteredTokens);
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlphaNumericRegex();
}
