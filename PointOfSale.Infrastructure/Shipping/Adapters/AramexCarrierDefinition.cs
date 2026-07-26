namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// Aramex carrier definition.
/// 
/// Aramex is a leading logistics and transportation solutions provider
/// in the Middle East and Africa, with a global network spanning over 65 countries.
/// 
/// KEY CAPABILITIES:
/// - Domestic and International Express
/// - Cash on Delivery (COD)
/// - Scheduled Pickups
/// - Real-time Tracking
/// - Webhook Notifications
/// 
/// API Documentation: https://www.aramex.com/developers
/// 
/// CARRIER-AGNOSTIC ARCHITECTURE:
/// - No Aramex-specific concepts leak to core domain
/// - This definition describes capabilities for the POS shipping module
/// </summary>
public sealed class AramexCarrierDefinition : ShippingCarrierDefinition
{
    public override string CarrierCode => "aramex";
    public override string DisplayName => "Aramex";
    public override string? LogoUrl => "https://www.aramex.com/logo.png";
    
    /// <summary>
    /// Aramex supports Cash on Delivery across MENA region.
    /// </summary>
    public override bool SupportsCod => true;
    
    /// <summary>
    /// Aramex supports prepaid shipments.
    /// </summary>
    public override bool SupportsPrepaid => true;
    
    /// <summary>
    /// Aramex provides comprehensive real-time tracking.
    /// </summary>
    public override bool SupportsTracking => true;
    
    /// <summary>
    /// The public Aramex API contract exposes pull-based tracking and does not
    /// publish a general shipment-status webhook contract.
    /// </summary>
    public override bool SupportsWebhooks => false;
    
    /// <summary>
    /// Aramex supports scheduled pickup requests via API.
    /// </summary>
    public override bool SupportsPickupScheduling => true;
    
    /// <summary>
    /// Aramex provides AWB labels in PDF format.
    /// </summary>
    public override bool SupportsLabels => true;
    
    /// <summary>
    /// The public Shipping Services contract supports pickup cancellation, not
    /// shipment/AWB cancellation.
    /// </summary>
    public override bool SupportsCancellation => false;
    
    /// <summary>
    /// Aramex provides real-time rate quotes via API.
    /// </summary>
    public override bool SupportsQuotes => true;
    
    /// <summary>
    /// Aramex operates globally with strong MENA presence.
    /// Primary markets: Egypt, UAE, Saudi Arabia, Jordan.
    /// </summary>
    public override IReadOnlyList<string> SupportedCountries => new[] 
    { 
        "EG", "AE", "SA", "JO", "KW", "BH", "QA", "OM", 
        "LB", "MA", "TN", "DZ", "PK", "IN", "GB", "US" 
    };
    
    /// <summary>
    /// Primary currency is typically local to the operating country.
    /// Default to Egyptian Pound for Egypt operations.
    /// </summary>
    public override string DefaultCurrency => "EGP";
}
