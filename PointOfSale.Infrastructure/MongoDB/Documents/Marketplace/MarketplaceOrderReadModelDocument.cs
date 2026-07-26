using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents.Marketplace;

[BsonIgnoreExtraElements]
public sealed class MarketplaceOrderReadModelDocument
{
    public const string CollectionName = "marketplace_order_read_models";

    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid OrderId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    
    [BsonRepresentation(BsonType.String)]
    public Guid? OnlineCustomerId { get; set; }
    
    public string Status { get; set; } = string.Empty;
    public string FinancialPostingStatus { get; set; } = string.Empty;
    public bool IsFinanciallyPosted { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalAmount { get; set; }

    public string Currency { get; set; } = "EGP";
    public List<MarketplaceOrderReadModelItemDocument> Items { get; set; } = new();

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? PaidAtUtc { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? FinancialPostedAtUtc { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? DeliveredAtUtc { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? LastEventId { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastEventOccurredAtUtc { get; set; }

    public int LastEventVersion { get; set; }

    public long LastEntityMutationVersion { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ProjectionUpdatedAtUtc { get; set; }
}

public sealed class MarketplaceOrderReadModelItemDocument
{
    [BsonRepresentation(BsonType.String)]
    public Guid ProductId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? VariantId { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal UnitPrice { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Total { get; set; }

    public string? ImageUrl { get; set; }
}
