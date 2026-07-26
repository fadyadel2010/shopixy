using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PointOfSale.API.CommerceHub.Services;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubSearchIntelligenceService
{
    void TrackClick(CommerceHubTrackedSearchClick click);
    void TrackImpression(CommerceHubTrackedSearchImpression impression);
    void TrackFilterEvent(CommerceHubTrackedSearchFilterEvent filterEvent);
    void TrackQuery(CommerceHubTrackedSearchQuery query);
    Task<IReadOnlyCollection<string>> ExpandQueryAsync(string? query, CancellationToken cancellationToken = default);
}

public class CommerceHubSearchIntelligenceService : ICommerceHubSearchIntelligenceService
{
    private const string SynonymCacheKey = "commercehub:synonyms";
    private static readonly TimeSpan ClickDeduplicationWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ImpressionDeduplicationWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FilterEventDeduplicationWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SynonymCacheTtl = TimeSpan.FromMinutes(10);

    private readonly ICommerceHubSearchAnalyticsQueue _queue;
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;

    public CommerceHubSearchIntelligenceService(
        ICommerceHubSearchAnalyticsQueue queue,
        ApplicationDbContext dbContext,
        IMemoryCache cache)
    {
        _queue = queue;
        _dbContext = dbContext;
        _cache = cache;
    }

    public void TrackClick(CommerceHubTrackedSearchClick click)
    {
        if (click.ProductId == Guid.Empty)
        {
            return;
        }

        var normalizedQuery = NormalizeQuery(click.QueryText);
        if (IsDuplicateClick(click.SessionId, click.ProductId, normalizedQuery))
        {
            return;
        }

        _queue.Enqueue(click with
        {
            QueryText = normalizedQuery,
            ClickedAtUtc = click.ClickedAtUtc == default ? DateTime.UtcNow : click.ClickedAtUtc
        });
    }

    public void TrackImpression(CommerceHubTrackedSearchImpression impression)
    {
        var normalizedQuery = NormalizeQuery(impression.QueryText);
        if (string.IsNullOrWhiteSpace(normalizedQuery) || impression.ProductIds.Count == 0)
        {
            return;
        }

        var normalizedProductIds = impression.ProductIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (normalizedProductIds.Count == 0 ||
            IsDuplicateImpression(impression.SessionId, normalizedQuery, normalizedProductIds))
        {
            return;
        }

        _queue.Enqueue(impression with
        {
            QueryText = normalizedQuery,
            ProductIds = normalizedProductIds,
            ImpressedAtUtc = impression.ImpressedAtUtc == default ? DateTime.UtcNow : impression.ImpressedAtUtc
        });
    }

    public void TrackFilterEvent(CommerceHubTrackedSearchFilterEvent filterEvent)
    {
        var normalizedKey = NormalizeFilterKey(filterEvent.FilterKey);
        var normalizedValue = NormalizeFilterValue(filterEvent.FilterValue);
        if (string.IsNullOrWhiteSpace(normalizedKey) || string.IsNullOrWhiteSpace(normalizedValue))
        {
            return;
        }

        var normalizedQuery = NormalizeOptionalQuery(filterEvent.QueryText);
        if (IsDuplicateFilterEvent(filterEvent.SessionId, normalizedKey, normalizedValue, normalizedQuery))
        {
            return;
        }

        _queue.Enqueue(filterEvent with
        {
            FilterKey = normalizedKey,
            FilterValue = normalizedValue,
            QueryText = normalizedQuery,
            OccurredAtUtc = filterEvent.OccurredAtUtc == default ? DateTime.UtcNow : filterEvent.OccurredAtUtc
        });
    }

    public void TrackQuery(CommerceHubTrackedSearchQuery query)
    {
        var normalizedQuery = NormalizeQuery(query.QueryText);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return;
        }

        _queue.Enqueue(query with
        {
            QueryText = normalizedQuery,
            Intent = string.IsNullOrWhiteSpace(query.Intent) ? "unknown" : query.Intent.Trim().ToLowerInvariant(),
            AppliedExpansions = CommerceHubSearchTextUtility.DeduplicateTerms(
                query.AppliedExpansions,
                new[] { normalizedQuery },
                6),
            SearchedAtUtc = query.SearchedAtUtc == default ? DateTime.UtcNow : query.SearchedAtUtc
        });
    }

    public async Task<IReadOnlyCollection<string>> ExpandQueryAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeQuery(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return Array.Empty<string>();
        }

        var synonymMap = await LoadSynonymMapAsync(cancellationToken);
        if (synonymMap.Count == 0)
        {
            return Array.Empty<string>();
        }

        var expansions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (synonymMap.TryGetValue(normalizedQuery, out var exactSynonyms))
        {
            foreach (var synonym in exactSynonyms)
            {
                expansions.Add(synonym);
            }
        }

        var tokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!synonymMap.TryGetValue(tokens[i], out var tokenSynonyms))
            {
                continue;
            }

            foreach (var synonym in tokenSynonyms)
            {
                var expandedTokens = tokens.ToArray();
                expandedTokens[i] = synonym;
                expansions.Add(string.Join(' ', expandedTokens));
            }
        }

        return expansions
            .Where(expansion => !string.Equals(expansion, normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToList();
    }

    private async Task<Dictionary<string, IReadOnlyCollection<string>>> LoadSynonymMapAsync(
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SynonymCacheKey, out Dictionary<string, IReadOnlyCollection<string>>? cachedMap) &&
            cachedMap != null)
        {
            return cachedMap;
        }

        var synonyms = await _dbContext.CommerceHubSearchSynonyms
            .AsNoTracking()
            .Select(x => new
            {
                x.Term,
                x.SynonymsJson,
                x.Direction
            })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var synonym in synonyms)
        {
            var term = NormalizeQuery(synonym.Term);
            if (string.IsNullOrWhiteSpace(term))
            {
                continue;
            }

            var values = ParseSynonyms(synonym.SynonymsJson);
            if (values.Count == 0)
            {
                continue;
            }

            AddSynonyms(map, term, values);

            if (string.Equals(
                    synonym.Direction?.Trim(),
                    PointOfSale.Core.Entities.CommerceHubSearchSynonym.TwoWayDirection,
                    StringComparison.OrdinalIgnoreCase))
            {
                foreach (var value in values)
                {
                    AddSynonyms(map, value, new[] { term });
                }
            }
        }

        var readOnlyMap = map.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyCollection<string>)pair.Value
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);

        _cache.Set(SynonymCacheKey, readOnlyMap, SynonymCacheTtl);
        return readOnlyMap;
    }

    private static IReadOnlyCollection<string> ParseSynonyms(string? synonymsJson)
    {
        if (string.IsNullOrWhiteSpace(synonymsJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(synonymsJson) ?? new List<string>();
            return values
                .Select(NormalizeQuery)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string NormalizeQuery(string? value)
    {
        return CommerceHubSearchQueryNormalizer.Normalize(value);
    }

    private static string NormalizeOptionalQuery(string? value)
    {
        return NormalizeQuery(value);
    }

    private static string NormalizeFilterKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return CommerceHubAttributeService.NormalizeKey(value);
    }

    private static string NormalizeFilterValue(string? value)
    {
        var collapsed = CommerceHubSearchTextUtility.CollapseWhitespace(value);
        return string.IsNullOrWhiteSpace(collapsed)
            ? string.Empty
            : collapsed.ToLowerInvariant();
    }

    private bool IsDuplicateClick(string? sessionId, Guid productId, string normalizedQuery)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return false;
        }

        var cacheKey = $"click:{sessionId.Trim()}:{productId}:{normalizedQuery}";
        if (_cache.TryGetValue(cacheKey, out _))
        {
            return true;
        }

        _cache.Set(cacheKey, true, ClickDeduplicationWindow);
        return false;
    }

    private bool IsDuplicateImpression(
        string? sessionId,
        string normalizedQuery,
        IReadOnlyCollection<Guid> productIds)
    {
        if (string.IsNullOrWhiteSpace(sessionId) ||
            string.IsNullOrWhiteSpace(normalizedQuery) ||
            productIds.Count == 0)
        {
            return false;
        }

        var orderedIds = productIds.Select(id => id.ToString("N"));
        var cacheKey = $"impression:{sessionId.Trim()}:{normalizedQuery}:{string.Join(",", orderedIds)}";
        if (_cache.TryGetValue(cacheKey, out _))
        {
            return true;
        }

        _cache.Set(cacheKey, true, ImpressionDeduplicationWindow);
        return false;
    }

    private bool IsDuplicateFilterEvent(
        string? sessionId,
        string normalizedKey,
        string normalizedValue,
        string normalizedQuery)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var cacheKey = $"filter-event:{sessionId.Trim()}:{normalizedKey}:{normalizedValue}:{normalizedQuery}";
        if (_cache.TryGetValue(cacheKey, out _))
        {
            return true;
        }

        _cache.Set(cacheKey, true, FilterEventDeduplicationWindow);
        return false;
    }

    private static void AddSynonyms(
        IDictionary<string, HashSet<string>> map,
        string term,
        IEnumerable<string> values)
    {
        if (!map.TryGetValue(term, out var synonyms))
        {
            synonyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            map[term] = synonyms;
        }

        foreach (var value in values)
        {
            if (!string.Equals(value, term, StringComparison.OrdinalIgnoreCase))
            {
                synonyms.Add(value);
            }
        }
    }
}
