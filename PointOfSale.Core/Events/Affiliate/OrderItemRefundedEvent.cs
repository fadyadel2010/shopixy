namespace PointOfSale.Core.Events.Affiliate;

/// <summary>
/// AF-2: Event published when an individual order item is refunded.
///
/// Currently the platform's refund flow is order-level (OrderRefundedEvent).
/// This event bridges the gap by providing per-item granularity required
/// by the Affiliate Commission Ledger.
///
/// WHEN TO USE:
/// - Full refund: the OrderRefundedConsumer publishes one of these per paid OrderItem
/// - Partial (item-level) refund: future refund service publishes directly
///
/// SEMANTICS:
/// - RefundedAmount: Transaction-scoped (incremental) - the amount being refunded in THIS transaction
/// - CumulativeRefundedAmount: Total refunded amount across all transactions for this item
///
/// IDEMPOTENCY: Consumer calls AffiliateService.ReverseCommissionForOrderItemAsync(),
/// which is idempotent via DB unique constraints on the ledger.
/// </summary>
public class OrderItemRefundedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string RefundId { get; set; } = string.Empty;
    public Guid OrderItemId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    
    /// <summary>
    /// The refund amount for THIS specific transaction (transaction-scoped, incremental).
    /// This is the amount being refunded in the current refund operation.
    /// </summary>
    public decimal RefundedAmount { get; set; }
    
    /// <summary>
    /// The total cumulative refunded amount for this order item across all refund transactions.
    /// This includes the current transaction plus any previous refunds.
    /// </summary>
    public decimal CumulativeRefundedAmount { get; set; }
    
    public string Reason { get; set; } = string.Empty;
    public DateTime RefundedAt { get; set; }

    public Guid GetAuthoritativeAggregateId() => OrderId;
}
