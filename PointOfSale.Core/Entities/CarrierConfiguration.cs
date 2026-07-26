using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Carrier configuration for a tenant.
/// 
/// CARRIER-AGNOSTIC SHIPPING MODULE
/// 
/// ARCHITECTURAL RULES:
/// - One configuration per carrier per tenant
/// - API credentials stored encrypted
/// - Configuration is toggleable per carrier
/// - Cached in Redis for performance
/// 
/// Example carriers:
/// - bosta (Egypt)
/// - aramex (MENA region)
/// - mylerz (Egypt)
/// - dhl (International)
/// </summary>
public class CarrierConfiguration : BaseEntity, ITenantOwned
{
    /// <summary>
    /// Tenant isolation.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Carrier code (lowercase, no spaces).
    /// Examples: "bosta", "aramex", "mylerz", "dhl"
    /// </summary>
    public string CarrierCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name for admin UI.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether this carrier is enabled for this tenant.
    /// </summary>
    public bool IsEnabled { get; set; }
    
    /// <summary>
    /// Whether this is the default carrier for new shipments.
    /// Only one carrier can be default per tenant.
    /// </summary>
    public bool IsDefault { get; set; }
    
    #region API Credentials
    
    /// <summary>
    /// API key (encrypted in database).
    /// </summary>
    public string? ApiKey { get; set; }
    
    /// <summary>
    /// API secret (encrypted in database).
    /// </summary>
    public string? ApiSecret { get; set; }
    
    /// <summary>
    /// Account/Client ID (some carriers require this).
    /// </summary>
    public string? AccountId { get; set; }
    
    /// <summary>
    /// API URL (for carriers with configurable endpoints).
    /// Null = use carrier default.
    /// </summary>
    public string? ApiUrl { get; set; }
    
    /// <summary>
    /// Whether to use sandbox/test mode.
    /// </summary>
    public bool UseSandbox { get; set; }
    
    #endregion
    
    #region Carrier Capabilities
    
    /// <summary>
    /// Whether this carrier supports COD.
    /// </summary>
    public bool SupportsCod { get; set; }
    
    /// <summary>
    /// Whether this carrier supports real-time tracking.
    /// </summary>
    public bool SupportsTracking { get; set; }
    
    /// <summary>
    /// Whether this carrier supports webhooks.
    /// If false, tracking sync uses polling.
    /// </summary>
    public bool SupportsWebhooks { get; set; }
    
    /// <summary>
    /// Whether this carrier supports label generation.
    /// </summary>
    public bool SupportsLabels { get; set; }
    
    /// <summary>
    /// Whether this carrier supports rate quotes.
    /// </summary>
    public bool SupportsQuotes { get; set; }
    
    /// <summary>
    /// Whether this carrier supports shipment cancellation.
    /// </summary>
    public bool SupportsCancellation { get; set; }
    
    #endregion
    
    #region Configuration
    
    /// <summary>
    /// Carrier-specific configuration as JSON.
    /// Examples:
    /// - Bosta: { "businessTypeId": "123", "defaultZoneCode": "..." }
    /// - Aramex: { "productGroup": "DOM", "originEntity": "..." }
    /// </summary>
    public string? Configuration { get; set; }
    
    /// <summary>
    /// Webhook URL for this tenant/carrier.
    /// Some carriers require webhook registration.
    /// </summary>
    public string? WebhookUrl { get; set; }
    
    /// <summary>
    /// Webhook secret for signature validation.
    /// </summary>
    public string? WebhookSecret { get; set; }
    
    #endregion
    
    #region Pickup Configuration
    
    /// <summary>
    /// Default pickup address (if different from warehouse).
    /// </summary>
    public string? PickupAddress { get; set; }
    
    /// <summary>
    /// Pickup city.
    /// </summary>
    public string? PickupCity { get; set; }
    
    /// <summary>
    /// Pickup country code.
    /// </summary>
    public string? PickupCountry { get; set; }
    
    /// <summary>
    /// Pickup contact phone.
    /// </summary>
    public string? PickupPhone { get; set; }
    
    /// <summary>
    /// Pickup contact name.
    /// </summary>
    public string? PickupContactName { get; set; }
    
    #endregion
    
    #region Service Levels
    
    /// <summary>
    /// Available service levels as JSON array.
    /// Example: ["standard", "express", "same_day"]
    /// </summary>
    public string? AvailableServiceLevels { get; set; }
    
    /// <summary>
    /// Default service level.
    /// </summary>
    public string DefaultServiceLevel { get; set; } = "standard";
    
    #endregion
    
    #region Pricing Overrides
    
    /// <summary>
    /// Whether to use carrier rates or fixed rates.
    /// If false, use FixedShippingCost.
    /// </summary>
    public bool UseCarrierRates { get; set; } = true;
    
    /// <summary>
    /// Fixed shipping cost (if not using carrier rates).
    /// </summary>
    public decimal? FixedShippingCost { get; set; }
    
    /// <summary>
    /// Markup percentage on carrier rates.
    /// </summary>
    public decimal MarkupPercentage { get; set; }
    
    /// <summary>
    /// Minimum order amount for free shipping.
    /// Null = no free shipping.
    /// </summary>
    public decimal? FreeShippingThreshold { get; set; }
    
    #endregion
    
    #region Metadata
    
    /// <summary>
    /// Display order in carrier selection.
    /// </summary>
    public int SortOrder { get; set; }
    
    /// <summary>
    /// Notes for admin reference.
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Last time credentials were validated.
    /// </summary>
    public DateTime? LastValidatedAtUtc { get; set; }
    
    /// <summary>
    /// Whether last credential validation succeeded.
    /// </summary>
    public bool? LastValidationSucceeded { get; set; }
    
    /// <summary>
    /// Error message from last validation (if failed).
    /// </summary>
    public string? LastValidationError { get; set; }
    
    #endregion
}
