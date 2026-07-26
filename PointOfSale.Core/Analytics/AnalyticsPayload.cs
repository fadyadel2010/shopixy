namespace PointOfSale.Core.Analytics;

/// <summary>
/// Represents WHAT exactly happened in the analytics event.
/// 
/// DESIGN PRINCIPLES:
/// - Typed fields for common analytics data
/// - NO provider-specific fields (GA, Meta, etc.)
/// - All fields are POS-native
/// - Extra dictionary for extensibility
/// 
/// FIELD USAGE BY EVENT TYPE:
/// - PageView: (none required)
/// - ProductImpression: ProductId, Position (in Extra)
/// - ProductView: ProductId, VariantId
/// - SearchPerformed: SearchQuery, ResultCount (in Extra)
/// - FilterApplied: Filters
/// - AddToCart: ProductId, VariantId, Quantity, Value
/// - RemoveFromCart: ProductId, VariantId, Quantity
/// - BeginCheckout: Value
/// - PurchaseCompleted: OrderId, Value
/// - PaymentFailed: OrderId (optional), error in Extra
/// </summary>
public class AnalyticsPayload
{
    /// <summary>
    /// Product ID for product-related events.
    /// </summary>
    public Guid? ProductId { get; set; }

    /// <summary>
    /// Product variant ID for variant-specific events.
    /// Required for AddToCart/RemoveFromCart when product has variants.
    /// </summary>
    public Guid? VariantId { get; set; }

    /// <summary>
    /// Order ID for purchase-related events.
    /// </summary>
    public Guid? OrderId { get; set; }

    /// <summary>
    /// Category ID for category-related events.
    /// </summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// Monetary value in tenant's currency.
    /// Used for: Product price, cart total, order total.
    /// </summary>
    public decimal? Value { get; set; }

    /// <summary>
    /// Quantity for cart events (AddToCart, RemoveFromCart).
    /// </summary>
    public int? Quantity { get; set; }

    /// <summary>
    /// Search query string for SearchPerformed events.
    /// </summary>
    public string? SearchQuery { get; set; }

    /// <summary>
    /// Applied filters for FilterApplied events.
    /// Key: Filter name (e.g., "category", "brand", "price_range")
    /// Value: Filter value (e.g., "electronics", "Apple", "100-500")
    /// </summary>
    public Dictionary<string, string>? Filters { get; set; }

    /// <summary>
    /// Product name for display/reporting purposes.
    /// Denormalized for convenience - not for joins.
    /// </summary>
    public string? ProductName { get; set; }

    /// <summary>
    /// Product SKU for inventory correlation.
    /// </summary>
    public string? ProductSku { get; set; }

    /// <summary>
    /// Extensible key-value pairs for additional data.
    /// 
    /// RULES:
    /// - NO provider-specific fields
    /// - Use for position, result_count, error_message, etc.
    /// - Keys should be snake_case
    /// - Values should be serializable primitives
    /// </summary>
    public Dictionary<string, object>? Extra { get; set; }
}
