namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// Base class for carrier definitions.
/// 
/// CARRIER-AGNOSTIC SHIPPING MODULE
/// 
/// ============================================================================
/// P2 DECISION LOCK (2026-01-22):
/// These CarrierDefinition classes are designated as CARRIER CAPABILITY METADATA.
/// 
/// PURPOSE:
/// - Admin UI: Display carrier capabilities in carrier config screens
/// - Frontend: Show/hide features based on carrier support (e.g., COD option)
/// - Validation: Check carrier supports requested features before API calls
/// 
/// NOT USED FOR:
/// - Runtime adapter selection (registry handles that)
/// - Business logic (adapters handle that)
/// 
/// FUTURE INTEGRATION:
/// - Admin controller can expose definitions for carrier selection UI
/// - Frontend can use to conditionally show COD, scheduling, etc.
/// 
/// DO NOT REMOVE - Required for Shipping Frontend Phase
/// ============================================================================
/// 
/// Each carrier has a definition that describes its capabilities.
/// This allows the system to:
/// - Show/hide features based on carrier support
/// - Validate requests before sending to carrier
/// - Display carrier-specific UI options
/// </summary>
public abstract class ShippingCarrierDefinition
{
    /// <summary>
    /// Unique carrier code (lowercase, no spaces).
    /// </summary>
    public abstract string CarrierCode { get; }
    
    /// <summary>
    /// Display name for UI.
    /// </summary>
    public abstract string DisplayName { get; }
    
    /// <summary>
    /// Carrier logo URL (if available).
    /// </summary>
    public virtual string? LogoUrl => null;
    
    /// <summary>
    /// Whether carrier supports Cash on Delivery.
    /// </summary>
    public abstract bool SupportsCod { get; }
    
    /// <summary>
    /// Whether carrier supports prepaid shipments.
    /// </summary>
    public abstract bool SupportsPrepaid { get; }
    
    /// <summary>
    /// Whether carrier provides tracking updates.
    /// </summary>
    public abstract bool SupportsTracking { get; }
    
    /// <summary>
    /// Whether carrier can send webhook notifications.
    /// </summary>
    public abstract bool SupportsWebhooks { get; }
    
    /// <summary>
    /// Whether carrier supports scheduled pickup requests.
    /// </summary>
    public abstract bool SupportsPickupScheduling { get; }
    
    /// <summary>
    /// Whether carrier provides shipping labels.
    /// </summary>
    public virtual bool SupportsLabels => true;
    
    /// <summary>
    /// Whether carrier allows shipment cancellation.
    /// </summary>
    public virtual bool SupportsCancellation => true;
    
    /// <summary>
    /// Whether carrier provides real-time rate quotes.
    /// </summary>
    public virtual bool SupportsQuotes => true;
    
    /// <summary>
    /// Primary countries/regions served.
    /// </summary>
    public virtual IReadOnlyList<string> SupportedCountries => new[] { "EG" };
    
    /// <summary>
    /// Default currency for this carrier.
    /// </summary>
    public virtual string DefaultCurrency => "EGP";
}

/// <summary>
/// Bosta carrier definition.
/// 
/// Bosta is a popular last-mile delivery carrier in Egypt, specializing in:
/// - E-commerce deliveries
/// - Cash on delivery (COD)
/// - Same-day and next-day delivery
/// 
/// API Documentation: https://developers.bosta.co/
/// </summary>
public sealed class BostaCarrierDefinition : ShippingCarrierDefinition
{
    public override string CarrierCode => "bosta";
    public override string DisplayName => "Bosta";
    public override string? LogoUrl => "https://bosta.co/logo.png";
    
    /// <summary>
    /// Bosta supports COD - one of their core features for Egyptian market.
    /// </summary>
    public override bool SupportsCod => true;
    
    /// <summary>
    /// Bosta supports prepaid shipments.
    /// </summary>
    public override bool SupportsPrepaid => true;
    
    /// <summary>
    /// Bosta provides comprehensive tracking.
    /// </summary>
    public override bool SupportsTracking => true;
    
    /// <summary>
    /// Bosta supports webhooks for status updates.
    /// </summary>
    public override bool SupportsWebhooks => true;
    
    /// <summary>
    /// Pickup scheduling is not implemented by this adapter. A default pickup
    /// location does not establish recurring/automatic collection entitlement.
    /// </summary>
    public override bool SupportsPickupScheduling => false;
    
    /// <summary>
    /// Bosta provides AWB labels.
    /// </summary>
    public override bool SupportsLabels => false;
    
    /// <summary>
    /// Bosta allows cancellation before pickup.
    /// </summary>
    public override bool SupportsCancellation => true;
    
    /// <summary>
    /// Bosta uses zone-based pricing, quotes available.
    /// </summary>
    public override bool SupportsQuotes => false;
    
    /// <summary>
    /// Bosta primarily operates in Egypt.
    /// </summary>
    public override IReadOnlyList<string> SupportedCountries => new[] { "EG" };
    
    /// <summary>
    /// Primary currency is Egyptian Pound.
    /// </summary>
    public override string DefaultCurrency => "EGP";
}
