namespace PointOfSale.Core.Events;

#region Promotion Events (Offers)

/// <summary>
/// Event raised when a new promotion (offer) is created.
/// Triggers PromotionProjectionConsumer to create PromotionDocument in MongoDB
/// and update product documents with promotion badges.
/// </summary>
public class PromotionCreatedEvent : IntegrationEvent
{
    public Guid PromotionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    /// <summary>
    /// Discount type: "percentage" or "fixed"
    /// </summary>
    public string DiscountType { get; set; } = "percentage";
    
    /// <summary>
    /// Discount value (percentage 0-100 or fixed amount)
    /// </summary>
    public decimal DiscountValue { get; set; }
    
    /// <summary>
    /// Maximum discount amount for percentage promotions (cap)
    /// </summary>
    public decimal? MaxDiscountAmount { get; set; }
    
    /// <summary>
    /// Scope: "product", "category", or "cart"
    /// </summary>
    public string Scope { get; set; } = "product";
    
    /// <summary>
    /// The entity this promotion applies to (ProductId or CategoryId)
    /// </summary>
    public Guid? AppliesToId { get; set; }
    
    /// <summary>
    /// Promotion validity start date
    /// </summary>
    public DateTime StartDate { get; set; }
    
    /// <summary>
    /// Promotion validity end date
    /// </summary>
    public DateTime EndDate { get; set; }
    
    /// <summary>
    /// Priority for conflict resolution
    /// </summary>
    public int Priority { get; set; }
    
    /// <summary>
    /// Whether the promotion is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Whether this promotion is exclusive (cannot combine with coupons)
    /// </summary>
    public bool IsExclusive { get; set; }
    
    /// <summary>
    /// Minimum order amount
    /// </summary>
    public decimal? MinOrderAmount { get; set; }
    
    /// <summary>
    /// Maximum usage count
    /// </summary>
    public int? MaxUsage { get; set; }
    
    /// <summary>
    /// Current usage count
    /// </summary>
    public int CurrentUsage { get; set; }
    
    /// <summary>
    /// Banner display style for category promotions: gradient, solid, minimal, festive
    /// </summary>
    public string BannerStyle { get; set; } = "gradient";
    
    /// <summary>
    /// Manual boost override for merchandising control.
    /// When set (non-null), adds to calculated promotion boost in search ranking.
    /// Synced to Elasticsearch for function_score.
    /// </summary>
    public double? SearchBoostOverride { get; set; }
}

/// <summary>
/// Event raised when a promotion is updated.
/// Triggers PromotionProjectionConsumer to update MongoDB documents.
/// </summary>
public class PromotionUpdatedEvent : IntegrationEvent
{
    public Guid PromotionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DiscountType { get; set; } = "percentage";
    public decimal DiscountValue { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public string Scope { get; set; } = "product";
    public Guid? AppliesToId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; }
    public bool IsExclusive { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public int? MaxUsage { get; set; }
    public int CurrentUsage { get; set; }
    public string BannerStyle { get; set; } = "gradient";
    
    /// <summary>
    /// Manual boost override for merchandising control.
    /// </summary>
    public double? SearchBoostOverride { get; set; }
}

/// <summary>
/// Event raised when a promotion is deleted.
/// Triggers PromotionProjectionConsumer to remove from MongoDB.
/// </summary>
public class PromotionDeletedEvent : IntegrationEvent
{
    public Guid PromotionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Scope { get; set; } = "product";
    public Guid? AppliesToId { get; set; }
}

/// <summary>
/// Event raised when a promotion expires (scheduled job or on-demand).
/// Triggers cleanup of promotion badges from product documents.
/// </summary>
public class PromotionExpiredEvent : IntegrationEvent
{
    public Guid PromotionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime ExpiredAt { get; set; }
    public string Scope { get; set; } = "product";
    public Guid? AppliesToId { get; set; }
}

/// <summary>
/// Event raised when a promotion is applied to an order.
/// Used for analytics, usage tracking, and MongoDB projections.
/// </summary>
public class PromotionAppliedEvent : IntegrationEvent
{
    public Guid PromotionId { get; set; }
    public string PromotionName { get; set; } = string.Empty;
    
    /// <summary>
    /// "offer" or "coupon"
    /// </summary>
    public string PromotionType { get; set; } = "offer";
    
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal OrderSubtotal { get; set; }
    
    /// <summary>
    /// New total usage count after this application
    /// </summary>
    public int NewTotalUsageCount { get; set; }
    
    /// <summary>
    /// List of items the promotion was applied to
    /// </summary>
    public List<PromotionAppliedItem> AppliedToItems { get; set; } = new();
}

/// <summary>
/// Represents an item that a promotion was applied to
/// </summary>
public class PromotionAppliedItem
{
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal OriginalPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalPrice { get; set; }
}

/// <summary>
/// Event raised when promotion usage changes.
/// Triggers MongoDB update for CurrentUsage.
/// </summary>
public class PromotionUsageChangedEvent : IntegrationEvent
{
    public Guid PromotionId { get; set; }
    public int CurrentUsage { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? CustomerId { get; set; }
}

#endregion
