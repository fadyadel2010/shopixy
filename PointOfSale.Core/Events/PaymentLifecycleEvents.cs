namespace PointOfSale.Core.Events;

/// <summary>
/// PHASE: Payment Lifecycle Architecture
/// 
/// Domain events that trigger payment status changes and journal entries.
/// 
/// CRITICAL: These events are the ONLY way to change PaymentStatus.
/// Controllers must raise these events instead of directly modifying status.
/// </summary>

#region Offline Collection Events

/// <summary>
/// Event raised when an offline-collection order is delivered and funds are collected from the customer.
/// 
/// TRIGGERS:
/// - PaymentStatus → Collected
/// - JOURNAL: DEBIT offline clearing, CREDIT 4020 E-commerce Sales
/// 
/// IDEMPOTENCY KEY: OFFLINE_COLLECTED:{TenantId}:{OrderId}
/// </summary>
public class OfflinePaymentCollectedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal AmountCollected { get; set; }
    
    /// <summary>
    /// Delivery confirmation reference (shipment ID, POD number, etc.)
    /// </summary>
    public string? DeliveryReference { get; set; }
    
    /// <summary>
    /// Who confirmed the delivery (driver ID, system, etc.)
    /// </summary>
    public string? ConfirmedBy { get; set; }
    
    public DateTime DeliveredAt { get; set; }

    public Guid GetAuthoritativeAggregateId() => OrderId;
}

/// <summary>
/// Event raised when a collector settles offline collections with the merchant.
/// 
/// TRIGGERS:
/// - PaymentStatus → Settled
/// - JOURNAL: DEBIT 1030 Bank (net), DEBIT collection fees, CREDIT offline clearing (gross)
/// 
/// IDEMPOTENCY KEY: OFFLINE_SETTLEMENT:{TenantId}:{SettlementId}
/// </summary>
public class OfflineSettlementCompletedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid SettlementBatchId { get; set; }

    /// <summary>
    /// Unique settlement ID from collector
    /// </summary>
    public string SettlementId { get; set; } = string.Empty;
    
    /// <summary>
    /// Gross amount collected from customers
    /// </summary>
    public decimal GrossAmount { get; set; }
    
    /// <summary>
    /// Collection fees deducted
    /// </summary>
    public decimal Fees { get; set; }

    public string Currency { get; set; } = string.Empty;
    
    /// <summary>
    /// Net amount deposited to bank
    /// </summary>
    public decimal NetAmount => GrossAmount - Fees;
    
    /// <summary>
    /// Collector name
    /// </summary>
    public string ShippingCompany { get; set; } = string.Empty;
    
    /// <summary>
    /// Bank account where settlement was deposited
    /// </summary>
    public string? BankAccount { get; set; }
    
    /// <summary>
    /// Order IDs included in this settlement (for reconciliation)
    /// </summary>
    public List<Guid> OrderIds { get; set; } = new();
    
    public DateTime SettlementDate { get; set; }

    public Guid GetAuthoritativeAggregateId() => SettlementBatchId;
}

#endregion

#region Online Payment Events

/// <summary>
/// Event raised when an online payment is captured by a gateway.
/// 
/// TRIGGERS:
/// - PaymentStatus → Collected
/// - JOURNAL: DEBIT 1120 Gateway Clearing, CREDIT 4020 E-commerce Sales
/// 
/// IDEMPOTENCY KEY: GATEWAY_CAPTURE:{TenantId}:{PaymentIntentId}
/// </summary>
public class OnlinePaymentCapturedEvent : IntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Gateway { get; set; } = string.Empty;
    public string PaymentIntentId { get; set; } = string.Empty;
    public decimal AmountCaptured { get; set; }
    public string? Currency { get; set; }
    public DateTime CapturedAt { get; set; }
}

/// <summary>
/// Event raised when a gateway settles funds to the merchant bank account.
/// 
/// TRIGGERS:
/// - PaymentStatus → Settled
/// - JOURNAL: DEBIT 1030 Bank (net), DEBIT 6010 Gateway Fees, CREDIT 1120 Gateway Clearing (gross)
/// 
/// IDEMPOTENCY KEY: GATEWAY_SETTLEMENT:{TenantId}:{SettlementId}
/// </summary>
public class GatewaySettlementCompletedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid SettlementBatchId { get; set; }
    public string SettlementId { get; set; } = string.Empty;
    public string Gateway { get; set; } = string.Empty;
    public string? PayoutBatchId { get; set; }
    public List<Guid> OrderIds { get; set; } = new();
    public List<Guid> PaymentIntentIds { get; set; } = new();
    public decimal GrossAmount { get; set; }
    public decimal Fees { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal NetAmount => GrossAmount - Fees;
    public string? BankReference { get; set; }
    public DateTime SettlementDate { get; set; }

    public Guid GetAuthoritativeAggregateId() => SettlementBatchId;
}

#endregion

#region Payment Review Events

/// <summary>
/// Event raised when a reviewed payment is approved by admin.
/// 
/// TRIGGERS:
/// - PaymentStatus → Collected
/// - JOURNAL: DEBIT provider clearing, CREDIT 4020 E-commerce Sales
/// 
/// IDEMPOTENCY KEY: PAYMENT_REVIEW_APPROVED:{TenantId}:{OrderId}
/// </summary>
public class ReviewedPaymentApprovedEvent : IntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal AmountApproved { get; set; }
    
    /// <summary>
    /// Admin who approved the payment
    /// </summary>
    public string ApprovedBy { get; set; } = string.Empty;
    
    /// <summary>
    /// Transaction reference from provider
    /// </summary>
    public string? ProviderTransactionId { get; set; }
    
    public DateTime ApprovedAt { get; set; }
}

/// <summary>
/// Event raised when a reviewed payment is rejected by admin.
/// 
/// TRIGGERS:
/// - PaymentStatus → Rejected
/// - NO JOURNAL (money never recognized)
/// </summary>
public class ReviewedPaymentRejectedEvent : IntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string RejectedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime RejectedAt { get; set; }
}

/// <summary>
/// Event raised when an online payment fails (gateway rejection, timeout, etc.)
/// 
/// TRIGGERS:
/// - PaymentStatus → Failed
/// - NO JOURNAL (money never recognized)
/// - Inventory released
/// </summary>
public class OnlinePaymentFailedEvent : IntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Gateway { get; set; } = string.Empty;
    public string? PaymentIntentId { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? FailureCode { get; set; }
    public DateTime FailedAt { get; set; }
}

/// <summary>
/// Event raised when a payment is cancelled (by customer or system).
/// 
/// TRIGGERS:
/// - PaymentStatus → Failed or Rejected
/// - Order → Cancelled
/// - NO JOURNAL (money never recognized)
/// - Inventory released
/// </summary>
public class PaymentCancelledEvent : IntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string? Gateway { get; set; }
    public string? PaymentIntentId { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? CancelledBy { get; set; }
    public DateTime CancelledAt { get; set; }
}

#endregion

#region Refund Events

/// <summary>
/// Event raised when an order is refunded.
/// 
/// TRIGGERS:
/// - PaymentStatus → Refunded (full) or PartiallyRefunded (partial)
/// - JOURNAL: DEBIT 4020 E-commerce Sales, CREDIT Clearing
/// 
/// IDEMPOTENCY KEY: ECOM_REFUND:{TenantId}:{OrderId}:{RefundId}
/// </summary>
public class OrderRefundedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Unique refund identifier
    /// </summary>
    public string RefundId { get; set; } = string.Empty;
    
    public decimal RefundAmount { get; set; }
    
    /// <summary>
    /// Whether this is a full refund
    /// </summary>
    public bool IsFullRefund { get; set; }
    
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>
    /// Original payment gateway/method
    /// </summary>
    public string PaymentMethod { get; set; } = string.Empty;
    
    public DateTime RefundedAt { get; set; }

    public Guid GetAuthoritativeAggregateId() => OrderId;
}

#endregion
