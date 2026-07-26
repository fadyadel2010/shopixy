namespace PointOfSale.Core.Analytics;

/// <summary>
/// A single step in a conversion funnel.
/// 
/// Each step represents a required event that users must complete
/// to progress through the funnel.
/// </summary>
public class AnalyticsFunnelStep
{
    /// <summary>
    /// Order of this step in the funnel (1-indexed).
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// The analytics event type that marks this step as complete.
    /// </summary>
    public AnalyticsEventType EventType { get; set; }

    /// <summary>
    /// Display name for this step.
    /// Example: "View Product", "Add to Cart"
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of this step.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Creates a funnel step.
    /// </summary>
    public static AnalyticsFunnelStep Create(int order, AnalyticsEventType eventType, string name)
    {
        return new AnalyticsFunnelStep
        {
            Order = order,
            EventType = eventType,
            Name = name
        };
    }
}

/// <summary>
/// Definition of a conversion funnel.
/// 
/// Funnels are composable definitions that can be computed
/// against Redis counters to show drop-off rates.
/// 
/// EXAMPLES:
/// - Purchase Funnel: PageView → ProductView → AddToCart → BeginCheckout → PurchaseCompleted
/// - Discovery Funnel: PageView → ProductImpression → ProductView → AddToCart
/// - Checkout Funnel: BeginCheckout → PurchaseCompleted
/// </summary>
public class AnalyticsFunnelDefinition
{
    /// <summary>
    /// Unique identifier for this funnel.
    /// Example: "purchase_funnel", "discovery_funnel"
    /// </summary>
    public string FunnelKey { get; set; } = string.Empty;

    /// <summary>
    /// Display name for this funnel.
    /// Example: "Purchase Funnel"
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this funnel measures.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The ordered steps in this funnel.
    /// Must have at least 2 steps.
    /// </summary>
    public IReadOnlyList<AnalyticsFunnelStep> Steps { get; set; } = Array.Empty<AnalyticsFunnelStep>();

    /// <summary>
    /// Whether this funnel is a system default (cannot be deleted).
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Validates the funnel definition.
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(FunnelKey) &&
               !string.IsNullOrEmpty(Name) &&
               Steps.Count >= 2;
    }
}
