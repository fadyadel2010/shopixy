using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Service for syncing OrderStatus based on Shipment and Payment events.
/// 
/// PHASE 2: EVENT-DRIVEN ORDER STATUS SYNC
/// 
/// ARCHITECTURAL RULES (NON-NEGOTIABLE):
/// - Orders do NOT decide their status - they REACT to events
/// - This service is called by shipment/payment event handlers
/// - One-way sync only (events → orders)
/// - Never polls, always reacts to events
/// 
/// CANONICAL ORDER LIFECYCLE:
/// - Pending:    Order created, not paid
/// - Confirmed:  Paid, awaiting fulfillment
/// - Processing: Shipment created / preparing
/// - Shipped:    Shipment in transit
/// - Delivered:  Shipment delivered
/// - Cancelled:  Order cancelled
/// - Returned:   Order returned
/// 
/// SAFETY RULES:
/// - Only move forward (Processing → Shipped is valid, Shipped → Processing is NOT)
/// - Never downgrade Delivered orders
/// - Never override Cancelled / Returned orders (terminal states)
/// </summary>
public interface IOrderStatusSyncService
{
    /// <summary>
    /// Sync order status based on shipment status change.
    /// Called when:
    /// - Shipment created successfully
    /// - Shipment status updated via carrier sync
    /// - Shipment status updated via webhook
    /// - Shipment status updated manually by admin
    /// </summary>
    /// <param name="tenantId">Tenant ID (for security isolation)</param>
    /// <param name="orderId">Order ID</param>
    /// <param name="shipmentId">Shipment ID</param>
    /// <param name="newShipmentStatus">New shipment status</param>
    /// <param name="source">Source of the status change (for logging)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating if order status was updated</returns>
    Task<OrderStatusSyncResult> SyncFromShipmentAsync(
        Guid tenantId,
        Guid orderId,
        Guid shipmentId,
        ShipmentStatus newShipmentStatus,
        string source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync order status based on payment confirmation.
    /// Called when:
    /// - Payment is confirmed (Pending order → Confirmed)
    /// 
    /// Rule: If OrderStatus = Pending AND PaymentStatus = Paid → OrderStatus = Confirmed
    /// </summary>
    /// <param name="tenantId">Tenant ID (for security isolation)</param>
    /// <param name="orderId">Order ID</param>
    /// <param name="newPaymentStatus">New payment status</param>
    /// <param name="source">Source of the status change (for logging)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating if order status was updated</returns>
    Task<OrderStatusSyncResult> SyncFromPaymentAsync(
        Guid tenantId,
        Guid orderId,
        PaymentStatus newPaymentStatus,
        string source,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of order status sync operation.
/// </summary>
public class OrderStatusSyncResult
{
    public bool Success { get; set; }
    public bool StatusChanged { get; set; }
    public OrderStatus? OldStatus { get; set; }
    public OrderStatus? NewStatus { get; set; }
    public string? Message { get; set; }

    public static OrderStatusSyncResult NoChange(OrderStatus currentStatus, string reason) => new()
    {
        Success = true,
        StatusChanged = false,
        OldStatus = currentStatus,
        NewStatus = currentStatus,
        Message = reason
    };

    public static OrderStatusSyncResult Changed(OrderStatus oldStatus, OrderStatus newStatus) => new()
    {
        Success = true,
        StatusChanged = true,
        OldStatus = oldStatus,
        NewStatus = newStatus,
        Message = $"Order status changed from {oldStatus} to {newStatus}"
    };

    public static OrderStatusSyncResult Failed(string error) => new()
    {
        Success = false,
        StatusChanged = false,
        Message = error
    };
}
