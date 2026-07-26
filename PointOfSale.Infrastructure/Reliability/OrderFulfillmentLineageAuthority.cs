using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Reliability;

/// <summary>
/// SQL-authoritative order/shipment lineage used when replayed shipment evidence
/// tries to mutate order status or derivative fulfillment visibility.
/// </summary>
public static class OrderFulfillmentLineageAuthority
{
    public static async Task<OrderShipmentLineageAuthoritySnapshot?> LoadCurrentShipmentAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid orderId,
        Guid shipmentId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await (
                from shipment in dbContext.Shipments.IgnoreQueryFilters().AsNoTracking()
                join order in dbContext.Orders.IgnoreQueryFilters().AsNoTracking()
                    on new { shipment.TenantId, shipment.OrderId }
                    equals new { order.TenantId, OrderId = order.Id }
                where shipment.TenantId == tenantId &&
                      shipment.OrderId == orderId &&
                      shipment.Id == shipmentId &&
                      !shipment.IsDeleted &&
                      !order.IsDeleted
                select new OrderShipmentLineageAuthoritySnapshot(
                    order.TenantId,
                    order.Id,
                    shipment.Id,
                    order.Status,
                    shipment.Status,
                    shipment.TrackingNumber,
                    shipment.CarrierCode,
                    shipment.ShippedAtUtc,
                    shipment.DeliveredAtUtc,
                    shipment.CancelledAtUtc,
                    shipment.ReturnedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        if (snapshot == null)
        {
            OperationalConvergenceTelemetry.RecordAuthorityFailure(
                "order_fulfillment_lineage",
                OperationalConvergenceAuthority.OrderFulfillment,
                OperationalConvergenceReason.MissingSqlAuthority,
                tenantId);
        }

        return snapshot;
    }

    public static async Task<OrderShipmentVisibilityAuthoritySnapshot?> LoadCurrentShipmentVisibilityAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await dbContext.Shipments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(shipment =>
                shipment.TenantId == tenantId &&
                shipment.OrderId == orderId &&
                !shipment.IsDeleted &&
                dbContext.Orders
                    .IgnoreQueryFilters()
                    .Any(order =>
                        order.TenantId == tenantId &&
                        order.Id == orderId &&
                        !order.IsDeleted))
            .Select(shipment => new OrderShipmentVisibilityAuthoritySnapshot(
                shipment.Id,
                shipment.Status,
                shipment.TrackingNumber,
                shipment.CarrierCode,
                shipment.ShippedAtUtc,
                shipment.DeliveredAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        if (snapshot == null)
        {
            OperationalConvergenceTelemetry.RecordAuthorityFailure(
                "order_fulfillment_visibility",
                OperationalConvergenceAuthority.OrderFulfillment,
                OperationalConvergenceReason.MissingSqlAuthority,
                tenantId);
        }

        return snapshot;
    }

    public static string ToProjectionShippingStatus(ShipmentStatus shipmentStatus)
    {
        return shipmentStatus switch
        {
            ShipmentStatus.PendingExternalRegistration => "Pending",
            ShipmentStatus.Pending => "Pending",
            ShipmentStatus.ReadyForPickup => "Processing",
            ShipmentStatus.PickedUp => "Shipped",
            ShipmentStatus.InTransit => "Shipped",
            ShipmentStatus.OutForDelivery => "OutForDelivery",
            ShipmentStatus.Delivered => "Delivered",
            ShipmentStatus.Cancelled => "Cancelled",
            ShipmentStatus.Failed => "Failed",
            ShipmentStatus.Returned => "Returned",
            _ => "Unavailable"
        };
    }
}

public sealed record OrderShipmentLineageAuthoritySnapshot(
    Guid TenantId,
    Guid OrderId,
    Guid ShipmentId,
    OrderStatus OrderStatus,
    ShipmentStatus ShipmentStatus,
    string? TrackingNumber,
    string? CarrierCode,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc,
    DateTime? CancelledAtUtc,
    DateTime? ReturnedAtUtc);

public sealed record OrderShipmentVisibilityAuthoritySnapshot(
    Guid ShipmentId,
    ShipmentStatus ShipmentStatus,
    string? TrackingNumber,
    string? CarrierCode,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc);
