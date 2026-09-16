using System.Text.RegularExpressions;

namespace PointOfSale.Core.Helpers;

/// <summary>
/// WAVE 1 — Product slug authority.
///
/// Proven contract (see PRODUCT_WAVE1 report):
/// - <c>Product.Slug</c> is OPTIONAL at rest (nullable, no uniqueness constraint).
/// - It is REQUIRED for storefront slug-route lookup
///   (<c>GET /storefront/products/by-slug/{slug}</c> rejects blank slugs with INVALID_SLUG,
///   and products without a slug are only reachable by id).
/// - Lookup is tenant-scoped and case-folded on read
///   (<c>StorefrontProductService</c> lowercases before querying), so slugs must be
///   stored lowercase to remain resolvable.
/// - Slugs are STABLE: renaming a product never rewrites its slug, so existing
///   storefront URLs, feeds, and search documents keep resolving.
///
/// The server is the slug authority: when the caller omits (or supplies an
/// unusable) slug, the orchestrator derives a deterministic, tenant-scoped,
/// collision-safe slug instead of persisting a blank value that would make the
/// product invisible on the slug route.
/// </summary>
public static partial class ProductSlugAuthority
{
    /// <summary>Maximum persisted slug length (matches CMS slug authority).</summary>
    public const int MaxLength = 160;

    /// <summary>Fallback prefix when no latin slug text can be derived (e.g. Arabic-only names).</summary>
    public const string FallbackPrefix = "product";

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlugCharactersRegex();

    /// <summary>
    /// Normalizes a caller-supplied slug to its canonical stored form.
    /// Returns null when the input carries no usable slug text (null, blank,
    /// or nothing but characters outside [a-z0-9], e.g. Arabic-only text).
    /// Never throws.
    /// </summary>
    public static string? Normalize(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var normalized = NonSlugCharactersRegex()
            .Replace(slug.Trim().ToLowerInvariant(), "-")
            .Trim('-');

        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        if (normalized.Length > MaxLength)
        {
            normalized = normalized[..MaxLength].TrimEnd('-');
        }

        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    /// <summary>
    /// Derives the deterministic base slug for a product.
    /// Prefers the explicit slug, then the primary name. When neither yields
    /// usable latin text, falls back to "product-{8 hex chars of product id}"
    /// which is deterministic per product (no random values) and URL-safe.
    /// </summary>
    public static string GenerateBase(string? requestedSlug, string? name, Guid productId)
    {
        return Normalize(requestedSlug)
            ?? Normalize(name)
            ?? $"{FallbackPrefix}-{productId:N}"[..(FallbackPrefix.Length + 9)];
    }

    /// <summary>
    /// Makes a base slug unique within the tenant by appending "-2", "-3", ...
    /// <paramref name="isTaken"/> is evaluated against tenant-scoped stored slugs
    /// (already normalized). The base itself is tried first, so the common
    /// no-collision case keeps the clean URL.
    /// </summary>
    public static string EnsureUnique(string baseSlug, Func<string, bool> isTaken)
    {
        if (!isTaken(baseSlug))
        {
            return baseSlug;
        }

        for (var attempt = 2; ; attempt++)
        {
            var suffix = "-" + attempt;
            var candidate = baseSlug.Length + suffix.Length > MaxLength
                ? baseSlug[..(MaxLength - suffix.Length)].TrimEnd('-') + suffix
                : baseSlug + suffix;

            if (!isTaken(candidate))
            {
                return candidate;
            }
        }
    }
}
