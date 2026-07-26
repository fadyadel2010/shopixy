namespace PointOfSale.Core.Events;

/// <summary>
/// Event raised when a coupon is consumed (used in a successful order).
/// 
/// ARCHITECTURE:
/// - Published ONLY after successful order creation with coupon
/// - Consumed by CouponProjectionConsumer to update MongoDB CurrentUsage
/// - Carries the SQL usage counter committed atomically with CouponUsage
/// 
/// CRITICAL: This replaces the broken CouponUsageChangedEvent flow that
/// was counting Orders where CouponId was never set.
/// </summary>
public class CouponConsumedEvent : IntegrationEvent
{
    /// <summary>
    /// The CouponUsage record ID (source of truth)
    /// </summary>
    public Guid CouponUsageId { get; set; }
    
    /// <summary>
    /// The coupon that was consumed
    /// </summary>
    public Guid CouponId { get; set; }
    
    /// <summary>
    /// The coupon code (snapshot)
    /// </summary>
    public string CouponCode { get; set; } = string.Empty;
    
    /// <summary>
    /// The order where the coupon was used
    /// </summary>
    public Guid OrderId { get; set; }
    
    /// <summary>
    /// The order number (for logging/debugging)
    /// </summary>
    public string? OrderNumber { get; set; }
    
    /// <summary>
    /// The customer who used the coupon (null for guest)
    /// </summary>
    public Guid? CustomerId { get; set; }
    
    /// <summary>
    /// The discount amount applied
    /// </summary>
    public decimal DiscountAmount { get; set; }
    
    /// <summary>
    /// NEW total usage count for this coupon.
    /// This value is committed atomically with the CouponUsage record in SQL.
    /// </summary>
    public int NewTotalUsageCount { get; set; }
    
    /// <summary>
    /// Usage count for this specific customer (for MaxPerCustomer tracking)
    /// </summary>
    public int CustomerUsageCount { get; set; }
}
