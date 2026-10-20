using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.Payments;

/// <summary>
/// PHASE 11.4.2.A — PAYMENT ALLOCATION SERVICE
/// 
/// Allocates payment amounts to individual OrderItems (variants).
/// 
/// CRITICAL RULES:
/// - MONEY IS TRACKED ON ORDER ITEMS (VARIANTS), NOT ON ORDERS
/// - Payments are distributed across items in order (by Id)
/// - Each SKU knows exactly how much money was paid for it
/// - This enables: Partial payments, Partial refunds, SKU-level disputes
/// </summary>
public interface IPaymentAllocationService
{
    /// <summary>
    /// Allocates a payment amount across order items.
    /// Distributes to items in order (by Id), filling each item's accepted net balance before moving to next.
    /// </summary>
    /// <param name="order">Order with Items loaded</param>
    /// <param name="paymentAmount">Amount to allocate</param>
    /// <returns>Allocation result with per-item breakdown</returns>
    PaymentAllocationResult AllocatePayment(Order order, decimal paymentAmount);
    
    /// <summary>
    /// Allocates a refund amount across order items.
    /// Distributes to items in order (by Id), up to each item's PaidAmount.
    /// </summary>
    /// <param name="order">Order with Items loaded</param>
    /// <param name="refundAmount">Amount to refund</param>
    /// <returns>Allocation result with per-item breakdown</returns>
    RefundAllocationResult AllocateRefund(Order order, decimal refundAmount);
    
    /// <summary>
    /// Allocates a refund to a specific order item (SKU-level refund).
    /// </summary>
    /// <param name="orderItem">The specific item to refund</param>
    /// <param name="refundAmount">Amount to refund (max: item.PaidAmount - item.RefundedAmount)</param>
    /// <returns>Actual amount refunded</returns>
    decimal AllocateRefundToItem(OrderItem orderItem, decimal refundAmount);
}

/// <summary>
/// Result of payment allocation across order items.
/// </summary>
public class PaymentAllocationResult
{
    public decimal TotalAllocated { get; set; }
    public decimal AmountRemaining { get; set; }
    public List<ItemAllocation> ItemAllocations { get; set; } = new();
    public bool AllItemsFullyPaid { get; set; }
}

/// <summary>
/// Result of refund allocation across order items.
/// </summary>
public class RefundAllocationResult
{
    public decimal TotalRefunded { get; set; }
    public decimal AmountRemaining { get; set; }
    public List<ItemAllocation> ItemAllocations { get; set; } = new();
    public bool AllItemsFullyRefunded { get; set; }
}

/// <summary>
/// Individual item allocation detail.
/// </summary>
public class ItemAllocation
{
    public Guid OrderItemId { get; set; }
    public Guid? VariantId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal ItemSubtotal { get; set; }
    public decimal ItemPaidAmountBefore { get; set; }
    public decimal ItemPaidAmountAfter { get; set; }
}

/// <summary>
/// PHASE 11.4.2.A — PAYMENT ALLOCATION SERVICE IMPLEMENTATION
/// </summary>
public class PaymentAllocationService : IPaymentAllocationService
{
    private readonly ILogger<PaymentAllocationService> _logger;

    public PaymentAllocationService(ILogger<PaymentAllocationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public PaymentAllocationResult AllocatePayment(Order order, decimal paymentAmount)
    {
        var result = new PaymentAllocationResult
        {
            AmountRemaining = paymentAmount
        };

        if (order.Items == null || !order.Items.Any())
        {
            _logger.LogWarning(
                "Cannot allocate payment - order has no items | OrderId: {OrderId}",
                order.Id);
            return result;
        }

        var amountToApply = paymentAmount;

        // PHASE 11.4.2.A: Allocate to items in order (by Id)
        // This ensures consistent allocation across partial payments
        foreach (var item in order.Items.OrderBy(i => i.Id))
        {
            if (amountToApply <= 0) break;

            var remaining = item.RemainingAmount;
            if (remaining <= 0) continue;

            var applied = Math.Min(remaining, amountToApply);
            var paidBefore = item.PaidAmount;

            // Apply payment to item
            item.PaidAmount += applied;
            amountToApply -= applied;

            result.ItemAllocations.Add(new ItemAllocation
            {
                OrderItemId = item.Id,
                VariantId = item.VariantId,
                SKU = item.SKU,
                Amount = applied,
                ItemSubtotal = item.Subtotal,
                ItemPaidAmountBefore = paidBefore,
                ItemPaidAmountAfter = item.PaidAmount
            });

            _logger.LogDebug(
                "Payment allocated to item | OrderItemId: {OrderItemId} | SKU: {SKU} | Amount: {Amount} | PaidAfter: {PaidAfter}/{Subtotal}",
                item.Id, item.SKU, applied, item.PaidAmount, item.Subtotal);
        }

        result.TotalAllocated = paymentAmount - amountToApply;
        result.AmountRemaining = amountToApply;
        result.AllItemsFullyPaid = order.Items.All(i => i.IsFullyPaid);

        _logger.LogInformation(
            "Payment allocation complete | OrderId: {OrderId} | Allocated: {Allocated} | Remaining: {Remaining} | AllPaid: {AllPaid}",
            order.Id, result.TotalAllocated, result.AmountRemaining, result.AllItemsFullyPaid);

        return result;
    }

    /// <inheritdoc />
    public RefundAllocationResult AllocateRefund(Order order, decimal refundAmount)
    {
        var result = new RefundAllocationResult
        {
            AmountRemaining = refundAmount
        };

        if (order.Items == null || !order.Items.Any())
        {
            _logger.LogWarning(
                "Cannot allocate refund - order has no items | OrderId: {OrderId}",
                order.Id);
            return result;
        }

        var amountToRefund = refundAmount;

        // PHASE 11.4.2.A: Refund from items in order (by Id)
        // Refund up to each item's net paid amount
        foreach (var item in order.Items.OrderBy(i => i.Id))
        {
            if (amountToRefund <= 0) break;

            var refundable = item.PaidAmount - item.RefundedAmount;
            if (refundable <= 0) continue;

            var refunded = Math.Min(refundable, amountToRefund);
            var refundedBefore = item.RefundedAmount;

            // Apply refund to item
            item.RefundedAmount += refunded;
            amountToRefund -= refunded;

            result.ItemAllocations.Add(new ItemAllocation
            {
                OrderItemId = item.Id,
                VariantId = item.VariantId,
                SKU = item.SKU,
                Amount = refunded,
                ItemSubtotal = item.Subtotal,
                ItemPaidAmountBefore = refundedBefore,
                ItemPaidAmountAfter = item.RefundedAmount
            });

            _logger.LogDebug(
                "Refund allocated to item | OrderItemId: {OrderItemId} | SKU: {SKU} | Amount: {Amount} | RefundedAfter: {RefundedAfter}",
                item.Id, item.SKU, refunded, item.RefundedAmount);
        }

        result.TotalRefunded = refundAmount - amountToRefund;
        result.AmountRemaining = amountToRefund;
        result.AllItemsFullyRefunded = order.Items.All(i => i.IsFullyRefunded);

        _logger.LogInformation(
            "Refund allocation complete | OrderId: {OrderId} | Refunded: {Refunded} | Remaining: {Remaining} | AllRefunded: {AllRefunded}",
            order.Id, result.TotalRefunded, result.AmountRemaining, result.AllItemsFullyRefunded);

        return result;
    }

    /// <inheritdoc />
    public decimal AllocateRefundToItem(OrderItem orderItem, decimal refundAmount)
    {
        var maxRefundable = orderItem.PaidAmount - orderItem.RefundedAmount;
        var actualRefund = Math.Min(refundAmount, maxRefundable);

        if (actualRefund <= 0)
        {
            _logger.LogWarning(
                "Cannot refund item - no refundable amount | OrderItemId: {OrderItemId} | Paid: {Paid} | AlreadyRefunded: {Refunded}",
                orderItem.Id, orderItem.PaidAmount, orderItem.RefundedAmount);
            return 0;
        }

        orderItem.RefundedAmount += actualRefund;

        _logger.LogInformation(
            "Item refunded | OrderItemId: {OrderItemId} | SKU: {SKU} | Refunded: {Amount} | TotalRefunded: {TotalRefunded}",
            orderItem.Id, orderItem.SKU, actualRefund, orderItem.RefundedAmount);

        return actualRefund;
    }
}
