namespace PointOfSale.Core.Enums;

/// <summary>
/// Type of discount for coupons and promotions
/// </summary>
public enum DiscountType
{
    /// <summary>Percentage discount (e.g., 10% off)</summary>
    Percentage = 0,
    
    /// <summary>Fixed amount discount (e.g., $10 off)</summary>
    FixedAmount = 1
}
