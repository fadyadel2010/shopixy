using PointOfSale.API.DTOs.Shipping;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Shipping;

/// <summary>
/// Service interface for Shipment operations.
/// 
/// PHASE 5.2: SHIPMENT LIFECYCLE
/// 
/// ARCHITECTURAL RULES (STRICT):
/// - ONE shipment per order (for now)
/// - Shipping Method is source of truth from POS
/// - Shipment is created after order is paid/confirmed
/// - Status transitions are strictly enforced
/// - Inventory is already reserved during checkout
/// </summary>
public interface IShipmentService
{
    /// <summary>
    /// Creates a shipment for an order.
    /// Called by authoritative post-confirmation shipment orchestration.
    /// 
    /// Rules:
    /// - Exactly ONE shipment per order
    /// - ShippingMethodId must exist & be active
    /// - COD orders may ship after order confirmation
    /// - Non-COD orders must have canonical paid authority
    /// - Carrier-managed shipments start as PendingExternalRegistration
    /// </summary>
    Task<ShipmentDto> CreateShipmentAsync(
        Guid tenantId,
        Guid orderId,
        Guid shippingMethodId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates shipment status.
    /// Used by admin panel or automation.
    /// 
    /// Lifecycle Rules:
    /// - PendingExternalRegistration → ReadyForPickup (successful carrier registration), Cancelled (order cancelled), Failed (registration fails permanently)
    /// - Pending → ReadyForPickup, Cancelled
    /// - ReadyForPickup → PickedUp, Cancelled
    /// - PickedUp → InTransit
    /// - InTransit → OutForDelivery, Delivered, Failed, Returned
    /// - OutForDelivery → Delivered, Failed, Returned
    /// - Delivered → Returned (within return window)
    /// - Failed → ❌ (terminal)
    /// - Cancelled → ❌ (terminal)
    /// - Returned → ❌ (terminal)
    /// </summary>
    Task<ShipmentStatusUpdateResponseDto> UpdateStatusAsync(
        Guid tenantId,
        Guid shipmentId,
        UpdateShipmentStatusRequestDto request,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replays authoritative shipment-side bridges for an already-persisted shipment.
    /// Used by recovery workflows to repair order/storefront/payment drift when
    /// shipment truth exists in SQL but downstream bridge events or order snapshots
    /// are missing.
    /// </summary>
    Task<bool> ReconcileShipmentAuthorityAsync(
        Guid tenantId,
        Guid shipmentId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets shipment by ID.
    /// </summary>
    Task<ShipmentDto?> GetShipmentByIdAsync(
        Guid tenantId,
        Guid shipmentId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets shipment by order ID.
    /// For storefront use.
    /// </summary>
    Task<StorefrontShipmentDto?> GetShipmentByOrderIdAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId,
        CancellationToken cancellationToken = default);
}

#region Shipment Exceptions

/// <summary>
/// Base exception for shipment failures.
/// </summary>
public class ShipmentException : Exception
{
    public string ErrorCode { get; }

    public ShipmentException(string errorCode, string message) 
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public ShipmentException(string errorCode, string message, Exception innerException) 
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Thrown when order is not found.
/// HTTP 404
/// </summary>
public class ShipmentOrderNotFoundException : ShipmentException
{
    public Guid OrderId { get; }

    public ShipmentOrderNotFoundException(Guid orderId) 
        : base("ORDER_NOT_FOUND", $"Order {orderId} not found")
    {
        OrderId = orderId;
    }
}

/// <summary>
/// Thrown when shipment already exists for order.
/// HTTP 409
/// </summary>
public class ShipmentAlreadyExistsException : ShipmentException
{
    public Guid OrderId { get; }
    public Guid ExistingShipmentId { get; }

    public ShipmentAlreadyExistsException(Guid orderId, Guid existingShipmentId) 
        : base("SHIPMENT_ALREADY_EXISTS", $"Shipment already exists for order {orderId}")
    {
        OrderId = orderId;
        ExistingShipmentId = existingShipmentId;
    }
}

/// <summary>
/// Thrown when order is not ready for shipment.
/// HTTP 409
/// </summary>
public class OrderNotReadyForShipmentException : ShipmentException
{
    public Guid OrderId { get; }
    public string CurrentStatus { get; }

    public OrderNotReadyForShipmentException(Guid orderId, string currentStatus) 
        : base("ORDER_NOT_READY_FOR_SHIPMENT", 
            $"Order {orderId} is not ready for shipment. Status: {currentStatus}")
    {
        OrderId = orderId;
        CurrentStatus = currentStatus;
    }
}

/// <summary>
/// Thrown when shipping method is inactive.
/// HTTP 400
/// </summary>
public class ShippingMethodInactiveException : ShipmentException
{
    public Guid ShippingMethodId { get; }

    public ShippingMethodInactiveException(Guid shippingMethodId) 
        : base("SHIPPING_METHOD_INACTIVE", 
            $"Shipping method {shippingMethodId} is not active")
    {
        ShippingMethodId = shippingMethodId;
    }
}

/// <summary>
/// Thrown when shipment is not found.
/// HTTP 404
/// </summary>
public class ShipmentNotFoundException : ShipmentException
{
    public Guid ShipmentId { get; }

    public ShipmentNotFoundException(Guid shipmentId) 
        : base("SHIPMENT_NOT_FOUND", $"Shipment {shipmentId} not found")
    {
        ShipmentId = shipmentId;
    }
}

/// <summary>
/// Thrown when status transition is invalid.
/// HTTP 409
/// </summary>
public class InvalidShipmentStateException : ShipmentException
{
    public Guid ShipmentId { get; }
    public ShipmentStatus CurrentStatus { get; }
    public ShipmentStatus RequestedStatus { get; }

    public InvalidShipmentStateException(Guid shipmentId, ShipmentStatus currentStatus, ShipmentStatus requestedStatus) 
        : base("INVALID_SHIPMENT_STATE", 
            $"Cannot transition shipment from {currentStatus} to {requestedStatus}")
    {
        ShipmentId = shipmentId;
        CurrentStatus = currentStatus;
        RequestedStatus = requestedStatus;
    }
}

/// <summary>
/// Thrown when a shipment request attempts to replace the shipping method already
/// bound to the authoritative order snapshot.
/// HTTP 409
/// </summary>
public class ShipmentShippingMethodMismatchException : ShipmentException
{
    public Guid OrderId { get; }
    public Guid? OrderShippingMethodId { get; }
    public Guid RequestedShippingMethodId { get; }

    public ShipmentShippingMethodMismatchException(
        Guid orderId,
        Guid? orderShippingMethodId,
        Guid requestedShippingMethodId)
        : base(
            "SHIPMENT_SHIPPING_METHOD_MISMATCH",
            $"Shipping method {requestedShippingMethodId} is not the authoritative selection for order {orderId}")
    {
        OrderId = orderId;
        OrderShippingMethodId = orderShippingMethodId;
        RequestedShippingMethodId = requestedShippingMethodId;
    }
}

/// <summary>
/// Thrown when a client/system status DTO attempts to overwrite carrier-managed tracking authority.
/// HTTP 409
/// </summary>
public class ShipmentCarrierTrackingAuthorityException : ShipmentException
{
    public ShipmentCarrierTrackingAuthorityException(Guid shipmentId, string carrierCode)
        : base(
            "CARRIER_TRACKING_AUTHORITY_REQUIRED",
            $"Shipment {shipmentId} is managed by carrier '{carrierCode}'. Tracking must come from carrier registration, tracking sync, or verified webhooks.")
    {
        ShipmentId = shipmentId;
        CarrierCode = carrierCode;
    }

    public Guid ShipmentId { get; }
    public string CarrierCode { get; }
}

/// <summary>
/// Thrown when an admin/system status DTO attempts to impersonate carrier lifecycle authority.
/// HTTP 409
/// </summary>
public class ShipmentCarrierStatusAuthorityException : ShipmentException
{
    public ShipmentCarrierStatusAuthorityException(Guid shipmentId, string carrierCode)
        : base(
            "CARRIER_STATUS_AUTHORITY_REQUIRED",
            $"Shipment {shipmentId} is managed by carrier '{carrierCode}'. Status must come from carrier tracking sync or a verified webhook.")
    {
        ShipmentId = shipmentId;
        CarrierCode = carrierCode;
    }

    public Guid ShipmentId { get; }
    public string CarrierCode { get; }
}

#endregion
