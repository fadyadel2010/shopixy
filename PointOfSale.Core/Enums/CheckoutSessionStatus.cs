namespace PointOfSale.Core.Enums;

/// <summary>
/// Status of a checkout session (NOT an order).
/// 
/// LIFECYCLE:
/// 1. Active: Customer is filling checkout fields
/// 2. Abandoned: Session idle beyond threshold (30 minutes default)
/// 3. ConvertedToOrder: Customer completed checkout successfully
/// 
/// DESIGN RULES:
/// - CheckoutSession is NOT a financial record
/// - Orders remain the only authoritative purchase entity
/// - This entity captures leads only
/// </summary>
public enum CheckoutSessionStatus
{
    /// <summary>
    /// Customer is actively filling checkout fields.
    /// Session is being updated via progressive autosave.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Customer left without completing checkout.
    /// Detected by background scanner when LastActivityAtUtc + threshold < now.
    /// </summary>
    Abandoned = 1,

    /// <summary>
    /// Customer completed checkout and an order was created.
    /// Terminal state - no further transitions allowed.
    /// </summary>
    ConvertedToOrder = 2
}
