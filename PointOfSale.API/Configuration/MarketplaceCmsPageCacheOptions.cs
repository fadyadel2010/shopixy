namespace PointOfSale.API.Configuration;

public sealed class MarketplaceCmsPageCacheOptions
{
    public const string SectionName = "MarketplaceCmsPages:Cache";

    public int PublishedTtlMinutes { get; set; } = 10;
}
