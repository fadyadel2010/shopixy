using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Tracks individual promotion (offer) applications per order.
/// 
/// ARCHITECTURE (SHOPIFY-GRADE PROMOTIONS ENGINE):
/// - Each successful order with an auto-applied promotion creates ONE PromotionUsage record
/// - This is the SOURCE OF TRUTH for promotion consumption
/// - Used for MaxUsage enforcement (total count per promotion)
/// - Created ONLY after successful order placement (NEVER in cart/preview)
/// 
/// NOTE: This is SEPARATE from CouponUsage which tracks coupon consumption.
/// The PromotionEngine reads both tables to calculate discounts.
/// 
/// CRITICAL RULES:
/// 1. PromotionUsage is IMMUTABLE after creation (no updates, no deletes)
/// 2. If order is cancelled/refunded, PromotionUsage remains
/// 3. All queries MUST filter by TenantId
/// 4. OrderId is UNIQUE - one usage per order per promotion type
/// </summary>
public class PromotionUsage : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this promotion usage.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Reference to the promotion (offer) that was applied.
    /// </summary>
    public Guid PromotionId { get; set; }
    public Promotion? Promotion { get; set; }
    
    /// <summary>
    /// Reference to the order where the promotion was applied.
    /// UNIQUE constraint with PromotionId - one usage per promotion per order.
    /// </summary>
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    
    /// <summary>
    /// The customer who received this promotion (null for guest checkout).
    /// </summary>
    public Guid? CustomerId { get; set; }
    public OnlineCustomer? Customer { get; set; }
    
    /// <summary>
    /// The promotion name that was applied (snapshot at time of use).
    /// Stored for historical reference even if promotion is later deleted.
    /// </summary>
    public string PromotionName { get; set; } = string.Empty;
    
    /// <summary>
    /// The discount amount that was applied.
    /// Stored for historical reference and analytics.
    /// </summary>
    public decimal DiscountAmount { get; set; }
    
    /// <summary>
    /// Order total before discount (for analytics).
    /// </summary>
    public decimal OrderSubtotal { get; set; }
    
    /// <summary>
    /// JSON array of items the promotion was applied to.
    /// Stores ProductId, VariantId, Quantity, OriginalPrice, DiscountAmount.
    /// </summary>
    public string? AppliedToItemsJson { get; set; }
    
    /// <summary>
    /// Timestamp when the promotion was applied.
    /// Set to DateTime.UtcNow at creation.
    /// </summary>
    public DateTime AppliedAtUtc { get; set; }
}
