namespace PointOfSale.API.DTOs.Feeds;

/// <summary>
/// FEED-1A: Canonical channel-neutral feed item produced by the set-based
/// feed projection. Serialization-friendly; carries no tenant objects,
/// EF entities, or navigation properties.
///
/// Identity contract (Feed-0):
/// - Simple product: FeedItemId = ProductId, VariantId/ItemGroupId null.
/// - Variant product: one item per admitted variant;
///   FeedItemId = VariantId, ItemGroupId = ProductId.
/// </summary>
public class FeedProduct
{
    public Guid FeedItemId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public Guid? ItemGroupId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProductUrl { get; set; } = string.Empty;

    public string PrimaryImageUrl { get; set; } = string.Empty;
    public IReadOnlyList<string> AdditionalImageUrls { get; set; } = Array.Empty<string>();

    public decimal Price { get; set; }
    public decimal? SalePrice { get; set; }
    public string Currency { get; set; } = string.Empty;

    public string Availability { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;

    public string? Brand { get; set; }
    public string? Sku { get; set; }
    public string? Gtin { get; set; }
    public string? CategoryName { get; set; }
}

/// <summary>
/// FEED-1A: Meta V1 availability vocabulary.
/// Mapped from existing Shopixy inventory semantics (Feed-0 decisions):
/// TrackStock=false -> InStock; tracked stock>0 -> InStock;
/// tracked stock=0 + AllowBackorder -> AvailableForOrder;
/// tracked stock=0 no backorder -> OutOfStock.
/// </summary>
public static class FeedAvailability
{
    public const string InStock = "in stock";
    public const string AvailableForOrder = "available for order";
    public const string OutOfStock = "out of stock";
}
