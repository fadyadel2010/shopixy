using System.Globalization;
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
    /// Key schema version. Changing canonicalization requires a new version so
    /// old distributed entries can expire without being interpreted as current.
    /// </summary>
    private const string HashSchemaVersion = "h2";

    /// <summary>
    /// Full SHA-256 output avoids making correctness depend on a deliberately
    /// truncated collision space.
    /// </summary>
    private const int HashLength = 64;

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
            .Where(f => !string.IsNullOrWhiteSpace(f.Key) && f.Value != null)
            .Select(f => new KeyValuePair<string, string>(
                f.Key.Trim().ToLowerInvariant(),
                f.Value!.Trim().ToLowerInvariant()))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .ThenBy(f => f.Value, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder(256);
        sb.Append(HashSchemaVersion);

        foreach (var filter in normalized)
        {
            AppendLengthPrefixed(sb, filter.Key);
            AppendLengthPrefixed(sb, filter.Value);
        }

        if (page.HasValue)
        {
            AppendLengthPrefixed(sb, "page");
            AppendLengthPrefixed(sb, page.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (pageSize.HasValue)
        {
            AppendLengthPrefixed(sb, "page-size");
            AppendLengthPrefixed(sb, pageSize.Value.ToString(CultureInfo.InvariantCulture));
        }

        return $"{HashSchemaVersion}-{HashString(sb.ToString())}";
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
            filters.Add(new("stock", inStock.Value ? "true" : "false"));
        if (minPrice.HasValue)
            filters.Add(new("pmin", minPrice.Value.ToString("G29", CultureInfo.InvariantCulture)));
        if (maxPrice.HasValue)
            filters.Add(new("pmax", maxPrice.Value.ToString("G29", CultureInfo.InvariantCulture)));
        if (!string.IsNullOrWhiteSpace(brand))
            filters.Add(new("brand", brand));

        return NormalizeAndHash(filters, page, pageSize);
    }

    private static void AppendLengthPrefixed(StringBuilder builder, string value)
    {
        builder
            .Append('|')
            .Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value);
    }

    private static string HashString(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..HashLength].ToLowerInvariant();
    }
}
