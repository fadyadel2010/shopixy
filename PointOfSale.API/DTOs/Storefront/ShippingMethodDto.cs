namespace PointOfSale.API.DTOs.Storefront;

/// <summary>
/// Shipping method DTO for storefront.
/// 
/// Phase 5.1: Shipping Methods Read API
/// Phase 10.4.C: Shipping Integration
/// 
/// This DTO exposes shipping method data to the e-commerce storefront.
/// Shipping methods are managed ONLY in POS - this is READ-ONLY.
/// 
/// IMPORTANT: Field names must match frontend TypeScript interface:
/// - price (not baseRate)
/// - currency
/// - estimatedDays (not deliveryDays)
/// </summary>
public class ShippingMethodDto
{
    /// <summary>
    /// Backend tenant scope that produced this shipping-method evidence.
    /// Storefront treats it as validation evidence only; backend SQL remains authoritative.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Shipping method ID.
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// Shipping method name (English).
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Shipping method name (Arabic).
    /// </summary>
    public string? NameAr { get; set; }
    
    /// <summary>
    /// Method description.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Base shipping rate.
    /// </summary>
    public decimal BaseRate { get; set; }
    
    /// <summary>
    /// Calculated price for display (same as BaseRate, but named to match frontend).
    /// </summary>
    public decimal Price { get; set; }
    
    /// <summary>
    /// Currency code (e.g., USD, SAR).
    /// </summary>
    public string Currency { get; set; } = string.Empty;
    
    /// <summary>
    /// Additional rate per kg.
    /// </summary>
    public decimal RatePerKg { get; set; }
    
    /// <summary>
    /// Order total threshold for free shipping (null = no free shipping).
    /// </summary>
    public decimal? FreeShippingThreshold { get; set; }
    
    /// <summary>
    /// Estimated delivery days (formatted string, e.g., "3-5 days").
    /// </summary>
    public string DeliveryDays { get; set; } = string.Empty;
    
    /// <summary>
    /// Estimated delivery days (numeric for frontend).
    /// </summary>
    public int? EstimatedDays { get; set; }
    
    /// <summary>
    /// Carrier/provider name (e.g., DHL, FedEx, Aramex).
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// Display order in shipping method list.
    /// </summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// Shipping methods list response.
/// </summary>
public class ShippingMethodsListResponseDto
{
    /// <summary>
    /// Canonical checkout availability state for the returned methods.
    /// </summary>
    public string Status { get; set; } = "AVAILABLE";

    /// <summary>
    /// List of shipping methods.
    /// </summary>
    public List<ShippingMethodDto> ShippingMethods { get; set; } = new();
    
    /// <summary>
    /// Total count.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Total configured methods, including disabled methods hidden from Storefront.
    /// </summary>
    public int ConfiguredCount { get; set; }

    /// <summary>
    /// Active methods before cart/address/warehouse applicability filters.
    /// </summary>
    public int ActiveCount { get; set; }

    /// <summary>
    /// Active methods applicable to the current cart/address context.
    /// </summary>
    public int ApplicableCount { get; set; }
}
