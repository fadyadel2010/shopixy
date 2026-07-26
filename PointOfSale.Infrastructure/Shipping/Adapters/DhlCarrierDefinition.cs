namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// DHL carrier definition.
/// 
/// DHL is a global logistics company providing international shipping,
/// express delivery, and freight transportation services.
/// 
/// KEY CAPABILITIES:
/// - International Express Shipping
/// - Domestic Express
/// - Real-time Tracking
/// - Scheduled Pickups
/// - Webhook Notifications
/// 
/// API Documentation: https://developer.dhl.com/
/// 
/// CARRIER-AGNOSTIC ARCHITECTURE:
/// - No DHL-specific concepts leak to core domain
/// - This definition describes capabilities for the POS shipping module
/// 
/// NOTE: DHL typically operates as prepaid only (no COD in most markets)
/// </summary>
public sealed class DhlCarrierDefinition : ShippingCarrierDefinition
{
    public override string CarrierCode => "dhl";
    public override string DisplayName => "DHL Express";
    public override string? LogoUrl => "https://www.dhl.com/logo.png";
    
    /// <summary>
    /// DHL typically does not support Cash on Delivery in most markets.
    /// COD is available in limited regions only.
    /// </summary>
    public override bool SupportsCod => false;
    
    /// <summary>
    /// DHL supports prepaid shipments (primary payment method).
    /// </summary>
    public override bool SupportsPrepaid => true;
    
    /// <summary>
    /// DHL provides comprehensive global tracking.
    /// </summary>
    public override bool SupportsTracking => true;
    
    /// <summary>
    /// No MyDHL REST webhook contract is implemented by this adapter.
    /// </summary>
    public override bool SupportsWebhooks => false;
    
    /// <summary>
    /// MyDHL pickup APIs exist, but this adapter does not implement them.
    /// </summary>
    public override bool SupportsPickupScheduling => false;
    
    /// <summary>
    /// DHL provides AWB labels in multiple formats (PDF, ZPL, EPL).
    /// </summary>
    public override bool SupportsLabels => true;
    
    /// <summary>
    /// MyDHL REST v3.3.1 does not publish shipment cancellation.
    /// </summary>
    public override bool SupportsCancellation => false;
    
    /// <summary>
    /// DHL provides real-time rate quotes via API.
    /// </summary>
    public override bool SupportsQuotes => true;
    
    /// <summary>
    /// DHL operates globally with presence in 220+ countries.
    /// Primary markets listed for this integration.
    /// </summary>
    public override IReadOnlyList<string> SupportedCountries => new[] 
    { 
        "EG", "SA", "AE", "JO", "KW", "BH", "QA", "OM",  // MENA
        "US", "GB", "DE", "FR", "NL", "BE", "ES", "IT",  // Americas & Europe
        "CN", "JP", "KR", "SG", "HK", "AU", "IN"         // APAC
    };
    
    /// <summary>
    /// Primary currency depends on account country.
    /// Default to USD for international shipments.
    /// </summary>
    public override string DefaultCurrency => "USD";
}
