using System.Text.RegularExpressions;
using PointOfSale.Infrastructure.Elasticsearch.Documents;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

public static class CommerceHubSearchTextUtility
{
    public const int MinimumAutocompleteQueryLength = 3;

    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex NonAlphaNumericRegex = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    public static string CollapseWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return MultiWhitespaceRegex.Replace(value.Trim(), " ");
    }

    public static bool HasAutocompleteQuery(string? value) =>
        CollapseWhitespace(value).Length >= MinimumAutocompleteQueryLength;

    public static string NormalizeSuggestionKey(string? value)
    {
        var collapsed = CollapseWhitespace(value);
        if (string.IsNullOrWhiteSpace(collapsed))
        {
            return string.Empty;
        }

        var cleaned = NonAlphaNumericRegex.Replace(collapsed, " ");
        return CommerceHubSearchDocumentMapper.NormalizeText(cleaned);
    }

    public static List<string> BuildSimilarTerms(
        string? query,
        IEnumerable<string> primaryTerms,
        IEnumerable<string> secondaryTerms,
        int limit,
        IEnumerable<string>? excludedTerms = null)
    {
        return DeduplicateTerms(
            primaryTerms.Concat(secondaryTerms),
            new[] { query ?? string.Empty }.Concat(excludedTerms ?? Array.Empty<string>()),
            limit);
    }

    public static List<string> DeduplicateTerms(
        IEnumerable<string> candidates,
        IEnumerable<string> excludedTerms,
        int limit)
    {
        if (limit <= 0)
        {
            return new List<string>();
        }

        var excludedKeys = new HashSet<string>(
            excludedTerms
                .Select(NormalizeSuggestionKey)
                .Where(key => !string.IsNullOrWhiteSpace(key)),
            StringComparer.OrdinalIgnoreCase);

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<string>(limit);

        foreach (var candidate in candidates)
        {
            var normalized = CollapseWhitespace(candidate);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var key = NormalizeSuggestionKey(normalized);
            if (string.IsNullOrWhiteSpace(key) || excludedKeys.Contains(key) || !seenKeys.Add(key))
            {
                continue;
            }

            results.Add(normalized);
            if (results.Count >= limit)
            {
                break;
            }
        }

        return results;
    }
}
