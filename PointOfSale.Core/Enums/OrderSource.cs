namespace PointOfSale.Core.Enums;

/// <summary>
/// Source of the order (which channel it came from)
/// </summary>
public enum OrderSource
{
    /// <summary>Order from e-commerce website</summary>
    Ecommerce = 0,
    
    /// <summary>Order from Point of Sale</summary>
    POS = 1,
    
    /// <summary>Order imported from Amazon</summary>
    Amazon = 2,
    
    /// <summary>Order from other marketplace</summary>
    Marketplace = 3,
    
    /// <summary>Manual order entry</summary>
    Manual = 4
}
