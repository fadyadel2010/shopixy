namespace PointOfSale.API.Services.Feeds;

/// <summary>
/// FEED-1B: Response safeguards for the public Meta feed.
///
/// ITEM CAP:
/// - Hard upper bound of <see cref="DefaultMaxItems"/> (10,000) feed items.
///   Meta's own catalog ingestion limits are far higher, so this cap only
///   protects against accidental unbounded output; it never constrains a
///   realistic Shopixy catalog.
/// - Truncation is deterministic: projection order is Product.Id then
///   Variant.Id, so the SAME first N items are returned on every request.
///
/// RESPONSE SIZE:
/// - Backstop consistent with the item cap (~2 KB/item average headroom).
///   When exceeded the deterministic first-K items are re-serialized — the
///   response is always valid RSS, never a partial/malformed document.
///
/// Non-positive values fall back to the hardened defaults, so a
/// misconfiguration can never remove the caps entirely.
/// </summary>
public sealed class MetaFeedSafeguards
{
    public const int DefaultMaxItems = 10_000;
    public const long DefaultMaxResponseBytes = 20L * 1024 * 1024; // 20 MB

    public static MetaFeedSafeguards Default { get; } = new();

    public int MaxItems { get; }
    public long MaxResponseBytes { get; }

    public MetaFeedSafeguards(int maxItems = DefaultMaxItems, long maxResponseBytes = DefaultMaxResponseBytes)
    {
        MaxItems = maxItems > 0 ? maxItems : DefaultMaxItems;
        MaxResponseBytes = maxResponseBytes > 0 ? maxResponseBytes : DefaultMaxResponseBytes;
    }
}
