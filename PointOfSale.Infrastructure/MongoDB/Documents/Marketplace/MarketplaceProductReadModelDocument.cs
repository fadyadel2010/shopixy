using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents.Marketplace;

[BsonIgnoreExtraElements]
public sealed class MarketplaceProductReadModelDocument
{
    public const string CollectionName = "marketplace_product_read_models";

    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid ProductId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid CategoryId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public string? FeaturesJson { get; set; }
    public string? FaqJson { get; set; }
    public string? TagsJson { get; set; }
    public string? Slug { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Price { get; set; }

    public string Currency { get; set; } = "EGP";

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Rating { get; set; }

    public int ReviewCount { get; set; }
    public string? Category { get; set; }
    public string? CategoryAr { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid VendorId { get; set; }

    public string VendorName { get; set; } = string.Empty;
    public List<string> Images { get; set; } = new();
    public bool IsAvailable { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? LastEventId { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastEventOccurredAtUtc { get; set; }

    public int LastEventVersion { get; set; }

    public long LastEntityMutationVersion { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ProjectionUpdatedAtUtc { get; set; }
}
