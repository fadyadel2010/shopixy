using PointOfSale.API.DTOs.Admin;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Admin;

/// <summary>
/// Admin Order Service Interface.
/// 
/// PHASE 6.1: ADMIN ORDER MANAGEMENT
/// 
/// ARCHITECTURAL RULES (STRICT):
/// - PostgreSQL is the ONLY source of truth for orders
/// - MongoDB is NOT used for admin operations
/// - All operations are tenant-isolated
/// - Events are reused from existing order/payment/shipment events
/// - Prefer rejecting invalid actions over auto-fixing
/// </summary>
public interface IAdminOrderService
{
    /// <summary>
    /// Lists orders with pagination and filtering.
    /// 
    /// Filters:
    /// - Status (optional)
    /// - PaymentStatus (optional)
    /// - FromDate/ToDate (optional)
    /// - CustomerId (optional)
    /// </summary>
    Task<AdminPagedResult<AdminOrderListItemDto>> ListOrdersAsync(
        Guid tenantId,
        AdminOrderListQuery query,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets full order details including items, payments, and shipment.
    /// </summary>
    Task<AdminOrderDetailsDto?> GetOrderDetailsAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a CommerceHub vendor sub-order owned by the current tenant.
    /// This is intentionally separate from the Storefront order detail contract.
    /// </summary>
    Task<AdminOrderDetailsDto?> GetCommerceHubVendorOrderDetailsAsync(
        Guid tenantId,
        Guid subOrderId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<AdminOrderAttachmentDownloadResult> OpenCheckoutAttachmentDownloadAsync(
        Guid tenantId,
        Guid orderId,
        Guid attachmentId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates order status (manual override).
    /// 
    /// Rules:
    /// - Cannot move backward arbitrarily
    /// - Delivered orders are immutable
    /// - Must emit OrderStatusChangedEvent
    /// </summary>
    Task<AdminOrderStatusUpdateResponse> UpdateOrderStatusAsync(
        Guid tenantId,
        Guid orderId,
        AdminUpdateOrderStatusRequest request,
        uint expectedRowVersion,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an order with side effects.
    /// 
    /// Rules:
    /// - Only allowed if order is NOT delivered
    /// - Must release inventory
    /// - Must cancel shipment (if exists)
    /// - Must update payment intent (if pending)
    /// - Must emit existing events
    /// </summary>
    Task<AdminOrderCancellationResponse> CancelOrderAsync(
        Guid tenantId,
        Guid orderId,
        AdminCancelOrderRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed record AdminOrderAttachmentDownloadResult(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes);

#region Admin Order Exceptions

/// <summary>
/// Base exception for admin order operations.
/// </summary>
public class AdminOrderException : Exception
{
    public string ErrorCode { get; }

    public AdminOrderException(string errorCode, string message) 
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public AdminOrderException(string errorCode, string message, Exception innerException) 
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Thrown when order is not found.
/// HTTP 404
/// </summary>
public class AdminOrderNotFoundException : AdminOrderException
{
    public Guid OrderId { get; }

    public AdminOrderNotFoundException(Guid orderId) 
        : base("ORDER_NOT_FOUND", $"Order {orderId} not found")
    {
        OrderId = orderId;
    }
}

public class AdminOrderAttachmentNotFoundException : AdminOrderException
{
    public Guid AttachmentId { get; }

    public AdminOrderAttachmentNotFoundException(Guid attachmentId)
        : base("ATTACHMENT_NOT_FOUND", $"Attachment {attachmentId} was not found for this order.")
    {
        AttachmentId = attachmentId;
    }
}

public class AdminOrderAttachmentNotDownloadableException : AdminOrderException
{
    public Guid AttachmentId { get; }

    public AdminOrderAttachmentNotDownloadableException(Guid attachmentId)
        : base("ATTACHMENT_NOT_DOWNLOADABLE", "Attachment is not available for download.")
    {
        AttachmentId = attachmentId;
    }
}

public class AdminOrderAttachmentIntegrityException : AdminOrderException
{
    public Guid AttachmentId { get; }

    public AdminOrderAttachmentIntegrityException(Guid attachmentId, Exception innerException)
        : base("ATTACHMENT_INTEGRITY_CHECK_FAILED", "Attachment could not be verified for download.", innerException)
    {
        AttachmentId = attachmentId;
    }
}

/// <summary>
/// Thrown when status transition is invalid.
/// HTTP 409
/// </summary>
public class InvalidAdminOrderStateException : AdminOrderException
{
    public Guid OrderId { get; }
    public OrderStatus CurrentStatus { get; }
    public OrderStatus RequestedStatus { get; }

    public InvalidAdminOrderStateException(Guid orderId, OrderStatus currentStatus, OrderStatus requestedStatus, string? customMessage = null) 
        : base("INVALID_ORDER_STATE", 
            customMessage ?? $"Cannot transition order from {currentStatus} to {requestedStatus}")
    {
        OrderId = orderId;
        CurrentStatus = currentStatus;
        RequestedStatus = requestedStatus;
    }
}

/// <summary>
/// Thrown when trying to modify a delivered order.
/// HTTP 409
/// </summary>
public class AdminOrderImmutableException : AdminOrderException
{
    public Guid OrderId { get; }
    public OrderStatus CurrentStatus { get; }

    public AdminOrderImmutableException(Guid orderId, OrderStatus currentStatus) 
        : base("ORDER_IMMUTABLE", 
            $"Order {orderId} is in {currentStatus} status and cannot be modified")
    {
        OrderId = orderId;
        CurrentStatus = currentStatus;
    }
}

public class AdminOrderConcurrencyException : AdminOrderException
{
    public Guid OrderId { get; }

    public AdminOrderConcurrencyException(Guid orderId)
        : base("ORDER_VERSION_CONFLICT", $"Order {orderId} was modified by another administrator. Refresh before retrying the lifecycle change.")
    {
        OrderId = orderId;
    }
}

/// <summary>
/// Thrown when cancelling an order would bypass the refund/payment lifecycle.
/// HTTP 409
/// </summary>
public class AdminOrderPaymentRequiresRefundException : AdminOrderException
{
    public Guid OrderId { get; }
    public PaymentStatus PaymentStatus { get; }

    public AdminOrderPaymentRequiresRefundException(Guid orderId, PaymentStatus paymentStatus)
        : base(
            "ORDER_PAYMENT_REQUIRES_REFUND",
            $"Order {orderId} has recognized payment state {paymentStatus} and cannot be cancelled directly. Use the refund lifecycle so accounting, inventory, and affiliate reversals stay synchronized.")
    {
        OrderId = orderId;
        PaymentStatus = paymentStatus;
    }
}

/// <summary>
/// Thrown when cancellation reason is missing.
/// HTTP 400
/// </summary>
public class CancellationReasonRequiredException : AdminOrderException
{
    public CancellationReasonRequiredException() 
        : base("CANCELLATION_REASON_REQUIRED", "Cancellation reason is required")
    {
    }
}

#endregion
