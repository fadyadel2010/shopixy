namespace PointOfSale.Core.Enums;

/// <summary>
/// E-commerce order status workflow
/// </summary>
public enum OrderStatus
{
    /// <summary>Order created but not yet confirmed</summary>
    Pending = 0,
    
    /// <summary>Order confirmed, awaiting processing</summary>
    Confirmed = 1,
    
    /// <summary>Order is being prepared</summary>
    Processing = 2,
    
    /// <summary>Order has been shipped</summary>
    Shipped = 3,
    
    /// <summary>Order delivered to customer</summary>
    Delivered = 4,
    
    /// <summary>Order completed successfully</summary>
    Completed = 5,
    
    /// <summary>Order was cancelled</summary>
    Cancelled = 6,
    
    /// <summary>Order was refunded</summary>
    Refunded = 7,
    
    /// <summary>Order is on hold (payment issue, stock issue, etc.)</summary>
    OnHold = 8
}
