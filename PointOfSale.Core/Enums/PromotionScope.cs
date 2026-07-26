namespace PointOfSale.Core.Enums;

/// <summary>
/// Defines the scope of a promotion (what it applies to)
/// </summary>
public enum PromotionScope
{
    /// <summary>
    /// Promotion applies to a specific product
    /// </summary>
    Product = 0,
    
    /// <summary>
    /// Promotion applies to all products in a category
    /// </summary>
    Category = 1,
    
    /// <summary>
    /// Promotion applies to the entire cart/order
    /// </summary>
    Cart = 2
}
