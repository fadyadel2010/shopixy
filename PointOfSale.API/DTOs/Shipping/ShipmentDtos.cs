using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.Shipping;

/// <summary>
/// Request DTO for creating a shipment.
/// Used internally by CheckoutService.
/// </summary>
public class CreateShipmentRequestDto
{
    /// <summary>
    /// Order ID.
    /// </summary>
    public Guid OrderId { get; set; }
    
    /// <summary>
    /// Shipping method ID.
    /// </summary>
    public Guid ShippingMethodId { get; set; }
}

/// <summary>
/// Response DTO for shipment.
/// </summary>
public class ShipmentDto
{
    /// <summary>
    /// Shipment ID.
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// Order ID.
    /// </summary>
    public Guid OrderId { get; set; }
    
    /// <summary>
    /// Shipment number.
    /// </summary>
    public string ShipmentNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Shipping method name.
    /// </summary>
    public string ShippingMethodName { get; set; } = string.Empty;
    
    /// <summary>
    /// Carrier name.
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// Shipping cost.
    /// </summary>
    public decimal ShippingCost { get; set; }
    
    /// <summary>
    /// Currency.
    /// </summary>
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Current status.
    /// </summary>
    public string Status { get; set; } = string.Empty;
    
    /// <summary>
    /// Tracking number.
    /// </summary>
    public string? TrackingNumber { get; set; }
    
    /// <summary>
    /// Tracking URL.
    /// </summary>
    public string? TrackingUrl { get; set; }
    
    /// <summary>
    /// Recipient name.
    /// </summary>
    public string RecipientName { get; set; } = string.Empty;
    
    /// <summary>
    /// Shipping address.
    /// </summary>
    public ShipmentAddressDto Address { get; set; } = new();
    
    /// <summary>
    /// Estimated delivery date.
    /// </summary>
    public DateTime? EstimatedDeliveryDate { get; set; }
    
    /// <summary>
    /// Actual delivery date.
    /// </summary>
    public DateTime? ActualDeliveryDate { get; set; }
    
    /// <summary>
    /// When shipment was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// When packed.
    /// </summary>
    public DateTime? PackedAt { get; set; }
    
    /// <summary>
    /// When shipped.
    /// </summary>
    public DateTime? ShippedAt { get; set; }
    
    /// <summary>
    /// When delivered.
    /// </summary>
    public DateTime? DeliveredAt { get; set; }
}

/// <summary>
/// Address DTO for shipment.
/// </summary>
public class ShipmentAddressDto
{
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
}

/// <summary>
/// Request DTO for updating shipment status.
/// </summary>
public class UpdateShipmentStatusRequestDto
{
    /// <summary>
    /// New status.
    /// </summary>
    public ShipmentStatus NewStatus { get; set; }
    
    /// <summary>
    /// Tracking number (required for Shipped status).
    /// </summary>
    public string? TrackingNumber { get; set; }
    
    /// <summary>
    /// Cancellation reason (required for Cancelled status).
    /// </summary>
    public string? CancellationReason { get; set; }
    
    /// <summary>
    /// Notes.
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Response DTO for status update.
/// </summary>
public class ShipmentStatusUpdateResponseDto
{
    public Guid ShipmentId { get; set; }
    public Guid OrderId { get; set; }
    public string PreviousStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Shipment for storefront (limited info).
/// </summary>
public class StorefrontShipmentDto
{
    public Guid ShipmentId { get; set; }
    public Guid OrderId { get; set; }
    public string ShipmentNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ShippingMethodName { get; set; } = string.Empty;
    public string? CarrierName { get; set; }
    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }
    public DateTime? EstimatedDeliveryDate { get; set; }
    public DateTime? ActualDeliveryDate { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}
