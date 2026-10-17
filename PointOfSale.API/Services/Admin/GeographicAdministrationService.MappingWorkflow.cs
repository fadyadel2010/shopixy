using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.GeographicAdministration;
using PointOfSale.Core.Entities.Geographic;

namespace PointOfSale.API.Services.Admin;

public sealed partial class GeographicAdministrationService
{
    public Task<bool> SyncBelongsToTenantAsync(Guid id, Guid tenantId, bool candidate, CancellationToken cancellationToken = default) =>
        _dbContext.CarrierGeographicSyncRuns.AsNoTracking().AnyAsync(r => r.TenantId == tenantId && r.AccountScopeId == tenantId &&
            r.AccountScopeKind == CarrierGeographicAccountScopeKind.TenantProviderAccount &&
            (candidate ? _dbContext.CarrierGeographicSyncCandidates.Any(c => c.Id == id && c.SyncRunId == r.Id) : r.Id == id), cancellationToken);

    public Task<bool> OverrideBelongsToTenantAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) =>
        _dbContext.CarrierGeographicOverrides.AsNoTracking().AnyAsync(r => r.Id == id && r.TenantId == tenantId && r.AccountScopeId == tenantId &&
            r.AccountScopeKind == CarrierGeographicAccountScopeKind.TenantProviderAccount, cancellationToken);

    public async Task<IReadOnlyList<GeographicAuditDto>> GetTenantAuditAsync(Guid tenantId, int limit, CancellationToken cancellationToken = default) =>
        await _dbContext.GeographicAudit.AsNoTracking().Where(a => a.TenantId == tenantId || a.TenantId == null && a.CatalogReleaseId != null && a.MappingReleaseId == null)
            .OrderByDescending(a => a.OccurredAtUtc).Take(Math.Clamp(limit, 1, 200))
            .Select(a => new GeographicAuditDto(a.Id, a.TenantId, a.ActorKind, a.ActorId, a.ActorRole, a.Action, a.AggregateType, a.AggregateId,
                a.CatalogReleaseId, a.MappingReleaseId, a.Reason, a.CorrelationId, a.OccurredAtUtc)).ToListAsync(cancellationToken);

    public Task<bool> MappingBelongsToTenantAsync(Guid mappingReleaseId, Guid tenantId, CancellationToken cancellationToken = default) =>
        _dbContext.CarrierGeographicMappingReleases.AsNoTracking().AnyAsync(r => r.Id == mappingReleaseId && r.TenantId == tenantId &&
            r.AccountScopeKind == CarrierGeographicAccountScopeKind.TenantProviderAccount && r.AccountScopeId == tenantId, cancellationToken);

    public async Task<MappingValidationPreviewDto> InspectMappingDraftAsync(Guid mappingReleaseId, CancellationToken cancellationToken = default)
    {
        MappingValidationPreviewDto FailPreview(string code) => new(mappingReleaseId, false, code, null);
        var release = await _dbContext.CarrierGeographicMappingReleases.AsNoTracking().SingleOrDefaultAsync(r => r.Id == mappingReleaseId, cancellationToken);
        if (release == null) return FailPreview("MappingReleaseNotFound");
        var now = DateTime.UtcNow;
        if (!await _dbContext.GeographicCatalogReleases.AnyAsync(r => r.Id == release.CatalogReleaseId && r.Status == GeographicReleaseStatus.Published &&
            _dbContext.GeographicCatalogPublications.Any(p => p.CatalogReleaseId == r.Id && p.ActivatedAtUtc <= now && p.SupersededAtUtc == null), cancellationToken))
            return FailPreview("PublishedCatalogReleaseRequired");
        if (release.EffectiveFromUtc > now || release.EffectiveToUtc <= now || release.FreshnessExpiresAtUtc <= now)
            return FailPreview("StaleMappingRelease");
        if (release.TenantId == null || release.AccountScopeKind != CarrierGeographicAccountScopeKind.TenantProviderAccount || release.AccountScopeId != release.TenantId)
            return FailPreview("MappingTenantAccountScopeRequired");
        var provider = await _dbContext.CarrierProviders.AsNoTracking().SingleOrDefaultAsync(p => p.Id == release.CarrierProviderId && p.IsActive, cancellationToken);
        if (provider == null) return FailPreview("UnknownCarrierProvider");
        var rows = await _dbContext.CarrierGeographicMappings.AsNoTracking().Where(m => m.MappingReleaseId == release.Id).OrderBy(m => m.Id).ToListAsync(cancellationToken);
        if (rows.Count == 0) return FailPreview("MissingRequiredProviderMapping");
        if (rows.GroupBy(m => (m.CanonicalPlaceId, m.ProviderPlaceType)).Any(g => g.Count() > 1) ||
            rows.GroupBy(m => (m.ProviderPlaceType, m.ProviderPlaceId)).Any(g => g.Count() > 1)) return FailPreview("AmbiguousProviderMapping");
        var ids = rows.SelectMany(m => new[] {m.CanonicalPlaceId, m.CountryPlaceId}).Distinct().ToArray();
        var places = await _dbContext.GeographicPlaces.AsNoTracking().Where(p => ids.Contains(p.Id) && p.IntroducedCatalogReleaseId == release.CatalogReleaseId &&
            p.Status == GeographicLifecycleStatus.Active && p.RetiredCatalogReleaseId == null && p.ValidFromUtc <= now && (p.ValidToUtc == null || p.ValidToUtc > now))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        foreach (var m in rows)
        {
            if (m.CarrierProviderId != release.CarrierProviderId || !places.TryGetValue(m.CanonicalPlaceId, out var canonical) ||
                !places.TryGetValue(m.CountryPlaceId, out var country) || country.PlaceKind != GeographicPlaceKind.Country ||
                canonical.PlaceKind != m.CanonicalPlaceKind || (canonical.Id != country.Id && canonical.CountryPlaceId != country.Id) ||
                m.MappingStatus != CarrierGeographicMappingStatus.Active || m.VerificationStatus is not (CarrierGeographicVerificationStatus.Reviewed or CarrierGeographicVerificationStatus.ManuallyApproved) ||
                m.RequiredForOperation != "ShipmentRegistration" || string.IsNullOrWhiteSpace(m.ProviderPlaceId) || m.ProviderPlaceId.Length > 200 ||
                m.ProviderPlaceId != m.ProviderPlaceId.Trim() || m.ProviderPlaceId.Any(char.IsControl) || m.EffectiveFromUtc > now || m.EffectiveToUtc <= now)
                return FailPreview("MappingIdentityOrReviewInvalid");
            if (m.ProviderParentMappingId.HasValue && !rows.Any(p => p.Id == m.ProviderParentMappingId && p.CountryPlaceId == m.CountryPlaceId))
                return FailPreview("MappingParentInvalid");
            if (provider.CarrierCode == "bosta")
            {
                if (!m.Source.StartsWith("bosta-sync:", StringComparison.Ordinal) || !Guid.TryParse(m.Source[11..], out var runId)) return FailPreview("BOSTA_PROVIDER_REFERENCE_REQUIRED");
                var run = await _dbContext.CarrierGeographicSyncRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId && r.TenantId == release.TenantId &&
                    r.Environment == release.Environment && r.CarrierProviderId == release.CarrierProviderId && r.Status == CarrierGeographicSyncStatus.Completed, cancellationToken);
                if (run?.CompletedAtUtc == null || run.CompletedAtUtc < now.AddDays(-1)) return FailPreview("BOSTA_SYNC_STALE_OR_FOREIGN");
                var accountEnvironment = release.Environment == CarrierGeographicEnvironment.Sandbox ?
                    PointOfSale.Core.Entities.ShippingAdministration.CarrierAccountEnvironment.Sandbox :
                    PointOfSale.Core.Entities.ShippingAdministration.CarrierAccountEnvironment.Production;
                var currentAccount = await _dbContext.CarrierAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a =>
                    a.TenantId == release.TenantId && a.CarrierCode == "bosta" && !a.IsDeleted && a.Environment == accountEnvironment, cancellationToken);
                if (currentAccount == null || run.SourceVersion != $"bosta-v1:{currentAccount.Id:N}:{currentAccount.ActiveCredentialReferenceId:N}")
                    return FailPreview("BOSTA_SAVED_CONFIGURATION_CHANGED");
                var candidate = await _dbContext.CarrierGeographicSyncCandidates.AsNoTracking().SingleOrDefaultAsync(c => c.SyncRunId == runId &&
                    c.ProviderPlaceType == m.ProviderPlaceType && c.ProviderPlaceId == m.ProviderPlaceId && c.CandidateCanonicalPlaceId == m.CanonicalPlaceId &&
                    c.CandidateStatus == CarrierGeographicSyncCandidateStatus.Approved, cancellationToken);
                if (candidate == null) return FailPreview("BOSTA_PROVIDER_REFERENCE_REQUIRED");
                var referenceEvidence = await _dbContext.GeographicAudit.AsNoTracking().SingleOrDefaultAsync(a => a.Action == "BostaReferenceDiscoveredV1" &&
                    a.AggregateId == candidate.Id && a.TenantId == release.TenantId && a.ValidationChecksumSha256 == run.SourceHashSha256, cancellationToken);
                PointOfSale.Infrastructure.Shipping.BostaGeographicReference? reference = null;
                try { if (referenceEvidence != null) reference = JsonSerializer.Deserialize<PointOfSale.Infrastructure.Shipping.BostaGeographicReference>(referenceEvidence.Reason); }
                catch (JsonException) { return FailPreview("BOSTA_PROVIDER_REFERENCE_REQUIRED"); }
                if (reference?.DropOffAvailable != true || reference.Type != m.ProviderPlaceType || reference.Id != m.ProviderPlaceId ||
                    reference.ParentId != candidate.ProviderParentPlaceId) return FailPreview("BOSTA_DESTINATION_UNSERVICEABLE");
                if (reference.ParentId != null && !rows.Any(parent => parent.Id == m.ProviderParentMappingId && parent.ProviderPlaceId == reference.ParentId))
                    return FailPreview("MappingParentInvalid");
                if (country.CanonicalCode != "EG" || m.ProviderPlaceType is not ("country" or "city" or "district")) return FailPreview("BOSTA_DOMESTIC_EGYPT_ROUTE_REQUIRED");
                var parentType = m.ProviderPlaceType == "district" ? "city" : m.ProviderPlaceType == "city" ? "country" : null;
                if (parentType == null ? m.CanonicalPlaceKind != GeographicPlaceKind.Country || m.ProviderParentMappingId != null :
                    !rows.Any(p => p.Id == m.ProviderParentMappingId && p.ProviderPlaceType == parentType)) return FailPreview("MappingParentInvalid");
                if (m.ProviderPlaceType == "city" && m.CanonicalPlaceKind is not (GeographicPlaceKind.AdministrativeArea or GeographicPlaceKind.Locality) ||
                    m.ProviderPlaceType == "district" && m.CanonicalPlaceKind is not (GeographicPlaceKind.Locality or GeographicPlaceKind.District)) return FailPreview("MappingPlaceKindInvalid");
            }
        }
        var edges = await _dbContext.GeographicPlaceHierarchyEdges.AsNoTracking().Where(e => e.IntroducedCatalogReleaseId == release.CatalogReleaseId && e.RetiredCatalogReleaseId == null && e.IsPrimary && e.Depth == 1).ToListAsync(cancellationToken);
        foreach (var m in rows.Where(m => m.ProviderParentMappingId.HasValue))
        {
            var parent = rows.Single(p => p.Id == m.ProviderParentMappingId).CanonicalPlaceId;
            var seen = new HashSet<Guid>(); var current = m.CanonicalPlaceId;
            while (current != parent)
            {
                if (!seen.Add(current)) return FailPreview("MappingCanonicalHierarchyInvalid");
                var parents = edges.Where(e => e.ChildPlaceId == current && e.CountryPlaceId == m.CountryPlaceId).ToArray();
                if (parents.Length != 1) return FailPreview("MappingCanonicalHierarchyInvalid");
                current = parents[0].ParentPlaceId;
            }
        }
        var content = JsonSerializer.Serialize(new { release.Id, release.ReleaseKey, release.ReleaseNumber, release.CarrierProviderId, release.CatalogReleaseId,
            release.TenantId, release.Environment, release.AccountScopeKind, release.AccountScopeId, release.EffectiveFromUtc, release.EffectiveToUtc,
            release.FreshnessExpiresAtUtc, rows = rows.Select(m => new { m.Id, m.CanonicalPlaceId, m.CountryPlaceId, m.CanonicalPlaceKind, m.ProviderPlaceType,
                m.ProviderPlaceId, m.ProviderPlaceCode, m.ProviderDisplayName, m.ProviderParentMappingId, m.RequiredForOperation, m.MappingStatus,
                m.VerificationStatus, m.Source, m.EffectiveFromUtc, m.EffectiveToUtc, m.ReviewedByUserId }) });
        return new(mappingReleaseId, true, null, Checksum(content));
    }

    public async Task<GeographicOperationResultDto> ValidateMappingDraftAsync(Guid mappingReleaseId, MappingValidationRequest request,
        Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        await using var tx = await BeginCatalogTransactionAsync(cancellationToken);
        var release = await RequireDraftMappingReleaseAsync(mappingReleaseId, cancellationToken);
        if (release == null) return Fail("MappingDraftRequired");
        if (!ValidActor(actorId, actorRole) || !ValidReason(request.Reason)) return Fail("MappingActorAndReasonRequired");
        if (release.CreatedByUserId == actorId || await _dbContext.CarrierGeographicMappings.AnyAsync(m => m.MappingReleaseId == release.Id && m.ReviewedByUserId == actorId, cancellationToken)) return Fail("FourEyesApprovalRequired");
        var preview = await InspectMappingDraftAsync(mappingReleaseId, cancellationToken);
        if (!preview.Succeeded) return Fail(preview.FailureCode!);
        if (preview.ContentChecksumSha256 != request.ExpectedContentChecksumSha256) return Fail("MappingContentChanged");
        release.ChecksumSha256 = preview.ContentChecksumSha256!;
        release.Status = GeographicReleaseStatus.Validated;
        await AddAuditAsync("MappingReleaseValidatedV1", "CarrierGeographicMappingRelease", release.Id, actorId, actorRole, request.Reason, mappingReleaseId: release.Id, cancellationToken: cancellationToken);
        _dbContext.ChangeTracker.Entries<GeographicAudit>().Single(e => e.State == EntityState.Added && e.Entity.Action == "MappingReleaseValidatedV1").Entity.ValidationChecksumSha256 = release.ChecksumSha256;
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);
        return Success(release.Id);
    }
}
