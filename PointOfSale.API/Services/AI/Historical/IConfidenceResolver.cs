using PointOfSale.API.Services.AI.Models;

namespace PointOfSale.API.Services.AI.Historical;

/// <summary>
/// Resolves data confidence level for historical context.
/// PHASE 5: Historical Consistency & Confidence Layer.
/// 
/// This service determines how confident we are in the data
/// based on order creation date and event completeness.
/// </summary>
public interface IConfidenceResolver
{
    /// <summary>
    /// Resolve confidence level for an order.
    /// </summary>
    /// <param name="orderCreatedAt">When the order was created</param>
    /// <param name="featureTimeline">When features were enabled</param>
    /// <param name="actualEventCount">Number of events recorded</param>
    /// <param name="expectedEventCount">Number of events expected</param>
    ConfidenceResult ResolveConfidence(
        DateTime orderCreatedAt,
        FeatureTimeline featureTimeline,
        int actualEventCount,
        int expectedEventCount);

    /// <summary>
    /// Build full historical context for an order.
    /// </summary>
    HistoricalContext BuildHistoricalContext(
        DateTime orderCreatedAt,
        FeatureTimeline featureTimeline,
        int actualEventCount,
        int expectedEventCount);
}
