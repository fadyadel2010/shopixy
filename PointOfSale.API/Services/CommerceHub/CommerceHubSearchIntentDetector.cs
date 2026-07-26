using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubSearchIntentDetector
{
    Task<CommerceHubSearchIntentContext> DetectAsync(string? query, CancellationToken cancellationToken = default);
}

public sealed partial class CommerceHubSearchIntentDetector : ICommerceHubSearchIntentDetector
{
    private const string CategoryKeywordCacheKey = "commercehub:intent:category-keywords";
    private static readonly TimeSpan CategoryKeywordCacheTtl = TimeSpan.FromMinutes(10);

    private static readonly string[] PriceMaximumKeywords =
    {
        "under",
        "below",
        "less than",
        "up to",
        "max",
        "maximum"
    };

    private static readonly string[] PriceMinimumKeywords =
    {
        "over",
        "above",
        "more than",
        "from",
        "starting at",
        "minimum",
        "min"
    };

    private static readonly string[] PriceGeneralKeywords =
    {
        "cheap",
        "budget",
        "affordable",
        "low cost",
        "discount",
        "sale",
        "under",
        "below",
        "less than",
        "over",
        "above",
        "price",
        "prices"
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;

    public CommerceHubSearchIntentDetector(
        ApplicationDbContext dbContext,
        IMemoryCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<CommerceHubSearchIntentContext> DetectAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var collapsedQuery = CommerceHubSearchTextUtility.CollapseWhitespace(query);
        var normalizedQuery = CommerceHubSearchQueryNormalizer.Normalize(collapsedQuery);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return CommerceHubSearchIntentContext.Unknown();
        }

        var categoryTerms = await MatchCategoryTermsAsync(normalizedQuery, cancellationToken);
        var hasDigits = DigitRegex().IsMatch(collapsedQuery);
        var looksLikePrice = ContainsAny(normalizedQuery, PriceGeneralKeywords) || ContainsCurrencyToken(collapsedQuery);
        var (minPrice, maxPrice) = looksLikePrice ? ParsePriceRange(collapsedQuery, normalizedQuery) : (null, null);

        if (looksLikePrice)
        {
            return new CommerceHubSearchIntentContext
            {
                Type = CommerceHubSearchIntentType.Price,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                CategoryTerms = categoryTerms
            };
        }

        if (categoryTerms.Count > 0 && !hasDigits)
        {
            return new CommerceHubSearchIntentContext
            {
                Type = CommerceHubSearchIntentType.Category,
                CategoryTerms = categoryTerms
            };
        }

        if (hasDigits || normalizedQuery.Contains(' ') || categoryTerms.Count > 0)
        {
            return new CommerceHubSearchIntentContext
            {
                Type = CommerceHubSearchIntentType.Product,
                CategoryTerms = categoryTerms
            };
        }

        return CommerceHubSearchIntentContext.Unknown();
    }

    private async Task<IReadOnlyCollection<string>> MatchCategoryTermsAsync(
        string normalizedQuery,
        CancellationToken cancellationToken)
    {
        var keywordMap = await LoadCategoryKeywordMapAsync(cancellationToken);
        if (keywordMap.Count == 0)
        {
            return Array.Empty<string>();
        }

        var matches = new List<string>();
        foreach (var pair in keywordMap.OrderByDescending(pair => pair.Key.Length))
        {
            if (normalizedQuery.Equals(pair.Key, StringComparison.OrdinalIgnoreCase) ||
                normalizedQuery.Contains($" {pair.Key} ", StringComparison.OrdinalIgnoreCase) ||
                normalizedQuery.StartsWith($"{pair.Key} ", StringComparison.OrdinalIgnoreCase) ||
                normalizedQuery.EndsWith($" {pair.Key}", StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(pair.Value);
            }
        }

        return matches
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
    }

    private async Task<Dictionary<string, string>> LoadCategoryKeywordMapAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CategoryKeywordCacheKey, out Dictionary<string, string>? cachedMap) &&
            cachedMap != null)
        {
            return cachedMap;
        }

        var categories = await _dbContext.CommerceHubCategories
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new
            {
                x.Name,
                x.Slug
            })
            .ToListAsync(cancellationToken);

        var keywordMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
        {
            var canonicalName = CommerceHubSearchTextUtility.CollapseWhitespace(category.Name);
            var normalizedName = CommerceHubSearchQueryNormalizer.Normalize(canonicalName);
            AddKeyword(keywordMap, normalizedName, canonicalName);

            var normalizedSlug = CommerceHubSearchQueryNormalizer.Normalize(category.Slug?.Replace('-', ' '));
            AddKeyword(keywordMap, normalizedSlug, canonicalName);

            foreach (var token in normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddKeyword(keywordMap, token, canonicalName);
                AddKeyword(keywordMap, Singularize(token), canonicalName);
            }
        }

        _cache.Set(CategoryKeywordCacheKey, keywordMap, CategoryKeywordCacheTtl);
        return keywordMap;
    }

    private static void AddKeyword(IDictionary<string, string> map, string? keyword, string canonicalName)
    {
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 2)
        {
            return;
        }

        var normalizedKeyword = CommerceHubSearchTextUtility.CollapseWhitespace(keyword);
        if (!map.ContainsKey(normalizedKeyword))
        {
            map[normalizedKeyword] = canonicalName;
        }
    }

    private static string Singularize(string value)
    {
        if (value.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && value.Length > 3)
        {
            return $"{value[..^3]}y";
        }

        if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase) && value.Length > 3)
        {
            return value[..^1];
        }

        return value;
    }

    private static bool ContainsCurrencyToken(string query) =>
        query.Contains("egp", StringComparison.OrdinalIgnoreCase) ||
        query.Contains("usd", StringComparison.OrdinalIgnoreCase) ||
        query.Contains("aed", StringComparison.OrdinalIgnoreCase) ||
        query.Contains("sar", StringComparison.OrdinalIgnoreCase) ||
        query.Contains("jneh", StringComparison.OrdinalIgnoreCase) ||
        query.Contains("\u062C\u0646\u064A\u0647", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string query, IEnumerable<string> keywords) =>
        keywords.Any(keyword => query.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static (decimal? MinPrice, decimal? MaxPrice) ParsePriceRange(string rawQuery, string normalizedQuery)
    {
        var match = PriceValueRegex().Match(rawQuery);
        if (!match.Success)
        {
            return (null, null);
        }

        var numericValue = match.Groups["value"].Value.Replace(",", string.Empty, StringComparison.Ordinal);
        if (!decimal.TryParse(numericValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0)
        {
            return (null, null);
        }

        if (ContainsAny(normalizedQuery, PriceMaximumKeywords))
        {
            return (null, amount);
        }

        if (ContainsAny(normalizedQuery, PriceMinimumKeywords))
        {
            return (amount, null);
        }

        return (null, amount);
    }

    [GeneratedRegex(@"\d")]
    private static partial Regex DigitRegex();

    [GeneratedRegex(@"(?<value>\d[\d,]*(?:\.\d+)?)")]
    private static partial Regex PriceValueRegex();
}
