using PointOfSale.API.DTOs.Admin;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Admin;

/// <summary>
/// Admin Shipment Service Interface.
/// 
/// PHASE 6.2: ADMIN SHIPMENT OPERATIONS
/// 
/// ARCHITECTURAL RULES (STRICT):
/// - PostgreSQL is the ONLY source of truth for shipments
/// - MongoDB/Elasticsearch are NOT used for admin operations
/// - All operations are tenant-isolated via Shipment.TenantId
/// - Events are reused from existing shipment events
/// - Prefer rejecting invalid actions over auto-fixing
/// </summary>
public interface IAdminShipmentService
{
    /// <summary>
    /// Lists shipments with pagination and filtering.
    /// 
    /// Filters:
    /// - Status (optional)
    /// - Carrier (optional)
    /// - FromDate/ToDate (optional)
    /// - OrderId (optional)
    /// </summary>
    Task<AdminPagedResult<AdminShipmentListItemDto>> ListShipmentsAsync(
        Guid tenantId,
        AdminShipmentListQuery query,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets full shipment details including order reference and timeline.
    /// </summary>
    Task<AdminShipmentDetailsDto?> GetShipmentDetailsAsync(
        Guid tenantId,
        Guid shipmentId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates shipment status (admin override).
    /// 
    /// Rules:
    /// - Pending → Packed, Cancelled
    /// - Packed → Shipped
    /// - Shipped → Delivered
    /// - Delivered → ❌
    /// - Cancelled → ❌
    /// </summary>
    Task<AdminShipmentStatusUpdateResponse> UpdateShipmentStatusAsync(
        Guid tenantId,
        Guid shipmentId,
        AdminUpdateShipmentStatusRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates tracking information only.
    /// </summary>
    Task<AdminTrackingUpdateResponse> UpdateTrackingAsync(
        Guid tenantId,
        Guid shipmentId,
        AdminUpdateTrackingRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a shipment (admin action).
    /// 
    /// Rules:
    /// - Only allowed if shipment is NOT delivered
    /// - Must emit ShipmentCancelledEvent
    /// </summary>
    Task<AdminShipmentCancellationResponse> CancelShipmentAsync(
        Guid tenantId,
        Guid shipmentId,
        AdminCancelShipmentRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);
}

#region Admin Shipment Exceptions

/// <summary>
/// Base exception for admin shipment operations.
/// </summary>
public class AdminShipmentException : Exception
{
    public string ErrorCode { get; }

    public AdminShipmentException(string errorCode, string message) 
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public AdminShipmentException(string errorCode, string message, Exception innerException) 
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Thrown when shipment is not found.
/// HTTP 404
/// </summary>
public class AdminShipmentNotFoundException : AdminShipmentException
{
    public Guid ShipmentId { get; }

    public AdminShipmentNotFoundException(Guid shipmentId) 
        : base("SHIPMENT_NOT_FOUND", $"Shipment {shipmentId} not found")
    {
        ShipmentId = shipmentId;
    }
}

/// <summary>
/// Thrown when status transition is invalid.
/// HTTP 409
/// </summary>
public class InvalidAdminShipmentStateException : AdminShipmentException
{
    public Guid ShipmentId { get; }
    public ShipmentStatus CurrentStatus { get; }
    public ShipmentStatus RequestedStatus { get; }

    public InvalidAdminShipmentStateException(Guid shipmentId, ShipmentStatus currentStatus, ShipmentStatus requestedStatus) 
        : base("INVALID_SHIPMENT_STATE", 
            $"Cannot transition shipment from {currentStatus} to {requestedStatus}")
    {
        ShipmentId = shipmentId;
        CurrentStatus = currentStatus;
        RequestedStatus = requestedStatus;
    }
}

/// <summary>
/// Thrown when trying to modify a delivered or cancelled shipment.
/// HTTP 409
/// </summary>
public class AdminShipmentImmutableException : AdminShipmentException
{
    public Guid ShipmentId { get; }
    public ShipmentStatus CurrentStatus { get; }

    public AdminShipmentImmutableException(Guid shipmentId, ShipmentStatus currentStatus) 
        : base("SHIPMENT_IMMUTABLE", 
            $"Shipment {shipmentId} is in {currentStatus} status and cannot be modified")
    {
        ShipmentId = shipmentId;
        CurrentStatus = currentStatus;
    }
}

/// <summary>
/// Thrown when cancellation reason is missing.
/// HTTP 400
/// </summary>
public class ShipmentCancellationReasonRequiredException : AdminShipmentException
{
    public ShipmentCancellationReasonRequiredException() 
        : base("CANCELLATION_REASON_REQUIRED", "Cancellation reason is required")
    {
    }
}

/// <summary>
/// Thrown when tracking number is required but missing.
/// HTTP 400
/// </summary>
public class TrackingNumberRequiredException : AdminShipmentException
{
    public TrackingNumberRequiredException() 
        : base("TRACKING_NUMBER_REQUIRED", "Tracking number is required for shipped status")
    {
    }
}

/// <summary>
/// Thrown when shipment progression would bypass payment/accounting authority.
/// HTTP 409
/// </summary>
public class ShipmentFinancialAuthorityException : AdminShipmentException
{
    public ShipmentFinancialAuthorityException(ShipmentStatus requestedStatus, string reason)
        : base("SHIPMENT_FINANCIAL_AUTHORITY_REQUIRED", $"Cannot advance shipment to {requestedStatus}. {reason}")
    {
        RequestedStatus = requestedStatus;
        Reason = reason;
    }

    public ShipmentStatus RequestedStatus { get; }
    public string Reason { get; }
}

/// <summary>
/// Thrown when an admin DTO attempts to overwrite carrier-managed tracking authority.
/// HTTP 409
/// </summary>
public class CarrierManagedTrackingAuthorityException : AdminShipmentException
{
    public CarrierManagedTrackingAuthorityException(Guid shipmentId, string carrierCode)
        : base(
            "CARRIER_TRACKING_AUTHORITY_REQUIRED",
            $"Shipment {shipmentId} is managed by carrier '{carrierCode}'. Tracking and carrier identity must come from carrier registration, tracking sync, or verified webhooks.")
    {
        ShipmentId = shipmentId;
        CarrierCode = carrierCode;
    }

    public Guid ShipmentId { get; }
    public string CarrierCode { get; }
}

#endregion
