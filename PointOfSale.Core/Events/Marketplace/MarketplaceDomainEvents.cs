using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Events.Marketplace;

/// <summary>
/// Marker contract for immutable marketplace facts.
/// </summary>
public interface IMarketplaceDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredAtUtc { get; }
    string CorrelationId { get; }
    Guid EntityId { get; }
    Guid? AggregateRootId { get; }
    Guid? TenantId { get; }
    int Version { get; }
    int EventVersion { get; }
}

/// <summary>
/// Base contract for versioned marketplace domain events.
/// </summary>
public abstract record MarketplaceDomainEvent : IMarketplaceDomainEvent
{
    private int _version = 1;

    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    public required string CorrelationId { get; init; }
    public required Guid EntityId { get; init; }
    public Guid? AggregateRootId { get; init; }
    public Guid? TenantId { get; init; }
    public int Version
    {
        get => _version;
        init => _version = value;
    }

    public int EventVersion
    {
        get => _version;
        init => _version = value;
    }
}

public sealed record MarketplaceOrderItem
{
    public required Guid ProductId { get; init; }
    public Guid? VariantId { get; init; }
    public required string Sku { get; init; }
    public required int Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
}

public sealed record OrderCreatedEvent : MarketplaceDomainEvent, ICurrencyAwareMarketplaceEvent
{
    public required string OrderNumber { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? OnlineCustomerId { get; init; }
    public required decimal TotalAmount { get; init; }
    public required decimal SubtotalAmount { get; init; }
    public required string Currency { get; init; }
    public string? PaymentMethodCode { get; init; }
    public string? PaymentMethodName { get; init; }
    public bool IsPaid { get; init; }
    public required IReadOnlyCollection<MarketplaceOrderItem> Items { get; init; }
    
    public string EventCategory => "checkout";
    public string EventName => "marketplace.order_created.currency";
}

public sealed record PaymentCompletedEvent : MarketplaceDomainEvent, ICurrencyAwareMarketplaceEvent
{
    public required string OrderNumber { get; init; }
    public required string PaymentMethod { get; init; }
    public string? ExternalReference { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string CompletionSource { get; init; }
    public required DateTime CompletedAtUtc { get; init; }
    
    public string EventCategory => "payment";
    public string EventName => "marketplace.payment_completed.currency";
}

public sealed record OrderDeliveredEvent : MarketplaceDomainEvent
{
    public required string OrderNumber { get; init; }
    public required DateTime DeliveredAtUtc { get; init; }
}

public sealed record RefundProcessedEvent : MarketplaceDomainEvent, ICurrencyAwareMarketplaceEvent
{
    public required string OrderNumber { get; init; }
    public required string RefundId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required bool IsFullRefund { get; init; }
    public required string Reason { get; init; }
    public string? PaymentMethod { get; init; }
    public required DateTime ProcessedAtUtc { get; init; }
    
    public string EventCategory => "payment";
    public string EventName => "marketplace.refund_processed.currency";
}

public sealed record ProductUpdatedEvent : MarketplaceDomainEvent
{
    public required string[] ChangedFields { get; init; }
    public string? Name { get; init; }
    public string? Sku { get; init; }
    public string? Slug { get; init; }
    public decimal? SalePrice { get; init; }
    public decimal? OnlinePrice { get; init; }
    public Guid? CategoryId { get; init; }
    public bool? IsOnlineVisible { get; init; }
    public bool? HasVariants { get; init; }
}

public sealed record ReviewCreatedEvent : MarketplaceDomainEvent
{
    public required Guid ProductId { get; init; }
    public required Guid UserId { get; init; }
    public required int Rating { get; init; }
    public string? Title { get; init; }
    public required bool IsVerifiedPurchase { get; init; }
    public ProductReviewStatus Status { get; init; } = ProductReviewStatus.Pending;
}

public sealed record ReviewApprovedEvent : MarketplaceDomainEvent
{
    public required Guid ProductId { get; init; }
    public required Guid UserId { get; init; }
    public required int Rating { get; init; }
    public required bool IsVerifiedPurchase { get; init; }
    public ProductReviewStatus PreviousStatus { get; init; } = ProductReviewStatus.Pending;
    public required DateTime ApprovedAtUtc { get; init; }
}

public sealed record ReviewRejectedEvent : MarketplaceDomainEvent
{
    public required Guid ProductId { get; init; }
    public required Guid UserId { get; init; }
    public ProductReviewStatus PreviousStatus { get; init; } = ProductReviewStatus.Pending;
    public required DateTime RejectedAtUtc { get; init; }
}

public sealed record ReviewUpdatedEvent : MarketplaceDomainEvent
{
    public required Guid ProductId { get; init; }
    public required Guid UserId { get; init; }
    public required int Rating { get; init; }
    public string? Title { get; init; }
    public required bool IsVerifiedPurchase { get; init; }
    public ProductReviewStatus PreviousStatus { get; init; } = ProductReviewStatus.Pending;
}

public sealed record ReviewDeletedEvent : MarketplaceDomainEvent
{
    public required Guid ProductId { get; init; }
    public required Guid UserId { get; init; }
    public ProductReviewStatus PreviousStatus { get; init; } = ProductReviewStatus.Pending;
}
