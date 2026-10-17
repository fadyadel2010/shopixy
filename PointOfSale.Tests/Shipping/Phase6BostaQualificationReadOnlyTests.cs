using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.Shipping;

// Explicitly opted-in, existing-data observation only. No SaveChanges/provider call.
public sealed class Phase6BostaQualificationReadOnlyTests
{
    [Phase6ReadOnlyFact]
    public async Task Authorized_order_original_contract_and_booking_evidence_read_only()
    {
        var tenant = Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
        var orderId = Guid.Parse("ac89be7f-5e8c-4834-bcec-a1d14d5af2e6");
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var order = await db.Orders.IgnoreQueryFilters().AsNoTracking().Include(o => o.Items)
            .SingleAsync(o => o.Id == orderId && o.TenantId == tenant);
        var shipments = await db.Shipments.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.OrderId == orderId && s.TenantId == tenant)
            .Select(s => new { s.Id, s.CarrierCode, s.ExternalRegistrationState, s.ExternalRegistrationAttemptCount,
                s.CarrierRequestId, s.ExternalReferenceId, s.TrackingNumber }).ToListAsync();
        var reservations = await db.InventoryReservations.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.OrderId == orderId && r.TenantId == tenant)
            .Select(r => new { r.OrderItemId, r.ProductId, r.VariantId, r.WarehouseId, r.Status,
                r.ReservedQuantity, r.ReleasedQuantity, r.CommittedQuantity }).ToListAsync();
        var warehouse = await db.Warehouses.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(w => w.Id == order.WarehouseId && w.TenantId == tenant);
        var physical = PointOfSale.Core.Policies.OrderInventoryReservationSnapshot.PhysicalItems(order);
        var result = new { observedUtc = DateTime.UtcNow, readOnly = true,
            order.Id, order.TenantId, order.OnlineCustomerId, order.OrderNumber,
            status = order.Status.ToString(), paymentStatus = order.PaymentStatus.ToString(),
            fulfillmentStatus = order.FulfillmentStatus.ToString(), fulfillmentMode = order.FulfillmentMode.ToString(),
            order.FulfillmentCarrierCode, order.ShippingMethodId, order.WarehouseId,
            order.Total, order.Currency, order.PaymentMethodCode,
            destinationAdmissionPresent = order.ShippingAddressAdmissionEvidenceId != null,
            order.ShippingCountryPlaceId, order.ShippingLocalityPlaceId, order.ShippingDistrictPlaceId,
            streetPresent = !string.IsNullOrWhiteSpace(order.ShippingAddress),
            warehouse = new { warehouse.Id, warehouse.TenantId, warehouse.IsActive,
                addressPresent = !string.IsNullOrWhiteSpace(warehouse.Address) },
            physicalLines = physical.Select(i => new { i.Id, i.ProductId, i.VariantId, i.WarehouseId, i.Quantity }),
            reservations, shipments };
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("SHOPIXY_PHASE6_EVIDENCE")!, "bosta-order-readonly.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        Assert.Equal(40m, order.Total);
        Assert.Equal("EGP", order.Currency);
        Assert.Single(physical);
        Assert.Empty(shipments);
        await tx.RollbackAsync();
    }
}

public sealed class Phase6ReadOnlyFactAttribute : FactAttribute
{
    public Phase6ReadOnlyFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SHOPIXY_PHASE6_READONLY") != "1")
            Skip = "Explicit Phase 6 configured read-only observation required.";
    }
}
