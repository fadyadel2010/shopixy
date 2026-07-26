namespace PointOfSale.Core.Events;

#region Coupon Events

/// <summary>
/// Event raised when a new coupon is created.
/// Triggers CouponProjectionConsumer to create CouponDocument in MongoDB.
/// </summary>
public class CouponCreatedEvent : IntegrationEvent
{
    public Guid CouponId { get; set; }
    public string Code { get; set; } = string.Empty;
    
    /// <summary>
    /// Discount type: "percentage" or "fixed"
    /// </summary>
    public string DiscountType { get; set; } = "percentage";
    
    /// <summary>
    /// Discount value (percentage 0-100 or fixed amount)
    /// </summary>
    public decimal DiscountValue { get; set; }
    
    /// <summary>
    /// Maximum discount amount for percentage coupons (cap)
    /// </summary>
    public decimal? MaxDiscountAmount { get; set; }
    
    /// <summary>
    /// Minimum order amount required to use this coupon
    /// </summary>
    public decimal? MinOrderAmount { get; set; }
    
    /// <summary>
    /// Maximum total uses for this coupon (null = unlimited)
    /// </summary>
    public int? MaxUsage { get; set; }
    
    /// <summary>
    /// Current usage count
    /// </summary>
    public int CurrentUsage { get; set; }
    
    /// <summary>
    /// Maximum uses per customer (null = unlimited)
    /// </summary>
    public int? MaxUsagePerCustomer { get; set; }
    
    /// <summary>
    /// Whether this coupon is only valid for first orders
    /// </summary>
    public bool FirstOrderOnly { get; set; }
    
    /// <summary>
    /// Coupon validity start date (null = immediately valid)
    /// </summary>
    public DateTime? StartDate { get; set; }
    
    /// <summary>
    /// Coupon validity end date (null = never expires)
    /// </summary>
    public DateTime? EndDate { get; set; }
    
    /// <summary>
    /// Whether the coupon is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Description for display purposes
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Optional: Product IDs this coupon applies to (null = all products)
    /// </summary>
    public List<Guid>? ApplicableProductIds { get; set; }
    
    /// <summary>
    /// Optional: Category IDs this coupon applies to (null = all categories)
    /// </summary>
    public List<Guid>? ApplicableCategoryIds { get; set; }

    /// <summary>
    /// Optional: Product IDs this coupon must never apply to.
    /// </summary>
    public List<Guid>? ExcludedProductIds { get; set; }
}

/// <summary>
/// Event raised when a coupon is updated.
/// Triggers CouponProjectionConsumer to update CouponDocument in MongoDB.
/// </summary>
public class CouponUpdatedEvent : IntegrationEvent
{
    public Guid CouponId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DiscountType { get; set; } = "percentage";
    public decimal DiscountValue { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public int? MaxUsage { get; set; }
    public int CurrentUsage { get; set; }
    public int? MaxUsagePerCustomer { get; set; }
    public bool FirstOrderOnly { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    public string? Description { get; set; }
    public List<Guid>? ApplicableProductIds { get; set; }
    public List<Guid>? ApplicableCategoryIds { get; set; }
    public List<Guid>? ExcludedProductIds { get; set; }
}

/// <summary>
/// Event raised when a coupon is deleted (soft or hard delete).
/// Triggers CouponProjectionConsumer to remove CouponDocument from MongoDB.
/// </summary>
public class CouponDeletedEvent : IntegrationEvent
{
    public Guid CouponId { get; set; }
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// Event raised when coupon usage changes (after order placement).
/// Triggers CouponProjectionConsumer to update CurrentUsage in MongoDB.
/// 
/// CRITICAL: This event MUST be published after a coupon is successfully used.
/// This is the ONLY way CurrentUsage is synchronized with MongoDB.
/// </summary>
public class CouponUsageChangedEvent : IntegrationEvent
{
    public Guid CouponId { get; set; }
    public string Code { get; set; } = string.Empty;
    
    /// <summary>
    /// The new current usage count
    /// </summary>
    public int CurrentUsage { get; set; }
    
    /// <summary>
    /// The order that triggered this usage
    /// </summary>
    public Guid? OrderId { get; set; }
    
    /// <summary>
    /// The customer who used the coupon (if authenticated)
    /// </summary>
    public Guid? CustomerId { get; set; }
}

#endregion
