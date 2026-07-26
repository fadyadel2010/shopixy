namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// Mylerz carrier definition.
/// 
/// Mylerz is a leading Egyptian last-mile delivery and logistics company
/// specializing in e-commerce fulfillment and Cash on Delivery services.
/// 
/// KEY CAPABILITIES:
/// - Domestic Express Delivery
/// - Cash on Delivery (COD)
/// - Real-time Tracking
/// - Webhook Notifications
/// - Same-day and Next-day Delivery
/// 
/// API Documentation: https://developers.mylerz.com/
/// 
/// CARRIER-AGNOSTIC ARCHITECTURE:
/// - No Mylerz-specific concepts leak to core domain
/// - This definition describes capabilities for the POS shipping module
/// </summary>
public sealed class MylerzCarrierDefinition : ShippingCarrierDefinition
{
    public override string CarrierCode => "mylerz";
    public override string DisplayName => "Mylerz";
    public override string? LogoUrl => "https://mylerz.com/logo.png";
    
    /// <summary>
    /// Mylerz supports Cash on Delivery - core feature for Egyptian e-commerce.
    /// </summary>
    public override bool SupportsCod => true;
    
    /// <summary>
    /// Mylerz supports prepaid shipments.
    /// </summary>
    public override bool SupportsPrepaid => true;
    
    /// <summary>
    /// Mylerz provides comprehensive real-time tracking.
    /// </summary>
    public override bool SupportsTracking => true;
    
    /// <summary>
    /// Mylerz supports webhook notifications for shipment status updates.
    /// </summary>
    public override bool SupportsWebhooks => true;
    
    /// <summary>
    /// Mylerz uses scheduled daily pickups, no API-based pickup scheduling.
    /// </summary>
    public override bool SupportsPickupScheduling => false;
    
    /// <summary>
    /// Mylerz provides AWB labels in PDF format.
    /// </summary>
    public override bool SupportsLabels => true;
    
    /// <summary>
    /// Mylerz allows shipment cancellation before pickup.
    /// </summary>
    public override bool SupportsCancellation => true;
    
    /// <summary>
    /// Mylerz provides zone-based rate quotes.
    /// </summary>
    public override bool SupportsQuotes => true;
    
    /// <summary>
    /// Mylerz primarily operates in Egypt.
    /// </summary>
    public override IReadOnlyList<string> SupportedCountries => new[] { "EG" };
    
    /// <summary>
    /// Primary currency is Egyptian Pound.
    /// </summary>
    public override string DefaultCurrency => "EGP";
}
