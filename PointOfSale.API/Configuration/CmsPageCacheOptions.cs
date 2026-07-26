namespace PointOfSale.API.Configuration;

public sealed class CmsPageCacheOptions
{
    public const string SectionName = "CmsPages:Cache";

    public int PublishedTtlMinutes { get; set; } = 10;
}
