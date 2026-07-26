namespace PointOfSale.Core.Analytics.Attribution;

/// <summary>
/// Attribution model type for determining which touchpoint gets credit.
/// 
/// MVP SCOPE:
/// - FirstTouch: First interaction with the store gets all credit
/// - LastTouch: Last interaction before purchase gets all credit
/// 
/// FUTURE (NOT MVP):
/// - Linear: Equal credit across all touchpoints
/// - TimeDecay: More recent touchpoints get more credit
/// - PositionBased: First and last get more credit
/// - DataDriven: ML-based attribution
/// </summary>
public enum AttributionModel
{
    /// <summary>
    /// First touchpoint gets 100% of the credit.
    /// Use case: Understanding which channels bring new customers.
    /// </summary>
    FirstTouch = 1,

    /// <summary>
    /// Last touchpoint gets 100% of the credit.
    /// Use case: Understanding which channels close the sale.
    /// </summary>
    LastTouch = 2
}
