namespace PointOfSale.Core.Enums;

/// <summary>
/// Distinguishes between different types of promotions in the unified engine
/// </summary>
public enum PromotionType
{
    /// <summary>
    /// Automatic offer (no code required)
    /// Applied automatically at checkout based on cart contents
    /// </summary>
    Offer = 0,
    
    /// <summary>
    /// Code-based coupon
    /// Requires customer to enter a promo code
    /// </summary>
    Coupon = 1
}
