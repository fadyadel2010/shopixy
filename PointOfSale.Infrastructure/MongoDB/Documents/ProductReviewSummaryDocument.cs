using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

[BsonIgnoreExtraElements]
public class ProductReviewSummaryDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid ProductId { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal AverageRating { get; set; }

    public int TotalReviews { get; set; }
    [BsonDictionaryOptions(global::MongoDB.Bson.Serialization.Options.DictionaryRepresentation.ArrayOfArrays)]
    public Dictionary<int, int> RatingBreakdown { get; set; } = new();

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastUpdatedAtUtc { get; set; }
}
