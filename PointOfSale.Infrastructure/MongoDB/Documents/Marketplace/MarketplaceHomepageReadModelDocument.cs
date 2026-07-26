using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents.Marketplace;

[BsonIgnoreExtraElements]
public sealed class MarketplaceHomepageReadModelDocument
{
    public const string CollectionName = "marketplace_homepage_read_models";
    public const string GlobalScopeKey = "global";

    [BsonId]
    public string Id { get; set; } = string.Empty;

    public string ScopeKey { get; set; } = GlobalScopeKey;
    public Guid? TenantId { get; set; }
    public List<MarketplaceHomepageSectionDocument> Sections { get; set; } = new();

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastUpdated { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastEventOccurredAtUtc { get; set; }

    public int LastEventVersion { get; set; }
}

public sealed class MarketplaceHomepageSectionDocument
{
    public string Type { get; set; } = string.Empty;
    public int Order { get; set; }
    public string DataJson { get; set; } = "{}";
    public int Version { get; set; }
    public int SchemaVersion { get; set; }
    public bool IsActive { get; set; }
}
