namespace PointOfSale.Core.Analytics;

/// <summary>
/// Metrics that can be monitored for alert rules.
/// All metrics are computed from Redis counters written in Phase 1.
/// 
/// RULES:
/// - NO MongoDB reads during evaluation
/// - NO recomputation from raw events
/// - Sliding time windows ONLY
/// </summary>
public enum AnalyticsMetric
{
    /// <summary>
    /// Total page views in the window.
    /// Source: analytics:tenant:{id}:counter:pageview:{window}
    /// </summary>
    PageViews = 1,

    /// <summary>
    /// Total add-to-cart events in the window.
    /// Source: analytics:tenant:{id}:counter:addtocart:{window}
    /// </summary>
    AddToCart = 2,

    /// <summary>
    /// Total checkout started events in the window.
    /// Source: analytics:tenant:{id}:counter:begincheckout:{window}
    /// </summary>
    BeginCheckout = 3,

    /// <summary>
    /// Total completed purchases in the window.
    /// Source: analytics:tenant:{id}:counter:purchasecompleted:{window}
    /// </summary>
    PurchaseCompleted = 4,

    /// <summary>
    /// Total payment failure events in the window.
    /// Source: analytics:tenant:{id}:counter:paymentfailed:{window}
    /// </summary>
    PaymentFailed = 5,

    /// <summary>
    /// Cart abandonment rate: (BeginCheckout - PurchaseCompleted) / BeginCheckout * 100
    /// Derived from existing counters.
    /// </summary>
    CartAbandonmentRate = 6,

    /// <summary>
    /// Conversion rate: PurchaseCompleted / PageViews * 100
    /// Derived from existing counters.
    /// </summary>
    ConversionRate = 7,

    /// <summary>
    /// Product view to cart rate: AddToCart / ProductViews * 100
    /// </summary>
    ProductToCartRate = 8,

    /// <summary>
    /// Total product view events in the window.
    /// Source: analytics:tenant:{id}:counter:productview:{window}
    /// </summary>
    ProductViews = 9
}

/// <summary>
/// Comparison operators for alert rule conditions.
/// </summary>
public enum AnalyticsComparisonOperator
{
    /// <summary>
    /// Alert when metric > threshold
    /// Use case: "Alert when payment failures > 10"
    /// </summary>
    GreaterThan = 1,

    /// <summary>
    /// Alert when metric < threshold
    /// Use case: "Alert when purchases < 1 in 30 minutes"
    /// </summary>
    LessThan = 2,

    /// <summary>
    /// Alert when metric == threshold
    /// Use case: "Alert when purchases == 0"
    /// </summary>
    Equal = 3,

    /// <summary>
    /// Alert when metric >= threshold
    /// </summary>
    GreaterThanOrEqual = 4,

    /// <summary>
    /// Alert when metric <= threshold
    /// </summary>
    LessThanOrEqual = 5
}

/// <summary>
/// Alert severity levels.
/// </summary>
public enum AnalyticsAlertSeverity
{
    /// <summary>
    /// Informational - good to know.
    /// </summary>
    Info = 1,

    /// <summary>
    /// Warning - requires attention.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Critical - immediate action required.
    /// </summary>
    Critical = 3
}
