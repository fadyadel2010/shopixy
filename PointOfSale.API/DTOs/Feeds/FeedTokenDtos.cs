namespace PointOfSale.API.DTOs.Feeds;

/// <summary>
/// FEED-1A: Response returned exactly once when a feed token is created or rotated.
/// The raw token appears only inside <see cref="FeedUrl"/> and is never returned again.
/// </summary>
public class CreateProductFeedTokenResponse
{
    public Guid TokenId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string FeedUrl { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// FEED-1A: Safe token metadata for admin listing.
/// Never contains the raw token or the stored hash.
/// </summary>
public class ProductFeedTokenInfoDto
{
    public Guid TokenId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
