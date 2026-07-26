using PointOfSale.API.Services.AI.Models;

namespace PointOfSale.API.Services.AI.OrderTracking;

/// <summary>
/// Provides order and shipment context for AI post-purchase support.
/// PHASE 3: Event-Aware, Order-Context Intelligence.
/// 
/// RULES:
/// - Read-only access to order/shipment data
/// - Returns null if order not found or customer doesn't have access
/// - Tenant-isolated (customer can only see their own orders)
/// </summary>
public interface IOrderContextProvider
{
    /// <summary>
    /// Get order tracking context for a specific order.
    /// Returns null if order not found or customer doesn't have access.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="customerId">Customer ID (for access control)</param>
    /// <param name="orderId">Order ID to lookup</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    Task<OrderTrackingContext?> GetOrderContextAsync(
        Guid tenantId,
        Guid customerId,
        Guid orderId,
        string correlationId);

    /// <summary>
    /// Get order tracking context by order number.
    /// Returns null if order not found or customer doesn't have access.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="customerId">Customer ID (for access control)</param>
    /// <param name="orderNumber">Order number (e.g., "ORD-10293")</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    Task<OrderTrackingContext?> GetOrderContextByNumberAsync(
        Guid tenantId,
        Guid customerId,
        string orderNumber,
        string correlationId);

    /// <summary>
    /// Get the most recent order for a customer.
    /// Used when customer asks about "my order" without specifying which one.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="customerId">Customer ID</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    Task<OrderTrackingContext?> GetLatestOrderContextAsync(
        Guid tenantId,
        Guid customerId,
        string correlationId);

    /// <summary>
    /// Get shipment tracking context for an order.
    /// Returns context with Exists=false if no shipment yet.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="orderId">Order ID</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    Task<ShipmentTrackingContext> GetShipmentContextAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId);
}
