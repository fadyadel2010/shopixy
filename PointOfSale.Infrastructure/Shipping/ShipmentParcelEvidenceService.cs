using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Shipping;

public sealed class ShipmentParcelEvidenceService(ApplicationDbContext db, ITenantContext tenantContext, ICarrierIntegrationAuthority? authority = null)
{
    public async Task<ParcelEvidenceView> ReadAsync(Guid tenantId, Guid shipmentId, CancellationToken ct = default)
    {
        EnsureTenant(tenantId);
        var shipment = await Load(tenantId, shipmentId, ct);
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == shipment.OrderId && o.TenantId == tenantId && !o.IsDeleted, ct);
        var cancellation = await db.ShipmentCancellationOperations.AnyAsync(x => x.TenantId == tenantId && x.ShipmentId == shipmentId, ct);
        var evidence = shipment.ParcelEvidenceJson == null ? null : Restore(shipment, order);
        var quoteBlockers = new List<string>();
        var quote = CarrierOperationSupports.Describe(shipment.CarrierCode).Single(operation => operation.Operation == "quote");
        if (quote.Implementation != "Implemented") quoteBlockers.AddRange(quote.Limitations.Count > 0 ? quote.Limitations : ["REMOTE_QUOTES_UNSUPPORTED"]);
        if (CarrierRegistrationEvidence.HasOutboundEvidence(shipment) ||
            shipment.Status is not (ShipmentStatus.Pending or ShipmentStatus.PendingExternalRegistration)) quoteBlockers.Add("PARCEL_QUOTE_LOCKED");
        if (cancellation || order.Status == OrderStatus.Cancelled) quoteBlockers.Add("CANCELLATION_BARRIER");
        if (evidence == null) quoteBlockers.Add("PARCEL_MEASUREMENTS_REQUIRED");
        // An uncaptured origin is independently resolved/validated by the quote
        // command. Read eligibility is not canonical route qualification.
        if (shipment.OriginCountryIso2 != null && shipment.ShippingCountry != null &&
            !string.Equals(shipment.OriginCountryIso2, shipment.ShippingCountry, StringComparison.OrdinalIgnoreCase))
            quoteBlockers.Add("CARRIER_CUSTOMS_VALUE_SEMANTICS_UNPROVEN");
        if (quoteBlockers.Count == 0)
        {
            if (authority == null) quoteBlockers.Add("CARRIER_QUOTE_READINESS_UNAVAILABLE");
            else
            {
                var readiness = await authority.GetIntegrationReadinessAsync(tenantId, shipment.CarrierCode,
                    shipment.ShippingMethodId, shipment.OriginWarehouseId ?? order.WarehouseId, CarrierIntegrationPurpose.Quote, ct);
                quoteBlockers.AddRange(readiness.Blockers);
                if (!readiness.CanExecute && readiness.Blockers.Count == 0) quoteBlockers.Add("CARRIER_NOT_READY");
                var service = await authority.GetServiceLevelReadinessAsync(tenantId, shipment.CarrierCode, shipment.ServiceLevel, ct);
                quoteBlockers.AddRange(service.Blockers);
                if (!service.CanExecute && service.Blockers.Count == 0) quoteBlockers.Add("SERVICE_LEVEL_UNAVAILABLE");
            }
        }
        return new(evidence,
            CanMeasure(shipment) && shipment.ParcelEvidenceJson == null && !cancellation && order.Status != OrderStatus.Cancelled, ParcelMeasurements.Contents(order),
            shipment.ExternalRegistrationState, shipment.ExternalRegistrationError,
            shipment.OriginCountryIso2 == null || shipment.ShippingCountry == null ? null :
                !string.Equals(shipment.OriginCountryIso2, shipment.ShippingCountry, StringComparison.OrdinalIgnoreCase))
        { ShipmentId = shipment.Id, CarrierCode = shipment.CarrierCode, RequiresGoodsDeclaration = shipment.CarrierCode == CarrierCodes.Bosta,
            CanQuote = quoteBlockers.Count == 0, QuoteBlockers = quoteBlockers.Distinct().ToArray() };
    }

    public async Task<ParcelEvidence> SaveAsync(Guid tenantId, Guid shipmentId, Guid actor, ParcelMeasurement input, CancellationToken ct = default)
    {
        EnsureTenant(tenantId);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        if (db.Database.IsNpgsql())
            await db.Shipments.FromSqlInterpolated($"SELECT * FROM \"Shipments\" WHERE \"Id\"={shipmentId} AND \"TenantId\"={tenantId} AND NOT \"IsDeleted\" FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        var shipment = await Load(tenantId, shipmentId, ct);
        await db.Entry(shipment).ReloadAsync(ct);
        if (!CanMeasure(shipment) || await db.ShipmentCancellationOperations.AnyAsync(x => x.TenantId == tenantId && x.ShipmentId == shipmentId, ct))
            throw new InvalidOperationException("PARCEL_EVIDENCE_LOCKED");
        var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == shipment.OrderId && o.TenantId == tenantId && !o.IsDeleted, ct);
        if (order.Status == OrderStatus.Cancelled) throw new InvalidOperationException("PARCEL_EVIDENCE_LOCKED");
        if (shipment.CarrierCode is CarrierCodes.Dhl or CarrierCodes.FedEx or CarrierCodes.Bosta or CarrierCodes.Aramex)
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == actor && u.TenantId == tenantId && u.IsActive && !u.IsDeleted, ct);
            if (user == null || !Core.Security.PosRolePermissionCatalog.IsGranted(user.Role, Core.Security.PosPermissions.ShippingFulfill))
                throw new UnauthorizedAccessException();
        }
        var evidence = shipment.CarrierCode == CarrierCodes.FedEx
            ? FedExParcelMeasurements.Admit(input, shipment, order, actor)
            : ParcelMeasurements.Admit(input, shipment, order, actor);
        if (shipment.ParcelEvidenceJson != null)
        {
            var previous = Restore(shipment, order);
            if (previous.Measurement != input) throw new InvalidOperationException("PARCEL_EVIDENCE_IMMUTABLE");
            return previous;
        }
        shipment.ParcelEvidenceJson = JsonSerializer.Serialize(evidence);
        // Do not copy to legacy WeightKg numeric(10,3); the snapshot preserves full decimal precision.
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return evidence;
    }

    private void EnsureTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty || tenantContext.RequiredTenantId != tenantId) throw new UnauthorizedAccessException();
    }
    private Task<Shipment> Load(Guid tenantId, Guid shipmentId, CancellationToken ct) => LoadCore(tenantId, shipmentId, ct);
    private async Task<Shipment> LoadCore(Guid tenantId, Guid shipmentId, CancellationToken ct) =>
        await db.Shipments.SingleOrDefaultAsync(s => s.Id == shipmentId && s.TenantId == tenantId && !s.IsDeleted, ct)
        ?? throw new KeyNotFoundException();
    private static ParcelEvidence Restore(Shipment s, Order o) => s.CarrierCode == CarrierCodes.FedEx
        ? FedExParcelMeasurements.Restore(s, o) : ParcelMeasurements.Restore(s, o);
    private static bool CanMeasure(Shipment s) => s.CarrierCode is CarrierCodes.Dhl or CarrierCodes.FedEx or CarrierCodes.Bosta or CarrierCodes.Aramex &&
        !CarrierRegistrationEvidence.HasOutboundEvidence(s) && s.Status is ShipmentStatus.Pending or ShipmentStatus.PendingExternalRegistration;
}

public sealed record ParcelEvidenceView(ParcelEvidence? Evidence, bool CanMeasure, IReadOnlyList<ParcelContent> Contents,
    string? RegistrationState, string? RegistrationError, bool? IsInternational)
{
    public Guid ShipmentId { get; init; }
    public string? CarrierCode { get; init; }
    public bool RequiresGoodsDeclaration { get; init; }
    public bool CanQuote { get; init; }
    public IReadOnlyList<string> QuoteBlockers { get; init; } = [];
}
