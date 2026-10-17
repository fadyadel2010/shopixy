using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Shipping;

public sealed class BostaNativeDestinationService(ApplicationDbContext db, ICarrierIntegrationAuthority authority, IShippingCarrierRegistry registry)
{
    public async Task<(CarrierAccount Account, CarrierCredentialsResolution Saved)> SavedAsync(Guid tenant, Guid accountId, CancellationToken ct)
    {
        var account = await db.CarrierAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a =>
            a.Id == accountId && a.TenantId == tenant && a.CarrierCode == "bosta" && !a.IsDeleted, ct)
            ?? throw new KeyNotFoundException("UNKNOWN_CARRIER_ACCOUNT");
        var saved = await authority.ResolveConnectionCredentialsAsync(tenant, "bosta", ct);
        if (!saved.Succeeded || saved.CarrierCode != "bosta" || saved.Configuration?.CarrierCode != "bosta" || saved.Credentials == null ||
            account.ActiveCredentialReferenceId == null || account.Environment?.ToString() != (saved.Credentials.UseSandbox ? "Sandbox" : "Production"))
            throw new ArgumentException("BOSTA_SAVED_CONFIGURATION_REQUIRED");
        return (account, saved);
    }
    public async Task<BostaGeographyResult> DiscoverAsync(Guid tenant, Guid accountId, string? cityId, CancellationToken ct)
    {
        var before = await SavedAsync(tenant, accountId, ct);
        if (registry.GetAdapter("bosta") is not IBostaGeographyDiscovery discovery) throw new ArgumentException("BOSTA_GEOGRAPHY_DISCOVERY_UNAVAILABLE");
        var result = await discovery.GetGeographyAsync(before.Saved.Credentials!, cityId, ct);
        await CheckCurrentAsync(tenant, before, ct);
        if (!result.Succeeded) throw new ArgumentException(result.ErrorCode ?? "BOSTA_GEOGRAPHY_DISCOVERY_FAILED");
        return result;
    }
    private async Task CheckCurrentAsync(Guid tenant, (CarrierAccount Account, CarrierCredentialsResolution Saved) before, CancellationToken ct)
    {
        var after = await SavedAsync(tenant, before.Account.Id, ct);
        if (before.Account.ActiveCredentialReferenceId != after.Account.ActiveCredentialReferenceId ||
            !BostaPickupVerificationCache.SameConfiguration(before.Saved, after.Saved)) throw new ArgumentException("BOSTA_SAVED_CONFIGURATION_CHANGED");
    }
    public async Task<BostaNativeDestination> SaveAsync(Guid tenant, Guid accountId, Guid shipmentId, string cityId, string districtId, CancellationToken ct)
    {
        var before = await SavedAsync(tenant, accountId, ct);
        var shipment = await db.Shipments.SingleOrDefaultAsync(s => s.Id == shipmentId && s.TenantId == tenant && !s.IsDeleted, ct)
            ?? throw new KeyNotFoundException("SHIPMENT_NOT_FOUND");
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == shipment.OrderId && o.TenantId == tenant && !o.IsDeleted, ct);
        EnsureEditable(shipment, order);
        if (registry.GetAdapter("bosta") is not IBostaGeographyDiscovery discovery) throw new ArgumentException("BOSTA_GEOGRAPHY_DISCOVERY_UNAVAILABLE");
        // Provider HTTP is outside the database transaction and durable create claim.
        var selected = BostaNativeDestination.Select(await discovery.GetGeographyAsync(before.Saved.Credentials!, cityId, ct), cityId, districtId);
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        if (db.Database.IsNpgsql())
            await db.Shipments.FromSqlInterpolated($"SELECT * FROM \"Shipments\" WHERE \"Id\"={shipmentId} AND \"TenantId\"={tenant} AND NOT \"IsDeleted\" FOR UPDATE").AsNoTracking().SingleAsync(ct);
        await db.Entry(shipment).ReloadAsync(ct);
        order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == shipment.OrderId && o.TenantId == tenant && !o.IsDeleted, ct);
        EnsureEditable(shipment, order);
        if (await db.ShipmentCancellationOperations.AnyAsync(c => c.TenantId == tenant && c.ShipmentId == shipmentId, ct))
            throw new ArgumentException("BOSTA_DESTINATION_LOCKED");
        await CheckCurrentAsync(tenant, before, ct);
        // PostgreSQL timestamps have microsecond precision; retain exactly the
        // same instant in JSON and scalar columns across a real database reload.
        var observedAt = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
        var snapshot = new BostaNativeDestination(BostaNativeDestination.SnapshotKind, 1, tenant, shipment.Id, order.Id, accountId,
            before.Saved.Configuration!.Id, before.Account.ActiveCredentialReferenceId, before.Saved.Credentials!.UseSandbox,
            BostaNativeDestination.CredentialHash(before.Saved.Credentials), BostaNativeDestination.AddressHash(shipment),
            selected.Country, selected.City, selected.District, observedAt);
        shipment.CarrierGeographicEnvironment = snapshot.UseSandbox ? "Sandbox" : "Production";
        shipment.CarrierGeographicAccountScope = "BostaCarrierAccount";
        shipment.CarrierGeographicAccountScopeId = accountId;
        shipment.CarrierGeographicResolvedAtUtc = snapshot.ObservedAtUtc;
        shipment.CarrierGeographicMappingSnapshotJson = JsonSerializer.Serialize(snapshot);
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return snapshot;
    }
    private static void EnsureEditable(Shipment s, Order o)
    {
        if (s.CarrierCode != "bosta" || o.FulfillmentCarrierCode != "bosta" || s.ServiceLevel != "standard" || o.ShippingServiceLevel != "standard" ||
            s.ShippingMethodId != o.ShippingMethodId || s.OrderId != o.Id || s.TenantId != o.TenantId)
            throw new ArgumentException("BOSTA_ORIGINAL_ORDER_BINDING_REQUIRED");
        if (CarrierRegistrationEvidence.HasOutboundEvidence(s) || s.CarrierRequestSnapshotJson != null ||
            s.CarrierGeographicMappingSnapshotJson != null || o.Status == OrderStatus.Cancelled ||
            s.Status is not (ShipmentStatus.Pending or ShipmentStatus.PendingExternalRegistration)) throw new ArgumentException("BOSTA_DESTINATION_LOCKED");
        if (s.ShippingCountry != "EG" || string.IsNullOrWhiteSpace(s.DestinationStreet ?? s.ShippingAddress) ||
            (s.DestinationStreet ?? s.ShippingAddress)!.Trim().Length <= 5) throw new ArgumentException("BOSTA_DESTINATION_ADDRESS_REQUIRED");
    }
}
