using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Shipping method configuration for e-commerce orders
/// </summary>
public class ShippingMethod : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this shipping method.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Shipping method name (English)
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Shipping method name (Arabic)
    /// </summary>
    public string? NameAr { get; set; }
    
    /// <summary>
    /// Method description
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Base shipping rate
    /// </summary>
    public decimal BaseRate { get; set; }
    
    /// <summary>
    /// Additional rate per kg
    /// </summary>
    public decimal PerKgRate { get; set; }
    
    /// <summary>
    /// Order total threshold for free shipping (null = no free shipping)
    /// </summary>
    public decimal? FreeShippingThreshold { get; set; }
    
    /// <summary>
    /// Minimum estimated delivery days
    /// </summary>
    public int EstimatedDeliveryDaysMin { get; set; } = 1;
    
    /// <summary>
    /// Maximum estimated delivery days
    /// </summary>
    public int EstimatedDeliveryDaysMax { get; set; } = 5;
    
    /// <summary>
    /// Whether this shipping method is currently available
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Display order in shipping method list
    /// </summary>
    public int SortOrder { get; set; }
    
    /// <summary>
    /// Countries where this shipping method is available (null = all countries)
    /// Stored as JSON array of country codes
    /// </summary>
    public string? AvailableCountries { get; set; }
    
    /// <summary>
    /// Carrier/provider name (e.g., DHL, FedEx, Aramex)
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// Carrier code for carrier-agnostic shipping module (e.g., "bosta", "aramex", "dhl").
    /// Links this shipping method to a carrier adapter for auto-shipment creation.
    /// Null means "manual" (no carrier integration).
    /// </summary>
    public string? CarrierCode { get; set; }
    
    /// <summary>
    /// Service level for delivery speed (standard, express, same_day, next_day).
    /// Defaults to "standard" if not specified.
    /// Used when creating shipments to determine carrier service level.
    /// </summary>
    public string ServiceLevel { get; set; } = "standard";
    
    /// <summary>
    /// Tracking URL template (use {tracking_number} as placeholder)
    /// </summary>
    public string? TrackingUrlTemplate { get; set; }
}
