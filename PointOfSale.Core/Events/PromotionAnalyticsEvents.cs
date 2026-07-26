namespace PointOfSale.Core.Events;

/// <summary>
/// Promotion analytics events for measuring promotion effectiveness.
/// Phase: Shopify-Grade Promotion-Aware Search Enhancements
/// 
/// DESIGN PRINCIPLES:
/// - Lightweight: Minimal payload for high-volume events
/// - Non-blocking: Fire-and-forget, no synchronous dependencies
/// - Event-sourced: Can be replayed for analytics
/// - Privacy-aware: No PII, only anonymized identifiers
/// 
/// CONSUMERS:
/// - Future analytics pipeline (Elasticsearch, ClickHouse, etc.)
/// - Real-time dashboards
/// - A/B testing analysis
/// 
/// NOTE: These events are analytics-only.
/// They do NOT affect business logic or data consistency.
/// </summary>
public class PromotionAnalyticsEvents
{
}

/// <summary>
/// Emitted when a promoted product appears in search results.
/// Used to measure promotion visibility/impressions.
/// 
/// Trigger: Product with HasActivePromotion=true returned in search
/// Frequency: High volume - may need sampling in production
/// </summary>
public class PromotionSeenEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent
{
    /// <summary>
    /// Tenant ID for isolation
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// The promotion that was seen
    /// </summary>
    public Guid PromotionId { get; set; }
    
    /// <summary>
    /// Product where promotion was displayed
    /// </summary>
    public Guid ProductId { get; set; }
    
    /// <summary>
    /// Search query that triggered the impression (anonymized)
    /// Null if from category listing
    /// </summary>
    public string? SearchQuery { get; set; }
    
    /// <summary>
    /// Position in search results (1-indexed)
    /// </summary>
    public int ResultPosition { get; set; }
    
    /// <summary>
    /// Page number where promotion appeared
    /// </summary>
    public int PageNumber { get; set; }
    
    /// <summary>
    /// Source of the impression: "search", "category", "homepage"
    /// </summary>
    public string Source { get; set; } = "search";
    
    /// <summary>
    /// Anonymous session identifier for funnel tracking
    /// NOT customer ID for privacy
    /// </summary>
    public string? SessionId { get; set; }
    
    /// <summary>
    /// When the promotion was seen
    /// </summary>
    public DateTime SeenAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Emitted when a customer clicks on a promoted product.
/// Used to measure promotion engagement/CTR.
/// 
/// Trigger: Click on product card with HasActivePromotion=true
/// Note: This requires frontend to emit event, backend logs it
/// </summary>
public class PromotionClickedEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent
{
    /// <summary>
    /// Tenant ID for isolation
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// The promotion that was clicked
    /// </summary>
    public Guid PromotionId { get; set; }
    
    /// <summary>
    /// Product that was clicked
    /// </summary>
    public Guid ProductId { get; set; }
    
    /// <summary>
    /// Search query that led to the click (anonymized)
    /// Null if from category listing
    /// </summary>
    public string? SearchQuery { get; set; }
    
    /// <summary>
    /// Position in search results when clicked
    /// </summary>
    public int ResultPosition { get; set; }
    
    /// <summary>
    /// Source of the click: "search", "category", "homepage", "cart"
    /// </summary>
    public string Source { get; set; } = "search";
    
    /// <summary>
    /// Anonymous session identifier for funnel tracking
    /// </summary>
    public string? SessionId { get; set; }
    
    /// <summary>
    /// When the promotion was clicked
    /// </summary>
    public DateTime ClickedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Emitted when a promoted product is purchased.
/// Used to measure promotion conversion rate.
/// 
/// Trigger: Order placed containing product with active promotion
/// This is the ultimate success metric for promotion effectiveness.
/// </summary>
public class PromotionConvertedEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent
{
    /// <summary>
    /// Tenant ID for isolation
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// The promotion that drove the conversion
    /// </summary>
    public Guid PromotionId { get; set; }
    
    /// <summary>
    /// Product that was purchased
    /// </summary>
    public Guid ProductId { get; set; }
    
    /// <summary>
    /// Variant that was purchased (for SKU-level tracking)
    /// </summary>
    public Guid? VariantId { get; set; }
    
    /// <summary>
    /// Order ID for reference
    /// </summary>
    public Guid OrderId { get; set; }
    
    /// <summary>
    /// Quantity purchased
    /// </summary>
    public int Quantity { get; set; }
    
    /// <summary>
    /// Original price before discount
    /// </summary>
    public decimal OriginalPrice { get; set; }
    
    /// <summary>
    /// Discount amount applied by this promotion
    /// </summary>
    public decimal DiscountAmount { get; set; }
    
    /// <summary>
    /// Final price paid (after discount)
    /// </summary>
    public decimal FinalPrice { get; set; }
    
    /// <summary>
    /// Anonymous session identifier for funnel tracking
    /// </summary>
    public string? SessionId { get; set; }
    
    /// <summary>
    /// When the conversion occurred
    /// </summary>
    public DateTime ConvertedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Emitted when a promotion boost is applied to search ranking.
/// Used to measure promotion impact on search visibility.
/// 
/// Trigger: Elasticsearch query applies promotion boost
/// Frequency: Lower volume (per-query, not per-result)
/// </summary>
public class PromotionBoostAppliedEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent
{
    /// <summary>
    /// Tenant ID for isolation
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Search query that triggered the boost
    /// </summary>
    public string? SearchQuery { get; set; }
    
    /// <summary>
    /// Number of promoted products in results
    /// </summary>
    public int PromotedProductCount { get; set; }
    
    /// <summary>
    /// Total products in results
    /// </summary>
    public int TotalProductCount { get; set; }
    
    /// <summary>
    /// Average boost score applied
    /// </summary>
    public double AverageBoostScore { get; set; }
    
    /// <summary>
    /// Whether time decay was applied
    /// </summary>
    public bool TimeDecayApplied { get; set; }
    
    /// <summary>
    /// Search duration in milliseconds
    /// </summary>
    public long SearchTimeMs { get; set; }
    
    /// <summary>
    /// When the boost was applied
    /// </summary>
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
}
