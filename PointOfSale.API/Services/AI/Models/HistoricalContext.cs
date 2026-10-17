using System.Text.Json.Serialization;

namespace PointOfSale.API.Services.AI.Models;

// =============================================================================
// PHASE 5: Historical Consistency & Confidence Layer
// 
// These models enable CommerceAI to:
// - Understand legacy orders gracefully
// - Explain incomplete or missing events
// - Communicate uncertainty safely
// - Never expose "Unknown" or "Bug-like" language
// 
// CORE PRINCIPLES (NON-NEGOTIABLE):
// - The system never lies
// - The system never guesses
// - Missing data is explained, not hidden
// - Legacy ≠ Error
// - Confidence > Completeness
// =============================================================================

/// <summary>
/// Data confidence level for historical context.
/// INTERNAL ONLY - never exposed to UI.
/// </summary>
public enum DataConfidenceLevel
{
    /// <summary>
    /// Full event trail available.
    /// All expected events are present.
    /// </summary>
    High = 3,
    
    /// <summary>
    /// Partial events available.
    /// Some events may be missing but core data is present.
    /// </summary>
    Medium = 2,
    
    /// <summary>
    /// Legacy or inferred data.
    /// Order was created before integrations were enabled.
    /// </summary>
    Low = 1
}

/// <summary>
/// Historical context for understanding legacy orders and data completeness.
/// PHASE 5: Historical Consistency & Confidence Layer.
/// </summary>
public class HistoricalContext
{
    /// <summary>
    /// Whether this order was created before modern integrations.
    /// </summary>
    [JsonPropertyName("isLegacyOrder")]
    public bool IsLegacyOrder { get; set; }

    /// <summary>
    /// Whether shipping integration was enabled when order was created.
    /// </summary>
    [JsonPropertyName("shippingIntegrationEnabled")]
    public bool? ShippingIntegrationEnabledAtCreation { get; set; }

    /// <summary>
    /// Whether payment integration was enabled when order was created.
    /// </summary>
    [JsonPropertyName("paymentIntegrationEnabled")]
    public bool? PaymentIntegrationEnabledAtCreation { get; set; }

    /// <summary>
    /// Whether event tracking was enabled when order was created.
    /// </summary>
    [JsonPropertyName("eventTrackingEnabled")]
    public bool? EventTrackingEnabledAtCreation { get; set; }

    /// <summary>
    /// Whether the event trail is incomplete.
    /// </summary>
    [JsonPropertyName("eventsIncomplete")]
    public bool EventsIncomplete { get; set; }

    /// <summary>
    /// Number of expected events based on order type.
    /// </summary>
    [JsonIgnore]
    public int ExpectedEventCount { get; set; }

    /// <summary>
    /// Number of actual events recorded.
    /// </summary>
    [JsonIgnore]
    public int ActualEventCount { get; set; }

    /// <summary>
    /// Data confidence level (INTERNAL ONLY - not serialized to AI prompt).
    /// </summary>
    [JsonIgnore]
    public DataConfidenceLevel Confidence { get; set; } = DataConfidenceLevel.High;

    /// <summary>
    /// Human-readable confidence explanation for the AI to use.
    /// This is the ONLY way confidence is communicated to the AI.
    /// </summary>
    [JsonPropertyName("confidenceNote")]
    public string? ConfidenceNote { get; set; }

    /// <summary>
    /// Date when the order was created (for time-based context).
    /// </summary>
    [JsonPropertyName("orderCreatedAt")]
    public DateTime OrderCreatedAt { get; set; }

    /// <summary>
    /// Date when shipping integration was enabled for this tenant.
    /// </summary>
    [JsonIgnore]
    public DateTime? ShippingIntegrationEnabledDate { get; set; }

    /// <summary>
    /// Date when payment integration was enabled for this tenant.
    /// </summary>
    [JsonIgnore]
    public DateTime? PaymentIntegrationEnabledDate { get; set; }
}

/// <summary>
/// Feature timeline for determining historical context.
/// Stores when various features were enabled for a tenant.
/// </summary>
public class FeatureTimeline
{
    /// <summary>
    /// When automated shipping tracking was enabled.
    /// </summary>
    public DateTime? ShippingTrackingEnabledAt { get; set; }

    /// <summary>
    /// When automated payment tracking was enabled.
    /// </summary>
    public DateTime? PaymentTrackingEnabledAt { get; set; }

    /// <summary>
    /// When event/audit logging was enabled.
    /// </summary>
    public DateTime? EventLoggingEnabledAt { get; set; }

    /// <summary>
    /// When carrier webhook integration was enabled.
    /// </summary>
    public DateTime? CarrierWebhooksEnabledAt { get; set; }

    /// <summary>
    /// Default feature timeline (all features enabled from the beginning).
    /// </summary>
    public static FeatureTimeline Default => new()
    {
        ShippingTrackingEnabledAt = DateTime.MinValue,
        PaymentTrackingEnabledAt = DateTime.MinValue,
        EventLoggingEnabledAt = DateTime.MinValue,
        CarrierWebhooksEnabledAt = DateTime.MinValue
    };
}

/// <summary>
/// Confidence resolution result.
/// </summary>
public class ConfidenceResult
{
    /// <summary>
    /// The determined confidence level.
    /// </summary>
    public DataConfidenceLevel Confidence { get; set; }

    /// <summary>
    /// Human-readable note for the AI.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Reasons contributing to the confidence level.
    /// </summary>
    public List<string> Reasons { get; set; } = new();

    /// <summary>
    /// Create a high confidence result.
    /// </summary>
    public static ConfidenceResult High() => new()
    {
        Confidence = DataConfidenceLevel.High,
        Note = null,
        Reasons = new List<string> { "Full event trail available" }
    };

    /// <summary>
    /// Create a medium confidence result with a reason.
    /// </summary>
    public static ConfidenceResult Medium(string reason, string note) => new()
    {
        Confidence = DataConfidenceLevel.Medium,
        Note = note,
        Reasons = new List<string> { reason }
    };

    /// <summary>
    /// Create a low confidence result with reasons.
    /// </summary>
    public static ConfidenceResult Low(List<string> reasons, string note) => new()
    {
        Confidence = DataConfidenceLevel.Low,
        Note = note,
        Reasons = reasons
    };
}
