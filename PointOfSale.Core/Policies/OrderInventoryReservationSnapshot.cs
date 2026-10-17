using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Policies;

/// <summary>
/// Checkout's original physical allocation policy, retained in the existing immutable
/// order snapshot. A backorder is not a physical reservation. Missing legacy evidence
/// keeps the previous strict requirement; today's catalog cannot waive old allocations.
/// </summary>
public static class OrderInventoryReservationSnapshot
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Capture(Order order, IReadOnlyDictionary<(Guid ProductId, Guid? VariantId), decimal> physical)
    {
        Require(order.TenantId != Guid.Empty && order.Id != Guid.Empty && order.WarehouseId.HasValue);
        var lines = order.Items.Where(i => !i.IsDeleted).Select(i => new Line(i.Id, i.ProductId, i.VariantId,
            i.WarehouseId, i.Quantity, physical.ContainsKey((i.ProductId, i.VariantId)))).ToArray();
        foreach (var (key, quantity) in physical)
            Require(quantity > 0 && lines.Where(l => (l.ProductId, l.VariantId) == key).Sum(l => l.Quantity) == quantity);
        var envelope = JsonNode.Parse(order.PricingSnapshotJson ?? "{}")!.AsObject();
        Require(!envelope.ContainsKey("inventoryReservation"));
        envelope["inventoryReservation"] = JsonSerializer.SerializeToNode(new Snapshot(1, order.TenantId, order.Id, order.WarehouseId!.Value, lines), JsonOptions);
        var captured = envelope.ToJsonString(JsonOptions);
        // Validate before the first insert, using the same reader as binding/payment.
        Read(order, captured);
        return captured;
    }

    public static IReadOnlyList<OrderItem> PhysicalItems(Order order) => Read(order, order.PricingSnapshotJson);

    private static IReadOnlyList<OrderItem> Read(Order order, string? json)
    {
        var items = order.Items.Where(i => !i.IsDeleted && i.Quantity > 0).ToArray();
        if (string.IsNullOrWhiteSpace(json)) return items; // Legacy: no new exemption.
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("inventoryReservation", out var value)) return items;
            var snapshot = value.Deserialize<Snapshot>(JsonOptions);
            Require(snapshot != null && snapshot.Version == 1 && snapshot.TenantId == order.TenantId &&
                snapshot.OrderId == order.Id && snapshot.WarehouseId == order.WarehouseId && snapshot.WarehouseId != Guid.Empty &&
                snapshot.Lines != null && snapshot.Lines.Length == items.Length &&
                snapshot.Lines.Select(l => l.OrderItemId).Distinct().Count() == items.Length);
            foreach (var item in items)
            {
                var line = snapshot!.Lines.SingleOrDefault(l => l.OrderItemId == item.Id);
                Require(line != null && item.OrderId == order.Id && line.ProductId == item.ProductId &&
                    line.VariantId == item.VariantId && line.VariantId.HasValue && line.VariantId != Guid.Empty &&
                    line.WarehouseId == item.WarehouseId && line.WarehouseId == order.WarehouseId && line.Quantity == item.Quantity);
            }
            var required = snapshot!.Lines.Where(l => l.RequiresPhysicalReservation).Select(l => l.OrderItemId).ToHashSet();
            return items.Where(i => required.Contains(i.Id)).ToArray();
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidOperationException("INVENTORY_RESERVATION_SNAPSHOT_INVALID", ex); }
    }

    private static void Require(bool valid)
    { if (!valid) throw new InvalidOperationException("INVENTORY_RESERVATION_SNAPSHOT_INVALID"); }

    private sealed record Snapshot(int Version, Guid TenantId, Guid OrderId, Guid WarehouseId, Line[] Lines);
    private sealed record Line(Guid OrderItemId, Guid ProductId, Guid? VariantId, Guid? WarehouseId, int Quantity,
        [property: JsonRequired] bool RequiresPhysicalReservation);
}
