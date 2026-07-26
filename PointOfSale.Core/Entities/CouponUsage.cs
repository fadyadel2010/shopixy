using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Tracks individual coupon redemptions (usages) per order.
/// 
/// ARCHITECTURE (SHOPIFY-GRADE COUPON ENGINE):
/// - Each successful order with a coupon creates ONE CouponUsage record
/// - This is the SOURCE OF TRUTH for coupon consumption
/// - Used for MaxUsage enforcement (total count per coupon)
/// - Used for MaxPerCustomer enforcement (count per customer)
/// - Created ONLY after payment success (NEVER in cart/preview/checkout reservation)
/// 
/// CRITICAL RULES:
/// 1. CouponUsage is IMMUTABLE after creation (no updates, no deletes)
/// 2. If order is cancelled/refunded, CouponUsage remains (admin can soft-delete if needed)
/// 3. All queries MUST filter by TenantId
/// 4. OrderId is UNIQUE - one usage per order
/// </summary>
public class CouponUsage : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this coupon usage.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Reference to the coupon that was used.
    /// </summary>
    public Guid CouponId { get; set; }
    public Coupon? Coupon { get; set; }
    
    /// <summary>
    /// Reference to the order where the coupon was used.
    /// UNIQUE constraint - one coupon usage per order.
    /// </summary>
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    
    /// <summary>
    /// The authenticated customer identity that used the coupon.
    /// Stored as a durable identity value for MaxPerCustomer enforcement and audit history.
    /// </summary>
    public Guid? CustomerId { get; set; }
    
    /// <summary>
    /// The coupon code that was used (snapshot at time of use).
    /// Stored for historical reference even if coupon is later deleted.
    /// </summary>
    public string CouponCode { get; set; } = string.Empty;
    
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
    /// Timestamp when the coupon was consumed.
    /// Set to DateTime.UtcNow at creation.
    /// </summary>
    public DateTime UsedAtUtc { get; set; }
}
