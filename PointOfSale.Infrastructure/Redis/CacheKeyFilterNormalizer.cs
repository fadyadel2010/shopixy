using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.Infrastructure.Redis;

/// <summary>
/// Normalizes and hashes filter combinations for cache key generation.
/// 
/// PHASE: Redis Hardening — Cache Key Explosion Control
/// 
/// PROBLEM:
/// Keys like products:list:{filtersHash} can grow unbounded because each unique
/// combination of filters, sorts, and pagination produces a different hash.
/// 
/// SOLUTION:
/// 1. Sort filter keys alphabetically
/// 2. Remove default/empty values
/// 3. Normalize casing and whitespace
/// 4. Cap maximum filter count
/// 5. Produce a deterministic, truncated hash
/// 
/// This ensures that equivalent filter sets always produce the same cache key,
/// and limits the maximum number of distinct keys.
/// </summary>
public static class CacheKeyFilterNormalizer
{
    /// <summary>
    /// Maximum number of filter parameters allowed.
    /// Excess filters are truncated (sorted alphabetically).
    /// </summary>
    private const int MaxFilterCount = 20;

    /// <summary>
    /// Hash output length in hex characters (12 bytes = 24 hex chars).
    /// Shorter hashes reduce key size while maintaining sufficient uniqueness.
    /// </summary>
    private const int HashTruncationLength = 24;

    /// <summary>
    /// Normalizes a dictionary of filter parameters into a deterministic hash string.
    /// </summary>
    /// <param name="filters">The filter key-value pairs to normalize.</param>
    /// <param name="page">Optional page number (included in hash if provided).</param>
    /// <param name="pageSize">Optional page size (included in hash if provided).</param>
    /// <returns>A deterministic, truncated SHA256 hash of the normalized filters.</returns>
    public static string NormalizeAndHash(
        IEnumerable<KeyValuePair<string, string?>> filters,
        int? page = null,
        int? pageSize = null)
    {
        var normalized = filters
            .Where(f => !string.IsNullOrWhiteSpace(f.Value))
            .Select(f => new KeyValuePair<string, string>(
                f.Key.Trim().ToLowerInvariant(),
                f.Value!.Trim().ToLowerInvariant()))
            .Where(f => !IsDefaultValue(f.Value))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Take(MaxFilterCount)
            .ToList();

        var sb = new StringBuilder(256);

        foreach (var filter in normalized)
        {
            if (sb.Length > 0)
            {
                sb.Append('|');
            }

            sb.Append(filter.Key).Append('=').Append(filter.Value);
        }

        if (page.HasValue)
        {
            sb.Append("|p=").Append(page.Value);
        }

        if (pageSize.HasValue)
        {
            sb.Append("|ps=").Append(pageSize.Value);
        }

        if (sb.Length == 0)
        {
            return "all";
        }

        return HashString(sb.ToString());
    }

    /// <summary>
    /// Convenience overload that accepts named parameters commonly used in product queries.
    /// </summary>
    public static string NormalizeProductFiltersHash(
        string? search = null,
        string? categoryId = null,
        string? sortBy = null,
        string? sortDirection = null,
        bool? inStock = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        string? brand = null,
        int? page = null,
        int? pageSize = null)
    {
        var filters = new List<KeyValuePair<string, string?>>();

        if (!string.IsNullOrWhiteSpace(search))
            filters.Add(new("q", search));
        if (!string.IsNullOrWhiteSpace(categoryId))
            filters.Add(new("cat", categoryId));
        if (!string.IsNullOrWhiteSpace(sortBy))
            filters.Add(new("sort", sortBy));
        if (!string.IsNullOrWhiteSpace(sortDirection))
            filters.Add(new("dir", sortDirection));
        if (inStock.HasValue)
            filters.Add(new("stock", inStock.Value.ToString()));
        if (minPrice.HasValue)
            filters.Add(new("pmin", minPrice.Value.ToString("F2")));
        if (maxPrice.HasValue)
            filters.Add(new("pmax", maxPrice.Value.ToString("F2")));
        if (!string.IsNullOrWhiteSpace(brand))
            filters.Add(new("brand", brand));

        return NormalizeAndHash(filters, page, pageSize);
    }

    private static bool IsDefaultValue(string value)
    {
        return value is "0" or "false" or "" or "null" or "none" or "all" or "default";
    }

    private static string HashString(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..HashTruncationLength].ToLowerInvariant();
    }
}
