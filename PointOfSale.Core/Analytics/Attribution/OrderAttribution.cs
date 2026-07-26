namespace PointOfSale.Core.Analytics.Attribution;

/// <summary>
/// Order-level attribution linking an order to its marketing source.
/// 
/// CRITICAL RULES:
/// - This record is IMMUTABLE after creation
/// - Created when PurchaseCompleted event is processed
/// - Links OrderId to session attribution context
/// - Supports both FirstTouch and LastTouch models
/// 
/// STORAGE:
/// - Primary: MongoDB (append-only, permanent record)
/// - Cache: Redis (for quick dashboard lookups)
/// 
/// QUERY PATTERNS:
/// - By TenantId + OrderId (single order lookup)
/// - By TenantId + Source + DateRange (ROI by source)
/// - By TenantId + Campaign + DateRange (campaign ROI)
/// </summary>
public class OrderAttribution
{
    /// <summary>
    /// The attributed order ID.
    /// Links to the Order entity/document.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Tenant identifier for multi-tenant isolation.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// POS-owned session identifier that made the purchase.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// The attribution model used for this record.
    /// Determines which touchpoint received credit.
    /// </summary>
    public AttributionModel Model { get; set; }

    /// <summary>
    /// Attributed traffic source.
    /// </summary>
    public string Source { get; set; } = "direct";

    /// <summary>
    /// Attributed marketing medium.
    /// </summary>
    public string Medium { get; set; } = "none";

    /// <summary>
    /// Attributed campaign name (if any).
    /// </summary>
    public string? Campaign { get; set; }

    /// <summary>
    /// Attributed content/creative (if any).
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Attributed search term (if any).
    /// </summary>
    public string? Term { get; set; }

    /// <summary>
    /// Total order value for ROI calculations.
    /// In tenant's base currency.
    /// </summary>
    public decimal OrderValue { get; set; }

    /// <summary>
    /// Currency code for the order value.
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// When this attribution was recorded (UTC).
    /// This is when the order was completed, not when the session started.
    /// </summary>
    public DateTime AttributedAtUtc { get; set; }

    /// <summary>
    /// Optional customer ID if the user was authenticated.
    /// </summary>
    public Guid? CustomerId { get; set; }

    /// <summary>
    /// Time between first session visit and purchase.
    /// Useful for understanding consideration time by channel.
    /// </summary>
    public TimeSpan? TimeToConversion { get; set; }

    /// <summary>
    /// Creates an OrderAttribution from an AttributionContext.
    /// </summary>
    public static OrderAttribution FromContext(
        AttributionContext context,
        Guid orderId,
        decimal orderValue,
        string currency,
        AttributionModel model)
    {
        return new OrderAttribution
        {
            OrderId = orderId,
            TenantId = context.TenantId,
            SessionId = context.SessionId,
            Model = model,
            Source = context.Source,
            Medium = context.Medium,
            Campaign = context.Campaign,
            Content = context.Content,
            Term = context.Term,
            OrderValue = orderValue,
            Currency = currency,
            CustomerId = context.CustomerId,
            AttributedAtUtc = DateTime.UtcNow,
            TimeToConversion = DateTime.UtcNow - context.CapturedAtUtc
        };
    }
}
