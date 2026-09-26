namespace PointOfSale.Infrastructure.MongoDB.Services;

public sealed class ProductLookupValidationCacheOptions
{
    public const string SectionName = "MongoProductLookupCache";

    public double PositiveTtlSeconds { get; set; } = 30;

    public double NegativeTtlSeconds { get; set; } = 5;

    public int MaxEntries { get; set; } = 8192;
}
