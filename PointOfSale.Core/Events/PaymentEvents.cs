using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Events;

/// <summary>
/// Base class for all payment events.
/// </summary>
public abstract class PaymentEventBase : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid OrderId { get; set; }
    public Guid? PaymentIntentId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;

    public Guid GetAuthoritativeAggregateId() => OrderId;
}

/// <summary>
/// Event emitted when a payment intent is created.
/// </summary>
public class PaymentIntentCreatedEvent : PaymentEventBase
{
    public Guid? CartId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? RedirectUrl { get; set; }
    public bool IsOfflineCollection { get; set; }
    public bool PromotionCalculationFailed { get; set; }
}

/// <summary>
/// Event emitted when a payment is authorized (2-step payments).
/// </summary>
public class PaymentAuthorizedEvent : PaymentEventBase
{
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? ProviderAuthorizationId { get; set; }
    public DateTime AuthorizedAt { get; set; }
}

/// <summary>
/// Event emitted when a payment is captured/completed.
/// </summary>
public class PaymentCapturedEvent : PaymentEventBase
{
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? ProviderPaymentId { get; set; }
    public DateTime CapturedAt { get; set; }
}

/// <summary>
/// Event emitted when a payment fails.
/// </summary>
public class PaymentFailedEvent : PaymentEventBase
{
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public string? FailureCode { get; set; }
    public DateTime FailedAt { get; set; }
}

/// <summary>
/// Event emitted when payment is skipped for offline collection orders.
/// </summary>
public class PaymentSkippedEvent : PaymentEventBase
{
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Reason { get; set; } = "OfflineCollection";
}

/// <summary>
/// Event emitted when an order is paid.
/// </summary>
public class OrderPaidEvent : PaymentEventBase
{
    public string OrderNumber { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public string Currency { get; set; } = string.Empty;
    public OrderStatus NewOrderStatus { get; set; }
    public PaymentStatus NewPaymentStatus { get; set; }
    public DateTime PaidAt { get; set; }
}

/// <summary>
/// Event emitted when an order is confirmed (for offline collection or after payment).
/// </summary>
public class OrderConfirmedEvent : PaymentEventBase
{
    public string OrderNumber { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string ConfirmationMethod { get; set; } = string.Empty;
    public OrderStatus NewOrderStatus { get; set; }
    public DateTime ConfirmedAt { get; set; }
}

/// <summary>
/// Event emitted after the authoritative collection accounting journal is posted.
/// Downstream systems that must not outrun financial truth should depend on this
/// event instead of reacting directly to paid/captured payment events.
/// </summary>
public class OrderCollectionAccountingPostedEvent : PaymentEventBase
{
    public string OrderNumber { get; set; } = string.Empty;
    public decimal AmountPosted { get; set; }
    public string Currency { get; set; } = string.Empty;
    public Guid FinancialPostingJournalId { get; set; }
    public FinancialPostingStatus NewFinancialPostingStatus { get; set; }
    public string AccountingEventType { get; set; } = string.Empty;
    public string? ExternalReference { get; set; }
    public DateTime PostedAt { get; set; }
}

/// <summary>
/// Event emitted when inventory is confirmed for an order.
/// </summary>
public class InventoryConfirmedEvent : PaymentEventBase
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid? ReservationId { get; set; }
    public List<InventoryConfirmationItem> Items { get; set; } = new();
    public DateTime ConfirmedAt { get; set; }
}

public class InventoryConfirmationItem
{
    public Guid ProductId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public Guid? WarehouseId { get; set; }
}

/// <summary>
/// Event emitted when inventory is released (payment failure/cancellation).
/// </summary>
public class InventoryReleasedEvent : PaymentEventBase
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid? ReservationId { get; set; }
    public string ReleaseReason { get; set; } = string.Empty;
    public List<InventoryConfirmationItem> Items { get; set; } = new();
    public DateTime ReleasedAt { get; set; }
}
