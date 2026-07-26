using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

[BsonIgnoreExtraElements]
public class CommerceHubOrderDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? Id { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid MarketplaceOrderId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Area { get; set; }
    public string? Notes { get; set; }
    public string Currency { get; set; } = "EGP";
    public string PaymentMethodCode { get; set; } = string.Empty;
    public string PaymentMethodName { get; set; } = string.Empty;
    public string? PaymentReference { get; set; }
    public string? PaymentReferenceId { get; set; }
    public string? PaymentGateway { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Subtotal { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal ShippingTotal { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalAmount { get; set; }

    [BsonRepresentation(BsonType.String)]
    public OrderStatus Status { get; set; }

    [BsonRepresentation(BsonType.String)]
    public PaymentStatus PaymentStatus { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? PaidAt { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? LastEventId { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastEventOccurredAtUtc { get; set; }

    public int LastEventVersion { get; set; }

    public long LastSourceMutationTicks { get; set; }

    public string ProjectionChecksum { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public Guid CustomerId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? OnlineCustomerId { get; set; }

    public List<CommerceHubSubOrderDocument> SubOrders { get; set; } = new();
}

[BsonIgnoreExtraElements]
public class CommerceHubSubOrderDocument
{
    [BsonRepresentation(BsonType.String)]
    public Guid SubOrderId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public Guid StoreId { get; set; }

    public string StoreName { get; set; } = string.Empty;
    public string Currency { get; set; } = "EGP";

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Subtotal { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Shipping { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? ShippingMethodId { get; set; }

    public string? ShippingMethodName { get; set; }
    public string? ShippingSnapshotJson { get; set; }
    
    [BsonRepresentation(BsonType.String)]
    public CommerceHubPaymentMethod PaymentMethod { get; set; }
    
    public string PaymentMethodCode { get; set; } = string.Empty;
    public string PaymentMethodName { get; set; } = string.Empty;
    public string? PaymentSnapshotJson { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Total { get; set; }

    [BsonRepresentation(BsonType.String)]
    public OrderStatus Status { get; set; }

    [BsonRepresentation(BsonType.String)]
    public PaymentStatus PaymentStatus { get; set; }

    public string? PaymentReferenceId { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? PaidAt { get; set; }

    public List<CommerceHubOrderItemDocument> Items { get; set; } = new();
}

[BsonIgnoreExtraElements]
public class CommerceHubOrderItemDocument
{
    [BsonRepresentation(BsonType.String)]
    public Guid ListingId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid ProductId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid VariantId { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string? VariantAttributesJson { get; set; }
    public string? ImageUrl { get; set; }
    public int Quantity { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal UnitPrice { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Total { get; set; }
}
