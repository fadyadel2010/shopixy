using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.Admin;

/// <summary>
/// Admin shipment list item DTO.
/// Used for paginated shipment lists in admin panel.
/// Phase 6.2: Admin Shipment Operations
/// </summary>
public class AdminShipmentListItemDto
{
    /// <summary>
    /// Shipment ID (lowercase for frontend compatibility).
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// Shipment ID (for backward compatibility).
    /// </summary>
    public Guid ShipmentId { get; set; }
    
    /// <summary>
    /// Order ID.
    /// </summary>
    public Guid OrderId { get; set; }
    
    /// <summary>
    /// Unique shipment number.
    /// </summary>
    public string ShipmentNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Current shipment status.
    /// </summary>
    public ShipmentStatus Status { get; set; }
    
    /// <summary>
    /// Carrier code (bosta, aramex, mylerz, dhl, manual).
    /// Used by frontend for CarrierBadge component.
    /// </summary>
    public string? CarrierCode { get; set; }
    
    /// <summary>
    /// Carrier name (display name).
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// Tracking number.
    /// </summary>
    public string? TrackingNumber { get; set; }
    
    /// <summary>
    /// Is this a COD (Cash on Delivery) shipment?
    /// </summary>
    public bool IsCashOnDelivery { get; set; }
    
    /// <summary>
    /// COD amount if COD.
    /// </summary>
    public decimal? CodAmount { get; set; }
    
    /// <summary>
    /// Has COD been collected?
    /// </summary>
    public bool CodCollected { get; set; }
    
    /// <summary>
    /// Currency code.
    /// </summary>
    public string? Currency { get; set; }
    
    /// <summary>
    /// Shipment creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
    
    /// <summary>
    /// For backward compatibility
    /// </summary>
    public DateTime CreatedAtUtc => CreatedAt;
}

/// <summary>
/// Admin shipment details DTO.
/// Used for viewing full shipment details.
/// Phase 6.2: Admin Shipment Operations
/// </summary>
public class AdminShipmentDetailsDto
{
    /// <summary>
    /// Shipment ID.
    /// </summary>
    public Guid ShipmentId { get; set; }
    
    /// <summary>
    /// Order ID.
    /// </summary>
    public Guid OrderId { get; set; }
    
    /// <summary>
    /// Order number.
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Unique shipment number.
    /// </summary>
    public string ShipmentNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Current shipment status.
    /// </summary>
    public ShipmentStatus Status { get; set; }
    
    /// <summary>
    /// Carrier name.
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// Tracking number.
    /// </summary>
    public string? TrackingNumber { get; set; }
    
    /// <summary>
    /// Tracking URL.
    /// </summary>
    public string? TrackingUrl { get; set; }
    
    /// <summary>
    /// Shipping method details.
    /// </summary>
    public AdminShippingMethodDto ShippingMethod { get; set; } = new();
    
    /// <summary>
    /// Recipient details.
    /// </summary>
    public AdminShipmentRecipientDto Recipient { get; set; } = new();
    
    /// <summary>
    /// Shipping cost.
    /// </summary>
    public decimal ShippingCost { get; set; }
    
    /// <summary>
    /// Currency.
    /// </summary>
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Estimated delivery date.
    /// </summary>
    public DateTime? EstimatedDeliveryDate { get; set; }
    
    /// <summary>
    /// Actual delivery date.
    /// </summary>
    public DateTime? ActualDeliveryDate { get; set; }
    
    /// <summary>
    /// When created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }
    
    /// <summary>
    /// When packed.
    /// </summary>
    public DateTime? PackedAtUtc { get; set; }
    
    /// <summary>
    /// When shipped.
    /// </summary>
    public DateTime? ShippedAtUtc { get; set; }
    
    /// <summary>
    /// When delivered.
    /// </summary>
    public DateTime? DeliveredAtUtc { get; set; }
    
    /// <summary>
    /// When cancelled.
    /// </summary>
    public DateTime? CancelledAtUtc { get; set; }
    
    /// <summary>
    /// Cancellation reason.
    /// </summary>
    public string? CancellationReason { get; set; }
    
    /// <summary>
    /// Internal notes.
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Shipping method DTO for admin view.
/// </summary>
public class AdminShippingMethodDto
{
    public Guid? ShippingMethodId { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Recipient details for admin view.
/// </summary>
public class AdminShipmentRecipientDto
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Country { get; set; }
}

/// <summary>
/// Request DTO for updating shipment status (admin override).
/// </summary>
public class AdminUpdateShipmentStatusRequest
{
    /// <summary>
    /// New shipment status.
    /// </summary>
    public ShipmentStatus NewStatus { get; set; }
    
    /// <summary>
    /// Reason for status change.
    /// </summary>
    public string? Reason { get; set; }
    
    /// <summary>
    /// Tracking number (optional, for Shipped status).
    /// </summary>
    public string? TrackingNumber { get; set; }
    
    /// <summary>
    /// Carrier name (optional).
    /// </summary>
    public string? CarrierName { get; set; }
}

/// <summary>
/// Response DTO for shipment status update.
/// </summary>
public class AdminShipmentStatusUpdateResponse
{
    public Guid ShipmentId { get; set; }
    public string ShipmentNumber { get; set; } = string.Empty;
    public ShipmentStatus OldStatus { get; set; }
    public ShipmentStatus NewStatus { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Request DTO for updating tracking information.
/// </summary>
public class AdminUpdateTrackingRequest
{
    /// <summary>
    /// Tracking number.
    /// </summary>
    public string TrackingNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Carrier name.
    /// </summary>
    public string? CarrierName { get; set; }
}

/// <summary>
/// Response DTO for tracking update.
/// </summary>
public class AdminTrackingUpdateResponse
{
    public Guid ShipmentId { get; set; }
    public string ShipmentNumber { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string? CarrierName { get; set; }
    public string? TrackingUrl { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Request DTO for cancelling a shipment.
/// </summary>
public class AdminCancelShipmentRequest
{
    /// <summary>
    /// Cancellation reason (required).
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Response DTO for shipment cancellation.
/// </summary>
public class AdminShipmentCancellationResponse
{
    public Guid ShipmentId { get; set; }
    public string ShipmentNumber { get; set; } = string.Empty;
    public ShipmentStatus OldStatus { get; set; }
    public ShipmentStatus NewStatus { get; set; }
    public DateTime CancelledAtUtc { get; set; }
}

/// <summary>
/// Query parameters for admin shipment list.
/// </summary>
public class AdminShipmentListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public ShipmentStatus? Status { get; set; }
    public string? Carrier { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? OrderId { get; set; }

    public const int MinPage = 1;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 50;
    public const int DefaultPageSize = 20;

    /// <summary>
    /// Validates and returns error message if invalid.
    /// </summary>
    public string? Validate()
    {
        if (Page < MinPage)
            return $"Page must be at least {MinPage}";
        
        if (PageSize < MinPageSize || PageSize > MaxPageSize)
            return $"PageSize must be between {MinPageSize} and {MaxPageSize}";

        if (FromDate.HasValue && ToDate.HasValue && FromDate > ToDate)
            return "FromDate cannot be after ToDate";

        return null;
    }
}

/// <summary>
/// Admin Shipment API error response.
/// </summary>
public class AdminShipmentApiErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TraceId { get; set; }

    public AdminShipmentApiErrorResponse() { }

    public AdminShipmentApiErrorResponse(string code, string message, string? traceId = null)
    {
        Code = code;
        Message = message;
        TraceId = traceId;
    }

    // Factory methods for common errors
    public static AdminShipmentApiErrorResponse TenantHeaderMissing(string? traceId = null) =>
        new("TENANT_HEADER_MISSING", "X-Tenant-Id header is required", traceId);

    public static AdminShipmentApiErrorResponse TenantHeaderInvalid(string? traceId = null) =>
        new("TENANT_HEADER_INVALID", "X-Tenant-Id must be a valid GUID", traceId);

    public static AdminShipmentApiErrorResponse ShipmentNotFound(string? traceId = null) =>
        new("SHIPMENT_NOT_FOUND", "Shipment not found", traceId);

    public static AdminShipmentApiErrorResponse InvalidShipmentState(string message, string? traceId = null) =>
        new("INVALID_SHIPMENT_STATE", message, traceId);

    public static AdminShipmentApiErrorResponse ShipmentImmutable(string? traceId = null) =>
        new("SHIPMENT_IMMUTABLE", "Delivered or cancelled shipments cannot be modified", traceId);

    public static AdminShipmentApiErrorResponse InternalError(string? traceId = null) =>
        new("INTERNAL_ERROR", "An internal error occurred", traceId);
}
