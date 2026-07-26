using Microsoft.Extensions.Logging;
using PointOfSale.API.Services.AI.Models;

namespace PointOfSale.API.Services.AI.OrderTracking;

/// <summary>
/// Provides order and shipment context for AI post-purchase support.
/// PHASE 3: Event-Aware, Order-Context Intelligence.
/// 
/// This implementation fetches data from MongoDB (read store) for fast access.
/// The AI uses this data to answer customer questions about their orders.
/// 
/// RULES:
/// - Read-only access
/// - Tenant-isolated
/// - Customer can only access their own orders
/// </summary>
public class OrderContextProvider : IOrderContextProvider
{
    private readonly IOrderTrackingRepository _orderRepository;
    private readonly ILogger<OrderContextProvider> _logger;

    public OrderContextProvider(
        IOrderTrackingRepository orderRepository,
        ILogger<OrderContextProvider> logger)
    {
        _orderRepository = orderRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OrderTrackingContext?> GetOrderContextAsync(
        Guid tenantId,
        Guid customerId,
        Guid orderId,
        string correlationId)
    {
        try
        {
            var order = await _orderRepository.GetByIdAsync(tenantId, orderId);
            
            // Check if storefront order belongs to the online customer.
            if (order == null || !BelongsToOnlineCustomer(order, customerId))
            {
                _logger.LogDebug(
                    "Order not found or access denied | TenantId: {TenantId} | CustomerId: {CustomerId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                    tenantId, customerId, orderId, correlationId);
                return null;
            }
            
            return MapToContext(order);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to get order context | TenantId: {TenantId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                tenantId, orderId, correlationId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<OrderTrackingContext?> GetOrderContextByNumberAsync(
        Guid tenantId,
        Guid customerId,
        string orderNumber,
        string correlationId)
    {
        try
        {
            var order = await _orderRepository.GetByOrderNumberAsync(tenantId, orderNumber);
            
            // Check if storefront order belongs to the online customer.
            if (order == null || !BelongsToOnlineCustomer(order, customerId))
            {
                _logger.LogDebug(
                    "Order not found or access denied | TenantId: {TenantId} | CustomerId: {CustomerId} | OrderNumber: {OrderNumber} | CorrelationId: {CorrelationId}",
                    tenantId, customerId, orderNumber, correlationId);
                return null;
            }
            
            return MapToContext(order);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to get order context by number | TenantId: {TenantId} | OrderNumber: {OrderNumber} | CorrelationId: {CorrelationId}",
                tenantId, orderNumber, correlationId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<OrderTrackingContext?> GetLatestOrderContextAsync(
        Guid tenantId,
        Guid customerId,
        string correlationId)
    {
        try
        {
            var order = await _orderRepository.GetLatestByCustomerAsync(tenantId, customerId);
            
            if (order == null)
            {
                _logger.LogDebug(
                    "No orders found for customer | TenantId: {TenantId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
                    tenantId, customerId, correlationId);
                return null;
            }
            
            return MapToContext(order);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to get latest order context | TenantId: {TenantId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
                tenantId, customerId, correlationId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ShipmentTrackingContext> GetShipmentContextAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId)
    {
        try
        {
            var shipment = await _orderRepository.GetShipmentAsync(tenantId, orderId);
            
            if (shipment == null)
            {
                // No shipment yet - return empty context
                return new ShipmentTrackingContext
                {
                    Exists = false
                };
            }
            
            return MapShipmentToContext(shipment);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to get shipment context | TenantId: {TenantId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                tenantId, orderId, correlationId);
            
            // Return empty context on error
            return new ShipmentTrackingContext
            {
                Exists = false
            };
        }
    }

    /// <summary>
    /// Map order document to tracking context.
    /// </summary>
    private OrderTrackingContext MapToContext(OrderTrackingDocument order)
    {
        return new OrderTrackingContext
        {
            Id = order.OrderId,
            Number = order.OrderNumber ?? $"ORD-{order.OrderId.ToString()[..8].ToUpper()}",
            Status = MapOrderStatus(order.Status),
            CreatedAt = order.CreatedAtUtc,
            Total = order.GrandTotal,
            Currency = order.Currency ?? "EGP",
            IsCancelled = order.Status == "Cancelled",
            CancellationReason = order.CancellationReason,
            Items = order.Items?.Select(i => new OrderItemTrackingContext
            {
                Name = i.ProductName ?? "Item",
                Quantity = i.Quantity,
                Price = i.UnitPrice,
                Variant = i.VariantName
            }).ToList() ?? new(),
            Payment = new PaymentTrackingContext
            {
                Status = MapPaymentStatus(order.PaymentStatus),
                Method = order.PaymentMethod ?? "Unknown",
                Amount = order.GrandTotal
            }
        };
    }

    /// <summary>
    /// Map shipment document to tracking context.
    /// </summary>
    private ShipmentTrackingContext MapShipmentToContext(ShipmentTrackingDocument shipment)
    {
        return new ShipmentTrackingContext
        {
            Exists = true,
            Carrier = shipment.CarrierName,
            TrackingNumber = shipment.TrackingNumber,
            Status = MapShipmentStatus(shipment.Status),
            EstimatedDelivery = shipment.EstimatedDeliveryDate,
            LastLocation = shipment.LastLocation,
            Failed = shipment.Status == "DeliveryFailed" || shipment.Status == "Failed",
            FailureReason = shipment.FailureReason,
            Timeline = shipment.Events?.Select(e => new ShipmentEventContext
            {
                Status = MapShipmentStatus(e.Status),
                At = e.OccurredAtUtc,
                Location = e.Location,
                Notes = e.Notes
            }).ToList() ?? new()
        };
    }

    /// <summary>
    /// Map internal order status to human-readable status.
    /// </summary>
    private static string MapOrderStatus(string? status)
    {
        return status?.ToLower() switch
        {
            "pending" => "Pending",
            "confirmed" => "Confirmed",
            "processing" => "Processing",
            "readyforshipment" or "ready_for_shipment" => "Ready for Shipment",
            "shipped" => "Shipped",
            "delivered" => "Delivered",
            "cancelled" => "Cancelled",
            "returned" => "Returned",
            "refunded" => "Refunded",
            _ => "Processing"
        };
    }

    /// <summary>
    /// Map internal payment status to human-readable status.
    /// </summary>
    private static string MapPaymentStatus(string? status)
    {
        return status?.ToLower() switch
        {
            "pending" => "Pending",
            "paid" => "Paid",
            "failed" => "Failed",
            "refunded" => "Refunded",
            "partiallyrefunded" or "partially_refunded" => "Partially Refunded",
            _ => "Pending"
        };
    }

    /// <summary>
    /// Map internal shipment status to human-readable status.
    /// </summary>
    private static string MapShipmentStatus(string? status)
    {
        return status?.ToLower() switch
        {
            "orderreceived" or "order_received" => "Order Received",
            "processing" => "Processing",
            "pickedup" or "picked_up" => "Picked Up",
            "intransit" or "in_transit" => "In Transit",
            "outfordelivery" or "out_for_delivery" => "Out for Delivery",
            "delivered" => "Delivered",
            "deliveryfailed" or "delivery_failed" or "failed" => "Delivery Failed",
            "returnedtosender" or "returned_to_sender" => "Returned to Sender",
            _ => status ?? "Unknown"
        };
    }

    private static bool BelongsToOnlineCustomer(OrderTrackingDocument order, Guid onlineCustomerId) =>
        (order.OnlineCustomerId.HasValue &&
         order.OnlineCustomerId.Value == onlineCustomerId) ||
        ((!order.OnlineCustomerId.HasValue || order.OnlineCustomerId.Value == Guid.Empty) &&
         order.CustomerId == onlineCustomerId);
}

// =============================================================================
// Document models and repository interface
// =============================================================================

/// <summary>
/// Repository interface for order tracking data.
/// </summary>
public interface IOrderTrackingRepository
{
    Task<OrderTrackingDocument?> GetByIdAsync(Guid tenantId, Guid orderId);
    Task<OrderTrackingDocument?> GetByOrderNumberAsync(Guid tenantId, string orderNumber);
    Task<OrderTrackingDocument?> GetLatestByCustomerAsync(Guid tenantId, Guid customerId);
    Task<ShipmentTrackingDocument?> GetShipmentAsync(Guid tenantId, Guid orderId);
}

/// <summary>
/// Order document for tracking.
/// </summary>
public class OrderTrackingDocument
{
    public Guid OrderId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? OnlineCustomerId { get; set; }
    public Guid CustomerId { get; set; }
    public string? OrderNumber { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public string? PaymentMethod { get; set; }
    public decimal GrandTotal { get; set; }
    public string? Currency { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public List<OrderItemTrackingDocument>? Items { get; set; }
}

public class OrderItemTrackingDocument
{
    public string? ProductName { get; set; }
    public string? VariantName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// Shipment document for tracking.
/// </summary>
public class ShipmentTrackingDocument
{
    public Guid OrderId { get; set; }
    public string? CarrierName { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Status { get; set; }
    public DateTime? EstimatedDeliveryDate { get; set; }
    public string? LastLocation { get; set; }
    public string? FailureReason { get; set; }
    public List<ShipmentEventDocument>? Events { get; set; }
}

public class ShipmentEventDocument
{
    public string? Status { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Stub implementation of order tracking repository.
/// In production, this would query MongoDB or PostgreSQL.
/// </summary>
public class StubOrderTrackingRepository : IOrderTrackingRepository
{
    public Task<OrderTrackingDocument?> GetByIdAsync(Guid tenantId, Guid orderId)
    {
        // Stub - returns null, actual implementation would query database
        return Task.FromResult<OrderTrackingDocument?>(null);
    }

    public Task<OrderTrackingDocument?> GetByOrderNumberAsync(Guid tenantId, string orderNumber)
    {
        return Task.FromResult<OrderTrackingDocument?>(null);
    }

    public Task<OrderTrackingDocument?> GetLatestByCustomerAsync(Guid tenantId, Guid customerId)
    {
        return Task.FromResult<OrderTrackingDocument?>(null);
    }

    public Task<ShipmentTrackingDocument?> GetShipmentAsync(Guid tenantId, Guid orderId)
    {
        return Task.FromResult<ShipmentTrackingDocument?>(null);
    }
}
