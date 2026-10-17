using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;

namespace PointOfSale.Tests.Shipping;

public sealed class ConfiguredManualDeliveryConvergenceReadOnlyTests
{
    [ConfiguredManualFinalFact]
    public async Task FreshManualUiDelivery_OriginalShipmentAndProcessedSqlOutboxAgree_ReadOnly()
    {
        var evidence = Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!;
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, "manual-runtime-manifest.json")));
        var m = manifest.RootElement; var tenant = m.GetProperty("tenantId").GetGuid(); var order = m.GetProperty("orderId").GetGuid(); var shipment = m.GetProperty("shipmentId").GetGuid();
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var c = new NpgsqlConnection(db.Database.GetConnectionString()); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", c, tx)) await ro.ExecuteNonQueryAsync();
        var statuses = new List<ShipmentStatusChangedEvent>(); var facts = new List<object>();
        await using (var q = new NpgsqlCommand("SELECT \"Id\",\"EventType\",\"EventVersion\",\"ProcessedAtUtc\",\"RetryCount\",\"EventPayload\",\"RequiresOperatorReconciliation\" FROM \"OutboxMessages\" WHERE \"TenantId\"=@tenant AND \"AggregateId\" IN (@order,@shipment) ORDER BY \"EventVersion\",\"CreatedAtUtc\",\"Id\"", c, tx))
        {
            q.Parameters.AddWithValue("tenant", tenant); q.Parameters.AddWithValue("order", order); q.Parameters.AddWithValue("shipment", shipment);
            await using var r = await q.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var type = r.GetString(1); Assert.False(r.IsDBNull(3)); Assert.Equal(0, r.GetInt32(4)); Assert.False(r.GetBoolean(6));
                if (type.StartsWith(typeof(ShipmentStatusChangedEvent).FullName!, StringComparison.Ordinal))
                {
                    var fact = JsonSerializer.Deserialize<ShipmentStatusChangedEvent>(r.GetString(5), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                    Assert.Equal(tenant, fact.TenantId); Assert.Equal(order, fact.OrderId); Assert.Equal(shipment, fact.ShipmentId); statuses.Add(fact);
                }
                facts.Add(new { Id=r.GetGuid(0), Type=type, Version=r.GetInt32(2), ProcessedAt=r.GetDateTime(3), RetryCount=r.GetInt32(4) });
            }
        }
        await File.WriteAllTextAsync(Path.Combine(evidence, $"manual-event-convergence-{order:N}.json"), JsonSerializer.Serialize(new {
            Tenant=tenant, Order=order, Shipment=shipment, Facts=facts, StateTransitions=statuses.Select(s => new { s.PreviousStatus, s.NewStatus }), ReadOnly=true, ObservedUtc=DateTime.UtcNow }));
        // The normal authority-recovery worker also records one Pending -> Pending
        // SQL/order synchronization fact (ShipmentService.RepairShipmentAuthorityAsync).
        // It is not pickup/delivery. Require it explicitly, never accept arbitrary
        // additional transitions or duplicate delivery facts.
        Assert.Equal(6, statuses.Count);
        Assert.Single(statuses.Where(s => s.PreviousStatus == ShipmentStatus.Pending && s.NewStatus == ShipmentStatus.Pending));
        foreach (var (previous, next) in new[] { (ShipmentStatus.Pending,ShipmentStatus.ReadyForPickup), (ShipmentStatus.ReadyForPickup,ShipmentStatus.PickedUp),
            (ShipmentStatus.PickedUp,ShipmentStatus.InTransit), (ShipmentStatus.InTransit,ShipmentStatus.OutForDelivery), (ShipmentStatus.OutForDelivery,ShipmentStatus.Delivered) })
            Assert.Single(statuses.Where(s => s.PreviousStatus == previous && s.NewStatus == next));
        await tx.RollbackAsync();
    }
}
