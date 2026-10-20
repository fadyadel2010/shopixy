namespace PointOfSale.Core.Entities;

/// <summary>
/// Order line item entity.
/// 
/// PHASE 11.4.1 — VARIANT-BASED CHECKOUT & INVENTORY RESERVATION
/// 
/// CRITICAL RULES:
/// - A Product is NEVER sold. A ProductVariant (SKU) is ALWAYS sold.
/// - VariantId is the SOURCE OF TRUTH for pricing and stock
/// - ProductId is kept for display/reporting only
/// - All pricing comes from Variant at checkout time (immutable snapshot)
/// - Orders are immutable SKU snapshots
/// 
/// PHASE 11.4.2.A — ORDER ITEM FINANCIAL MODEL & PAYMENT ALLOCATION
/// 
/// CRITICAL RULES:
/// - MONEY IS TRACKED ON ORDER ITEMS (VARIANTS), NOT ON ORDERS
/// - Every SKU knows exactly how much money was paid and refunded for it
/// - Net line balances exclude order-level shipping/fees; Order.Total includes both
/// - This enables: Partial payments, Partial refunds, SKU-level disputes, Accurate COGS
/// </summary>
public class OrderItem : BaseEntity
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    
    /// <summary>
    /// Product ID (for display/reporting only - NOT for pricing).
    /// </summary>
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    
    /// <summary>
    /// Variant ID - the actual sellable SKU.
    /// PHASE 11.4.1: This is the SOURCE OF TRUTH.
    /// All pricing and stock validation came from this variant.
    /// Null only for legacy orders before Phase 11.4.1.
    /// </summary>
    public Guid? VariantId { get; set; }
    public ProductVariant? Variant { get; set; }
    
    /// <summary>
    /// Unit of measure used for this line item
    /// </summary>
    public Guid? UnitId { get; set; }
    public Unit? Unit { get; set; }
    
    /// <summary>
    /// Warehouse from which the item will be fulfilled
    /// </summary>
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    
    /// <summary>
    /// Product/Variant SKU at time of order (denormalized for history).
    /// PHASE 11.4.1: Comes from Variant.SKU.
    /// </summary>
    public string SKU { get; set; } = string.Empty;
    
    /// <summary>
    /// Product name at time of order (denormalized for history)
    /// </summary>
    public string ProductName { get; set; } = string.Empty;
    
    /// <summary>
    /// Variant title/name if applicable (e.g., "Red / XL").
    /// PHASE 11.4.1: Formatted from Variant.Attributes.
    /// </summary>
    public string? VariantName { get; set; }
    
    /// <summary>
    /// Variant attributes as JSON (immutable snapshot).
    /// PHASE 11.4.1: Serialized from Variant.Attributes for historical record.
    /// Example: [{"Name":"Color","Value":"Red"},{"Name":"Size","Value":"XL"}]
    /// </summary>
    public string? VariantAttributesJson { get; set; }
    
    /// <summary>
    /// Product/Variant image URL (denormalized for display).
    /// PHASE 11.4.1: Comes from Variant.ImageUrl or Product fallback.
    /// </summary>
    public string? ImageUrl { get; set; }
    
    /// <summary>
    /// Quantity ordered
    /// </summary>
    public int Quantity { get; set; }
    
    /// <summary>
    /// Unit price at time of order.
    /// PHASE 11.4.1: Comes from Variant.OnlinePrice ?? Variant.Price.
    /// NEVER from Product.Price.
    /// </summary>
    public decimal UnitPrice { get; set; }
    
    /// <summary>
    /// Discount applied to this line item
    /// </summary>
    public decimal DiscountAmount { get; set; }
    
    /// <summary>
    /// Tax amount for this line item
    /// </summary>
    public decimal TaxAmount { get; set; }
    
    /// <summary>
    /// Line item total (Quantity * UnitPrice - Discount + Tax)
    /// </summary>
    public decimal Total { get; set; }
    
    /// <summary>
    /// Cost price at time of order (for profit calculation).
    /// PHASE 11.4.1: Comes from Variant.CostPrice.
    /// </summary>
    public decimal CostPrice { get; set; }
    
    /// <summary>
    /// Any special instructions for this item
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Immutable affiliate commission terms captured at checkout.
    /// Commission recording must use these fields instead of reading current policy.
    /// </summary>
    public bool AffiliateCommissionEligible { get; set; }
    public Guid? AffiliateCommissionPolicyIdSnapshot { get; set; }
    public decimal? AffiliateCommissionRateSnapshot { get; set; }
    public decimal? AffiliateMaxCommissionPerItemSnapshot { get; set; }
    public decimal? AffiliateCommissionBaseAmountSnapshot { get; set; }
    public decimal? AffiliateCommissionAmountSnapshot { get; set; }
    public DateTime? AffiliateCommissionSnapshotCapturedAtUtc { get; set; }
    
    // =============================================================================
    // PHASE 11.4.2.A — FINANCIAL LEDGER FIELDS
    // =============================================================================
    // MONEY IS TRACKED ON ORDER ITEMS (VARIANTS), NOT ON ORDERS
    // Every SKU knows exactly how much money was paid and refunded for it.
    // This enables: Partial payments, Partial refunds, SKU-level disputes
    // =============================================================================
    
    /// <summary>
    /// Computed subtotal for this line item (UnitPrice × Quantity).
    /// Gross merchandise amount before the accepted line discount and tax.
    /// </summary>
    public decimal Subtotal => UnitPrice * Quantity;

    /// <summary>
    /// Accepted line amount due. Checkout already allocates order discounts and
    /// tax into these persisted fields; never subtract the order discount again.
    /// Shipping and payment fees remain order-level charges. Derive from the
    /// pricing fields so legacy lines without a populated Total retain their value.
    /// </summary>
    public decimal PayableAmount => Math.Max(0m, Subtotal - DiscountAmount + TaxAmount);
    
    /// <summary>
    /// How much money has been applied/paid toward this SKU.
    /// PHASE 11.4.2.A: Payments are allocated per OrderItem, not Order.
    /// When payment comes in, it's distributed across items in order.
    /// </summary>
    public decimal PaidAmount { get; set; }
    
    /// <summary>
    /// How much money has been refunded for this SKU.
    /// PHASE 11.4.2.A: Refunds are tracked per OrderItem.
    /// Never exceeds PaidAmount.
    /// </summary>
    public decimal RefundedAmount { get; set; }
    
    /// <summary>
    /// Whether this line item is fully paid.
    /// Computed against the accepted net line amount, not gross merchandise.
    /// </summary>
    public bool IsFullyPaid => PaidAmount >= PayableAmount;
    
    /// <summary>
    /// Whether this line item is fully refunded.
    /// PHASE 11.4.2.A: Computed from RefundedAmount >= PaidAmount.
    /// </summary>
    public bool IsFullyRefunded => RefundedAmount >= PaidAmount;
    
    /// <summary>
    /// Net amount paid for this SKU (after refunds).
    /// PHASE 11.4.2.A: PaidAmount - RefundedAmount.
    /// </summary>
    public decimal NetPaidAmount => PaidAmount - RefundedAmount;
    
    /// <summary>
    /// Remaining amount to be paid for this SKU.
    /// Accepted net line amount less prior allocated payments.
    /// Refunds do not reopen the original collection obligation.
    /// Returns 0 if fully paid.
    /// </summary>
    public decimal RemainingAmount => Math.Max(0m, PayableAmount - PaidAmount);
}
