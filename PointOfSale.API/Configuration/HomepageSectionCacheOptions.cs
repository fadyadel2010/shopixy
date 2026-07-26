namespace PointOfSale.API.Configuration;

public sealed class HomepageSectionCacheOptions
{
    public const string SectionName = "HomepageSections:Cache";

    public int PublishedTtlMinutes { get; set; } = 10;
}
