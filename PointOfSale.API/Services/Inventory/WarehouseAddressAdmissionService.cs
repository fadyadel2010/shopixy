using System.Data;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Geographic;

namespace PointOfSale.API.Services.Inventory;

public sealed class WarehouseAddressAdmissionService(ApplicationDbContext db, ITenantContext tenantContext,
    IAddressAdmissionService admission, IGeographicCatalogService catalog)
{
    public async Task<WarehouseAddressAdmissionResponse> AdmitAsync(Guid warehouseId, Guid actorId,
        WarehouseAddressAdmissionRequest request, CancellationToken ct = default)
    {
        var actor = await RequireActorAsync(actorId, ct);
        var tenant = tenantContext.RequiredTenantId;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        if (db.Database.IsNpgsql())
            await db.Warehouses.FromSqlInterpolated($"SELECT * FROM \"Warehouses\" WHERE \"Id\"={warehouseId} AND \"TenantId\"={tenant} AND NOT \"IsDeleted\" FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(ct);
        var warehouse = await RequireWarehouseAsync(warehouseId, ct);
        if (string.IsNullOrWhiteSpace(warehouse.Address) || string.IsNullOrWhiteSpace(request.StreetLine1) ||
            request.StreetLine1.Trim() != warehouse.Address.Trim()) throw new ArgumentException("WarehouseStreetConfirmationRequired");
        if (string.IsNullOrWhiteSpace(warehouse.Name) || string.IsNullOrWhiteSpace(warehouse.Phone))
            throw new ArgumentException("WarehouseContactRequired");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000 || request.StreetLine1.Length > 300)
            throw new ArgumentException("WarehouseAdmissionReasonAndStreetRequired");
        var lineage = new GeographicPlaceLineage(request.CountryPlaceId, request.AdministrativeAreaPlaceId,
            request.LocalityPlaceId, request.DistrictPlaceId, request.ZonePlaceId);
        var postal = request.PostalCode?.Trim();
        var inputs = new CanonicalAddressInputs("warehouse-origin", actorId.ToString("N"), "tenant-warehouse:sql-authorized-operator",
            null, warehouse.Address.Trim(), null, null, null, null, null, null, null, warehouse.Name.Trim(), warehouse.Phone.Trim(), null);
        var fingerprint = CanonicalAddressEvidence.ComputeInputFingerprint(tenant, AddressAdmissionOwnerDomain.Warehouse, warehouseId, lineage, inputs, postal);
        var result = await admission.AdmitAsync(new GeographicAddressCandidate(tenant, AddressAdmissionOwnerDomain.Warehouse,
            warehouseId, lineage, fingerprint, postal, null, null, null,
            inputs), ct);
        if (!result.Succeeded || !result.AddressAdmissionEvidenceId.HasValue || !result.CatalogReleaseId.HasValue)
            throw new ArgumentException(result.FailureCode ?? "WarehouseAddressAdmissionFailed");
        var now = DateTime.UtcNow;
        db.GeographicAudit.Add(new GeographicAudit
        {
            Id = Guid.NewGuid(), TenantId = tenant, ActorId = actorId, ActorKind = GeographicAuditActorKind.PosAdmin,
            ActorRole = actor.Role.ToString(), Action = "WarehouseAddressAdmitted", AggregateType = nameof(Warehouse),
            AggregateId = warehouseId, CatalogReleaseId = result.CatalogReleaseId, Reason = request.Reason.Trim(),
            CorrelationId = result.AddressAdmissionEvidenceId.Value.ToString("N"), ValidationChecksumSha256 = fingerprint,
            OccurredAtUtc = now, CreatedAtUtc = now
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(warehouseId, result.AddressAdmissionEvidenceId.Value, result.CatalogReleaseId.Value, lineage, postal, fingerprint);
    }

    public async Task<WarehouseAddressPlacesResponse> GetPlacesAsync(Guid warehouseId, Guid actorId,
        Guid? countryPlaceId, Guid? parentPlaceId, CancellationToken ct = default)
    {
        await RequireActorAsync(actorId, ct);
        await RequireWarehouseAsync(warehouseId, ct);
        var release = await catalog.GetActivePublishedCatalogReleaseAsync(null, ct)
            ?? throw new ArgumentException("PublishedCatalogReleaseRequired");
        var now = DateTime.UtcNow;
        var places = db.GeographicPlaces.AsNoTracking().Where(p => p.IntroducedCatalogReleaseId == release.Id &&
            p.Status == GeographicLifecycleStatus.Active && p.RetiredCatalogReleaseId == null &&
            p.ValidFromUtc <= now && (p.ValidToUtc == null || p.ValidToUtc > now));
        if (!countryPlaceId.HasValue)
        {
            if (parentPlaceId.HasValue) throw new ArgumentException("CountryPlaceIdRequired");
            places = places.Where(p => p.PlaceKind == GeographicPlaceKind.Country);
        }
        else
        {
            var country = await places.SingleOrDefaultAsync(p => p.Id == countryPlaceId && p.PlaceKind == GeographicPlaceKind.Country, ct);
            if (country == null) throw new ArgumentException("UnknownOrUnpublishedCountry");
            var parent = parentPlaceId ?? countryPlaceId.Value;
            if (!await places.AnyAsync(p => p.Id == parent && (p.Id == countryPlaceId || p.CountryPlaceId == countryPlaceId), ct))
                throw new ArgumentException("WarehouseParentPlaceInvalid");
            var childIds = db.GeographicPlaceHierarchyEdges.Where(e => e.ParentPlaceId == parent && e.CountryPlaceId == countryPlaceId &&
                e.IsPrimary && e.Depth == 1 && e.IntroducedCatalogReleaseId == release.Id && e.RetiredCatalogReleaseId == null).Select(e => e.ChildPlaceId);
            places = places.Where(p => p.CountryPlaceId == countryPlaceId && childIds.Contains(p.Id));
        }
        var selected = await places.OrderBy(p => p.CanonicalCode).ToListAsync(ct);
        var ids = selected.Select(p => p.Id).ToArray();
        var names = await db.GeographicPlaceNames.AsNoTracking().Where(n => ids.Contains(n.PlaceId) && n.IsPreferred &&
            n.IntroducedCatalogReleaseId == release.Id && n.RetiredCatalogReleaseId == null).OrderBy(n => n.DisplayPriority).ThenBy(n => n.LanguageTag).ToListAsync(ct);
        var profiles = await db.GeographicCountryProfiles.AsNoTracking().Where(p => p.IsActive &&
            p.IntroducedCatalogReleaseId == release.Id && p.RetiredCatalogReleaseId == null).ToListAsync(ct);
        return new(release.Id, selected.Select(p =>
        {
            var profile = profiles.SingleOrDefault(x => x.CountryPlaceId == (p.PlaceKind == GeographicPlaceKind.Country ? p.Id : p.CountryPlaceId));
            return new WarehouseAddressPlaceOption(p.Id, p.PlaceKind, p.CanonicalCode, names.FirstOrDefault(n => n.PlaceId == p.Id)?.Name ?? p.CanonicalCode,
                profile?.Iso2, profile?.PostalCodePolicy, profile?.PostalCodeRegex, profile?.RequiredLevelCodesForDelivery, profile?.RequiredLevelCodesForOperations);
        }).ToArray());
    }

    private async Task<User> RequireActorAsync(Guid actorId, CancellationToken ct)
    {
        var tenant = tenantContext.RequiredTenantId;
        if (tenant == Guid.Empty || actorId == Guid.Empty) throw new UnauthorizedAccessException();
        var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == actorId && u.TenantId == tenant && u.IsActive && !u.IsDeleted, ct);
        if (actor == null || !PosRolePermissionCatalog.IsGranted(actor.Role, PosPermissions.InventoryApprove)) throw new UnauthorizedAccessException();
        return actor;
    }

    private async Task<Warehouse> RequireWarehouseAsync(Guid id, CancellationToken ct) =>
        await db.Warehouses.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id && w.TenantId == tenantContext.RequiredTenantId &&
            w.IsActive && !w.IsDeleted, ct) ?? throw new KeyNotFoundException();
}
