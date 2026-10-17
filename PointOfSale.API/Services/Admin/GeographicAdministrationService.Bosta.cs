using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.GeographicAdministration;
using PointOfSale.Core.Entities.Geographic;

namespace PointOfSale.API.Services.Admin;

public sealed partial class GeographicAdministrationService
{
    public async Task<GeographicOperationResultDto> AcceptBostaMappingAsync(Guid tenantId, Guid accountId, Guid? credentialReferenceId,
        BostaMappingDraftRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        await using var tx = await BeginCatalogTransactionAsync(cancellationToken);
        if (!ValidActor(actorId, actorRole) || !ValidReason(request.Reason) || request.Choices.Count is < 3 or > 10000 ||
            request.Choices.Select(c => c.CandidateId).Distinct().Count() != request.Choices.Count ||
            request.Choices.Select(c => c.CanonicalPlaceId).Distinct().Count() != request.Choices.Count) return Fail("MappingReviewRequired");
        var run = await _dbContext.CarrierGeographicSyncRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == request.SyncRunId && r.TenantId == tenantId, cancellationToken);
        if (run == null || run.Status != CarrierGeographicSyncStatus.Completed || run.SourceHashSha256 != request.ExpectedSourceHashSha256 ||
            run.SourceVersion != $"bosta-v1:{accountId:N}:{credentialReferenceId:N}" || run.CompletedAtUtc == null || run.CompletedAtUtc < DateTime.UtcNow.AddDays(-1) ||
            run.AccountScopeKind != CarrierGeographicAccountScopeKind.TenantProviderAccount || run.AccountScopeId != tenantId ||
            run.SourceKind != CarrierGeographicSyncSourceKind.ProviderApi) return Fail("BOSTA_SYNC_STALE_OR_FOREIGN");
        var account = await _dbContext.CarrierAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId && a.TenantId == tenantId && a.CarrierCode == "bosta" && !a.IsDeleted, cancellationToken);
        if (account?.ActiveCredentialReferenceId != credentialReferenceId || account?.Environment?.ToString() != run.Environment.ToString()) return Fail("BOSTA_SAVED_CONFIGURATION_CHANGED");
        var now = DateTime.UtcNow;
        if (!await _dbContext.GeographicCatalogReleases.AnyAsync(r => r.Id == request.CatalogReleaseId && r.Status == GeographicReleaseStatus.Published && r.CountryScopeIso2 == "EG" &&
            _dbContext.GeographicCatalogPublications.Any(p => p.CatalogReleaseId == r.Id && p.ActivatedAtUtc <= now && p.SupersededAtUtc == null), cancellationToken)) return Fail("PublishedCatalogReleaseRequired");
        var ids = request.Choices.Select(c => c.CandidateId).ToArray();
        var candidates = await _dbContext.CarrierGeographicSyncCandidates.Where(c => c.SyncRunId == run.Id && ids.Contains(c.Id)).ToListAsync(cancellationToken);
        if (candidates.Count != ids.Length || candidates.Any(c => c.CandidateStatus != CarrierGeographicSyncCandidateStatus.PendingReview || c.CarrierProviderId != run.CarrierProviderId)) return Fail("BOSTA_REFERENCE_REVIEW_INVALID");
        var canonicalIds = request.Choices.Select(c => c.CanonicalPlaceId).ToArray();
        var places = await _dbContext.GeographicPlaces.AsNoTracking().Where(p => canonicalIds.Contains(p.Id) && p.IntroducedCatalogReleaseId == request.CatalogReleaseId && p.Status == GeographicLifecycleStatus.Active && p.RetiredCatalogReleaseId == null).ToDictionaryAsync(p => p.Id, cancellationToken);
        if (places.Count != canonicalIds.Distinct().Count()) return Fail("UnknownOrUnpublishedPlace");
        if (!await _dbContext.CarrierProviders.AnyAsync(p => p.Id == run.CarrierProviderId && p.CarrierCode == "bosta" && p.IsActive, cancellationToken)) return Fail("UnknownCarrierProvider");
        if (candidates.Select(c => c.ProviderPlaceId).Distinct().Count() != candidates.Count) return Fail("AmbiguousProviderMapping");
        var edges = await _dbContext.GeographicPlaceHierarchyEdges.AsNoTracking().Where(e => e.IntroducedCatalogReleaseId == request.CatalogReleaseId &&
            e.IsPrimary && e.Depth == 1 && e.RetiredCatalogReleaseId == null).ToArrayAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            var canonical = places[request.Choices.Single(c => c.CandidateId == candidate.Id).CanonicalPlaceId];
            var parentType = candidate.ProviderPlaceType == "district" ? "city" : candidate.ProviderPlaceType == "city" ? "country" : null;
            var parent = candidates.SingleOrDefault(c => c.ProviderPlaceId == candidate.ProviderParentPlaceId);
            if (candidate.ProviderPlaceType is not ("country" or "city" or "district") ||
                (candidate.ProviderPlaceType == "country" ? canonical.PlaceKind != GeographicPlaceKind.Country || canonical.CanonicalCode != "EG" || candidate.ProviderParentPlaceId != null :
                parent == null || parent.ProviderPlaceType != parentType)) return Fail("MappingParentInvalid");
            if (candidate.ProviderPlaceType == "city" && canonical.PlaceKind is not (GeographicPlaceKind.AdministrativeArea or GeographicPlaceKind.Locality) ||
                candidate.ProviderPlaceType == "district" && canonical.PlaceKind is not (GeographicPlaceKind.Locality or GeographicPlaceKind.District)) return Fail("MappingPlaceKindInvalid");
            var evidence = await _dbContext.GeographicAudit.AsNoTracking().SingleOrDefaultAsync(a => a.Action == "BostaReferenceDiscoveredV1" &&
                a.AggregateId == candidate.Id && a.TenantId == tenantId && a.ValidationChecksumSha256 == run.SourceHashSha256, cancellationToken);
            PointOfSale.Infrastructure.Shipping.BostaGeographicReference? reference = null;
            try { if (evidence != null) reference = JsonSerializer.Deserialize<PointOfSale.Infrastructure.Shipping.BostaGeographicReference>(evidence.Reason); }
            catch (JsonException) { return Fail("BOSTA_PROVIDER_REFERENCE_REQUIRED"); }
            if (reference == null || !reference.DropOffAvailable || reference.Type != candidate.ProviderPlaceType || reference.Id != candidate.ProviderPlaceId ||
                reference.ParentId != candidate.ProviderParentPlaceId) return Fail("BOSTA_DESTINATION_UNSERVICEABLE");
            if (parent == null) continue;
            var parentPlace = places[request.Choices.Single(c => c.CandidateId == parent.Id).CanonicalPlaceId];
            if (canonical.CountryPlaceId != (parentPlace.CountryPlaceId ?? parentPlace.Id)) return Fail("MappingCanonicalHierarchyInvalid");
            var current = canonical.Id; var seen = new HashSet<Guid>();
            while (current != parentPlace.Id)
            {
                if (!seen.Add(current)) return Fail("MappingCanonicalHierarchyInvalid");
                var parents = edges.Where(e => e.ChildPlaceId == current && e.CountryPlaceId == canonical.CountryPlaceId).ToArray();
                if (parents.Length != 1) return Fail("MappingCanonicalHierarchyInvalid");
                current = parents[0].ParentPlaceId;
            }
        }
        var release = new CarrierGeographicMappingRelease {Id = Guid.NewGuid(), CarrierProviderId = run.CarrierProviderId,
            TenantId = tenantId, AccountScopeKind = run.AccountScopeKind, AccountScopeId = run.AccountScopeId, Environment = run.Environment,
            ReleaseNumber = DateTime.UtcNow.Ticks, ReleaseKey = "bosta-" + Guid.NewGuid().ToString("N"), CatalogReleaseId = request.CatalogReleaseId,
            SourceSummary = "Authenticated Bosta reference sync " + run.Id, ChecksumSha256 = run.SourceHashSha256!, CreatedByUserId = actorId,
            EffectiveFromUtc = DateTime.UtcNow, FreshnessExpiresAtUtc = run.CompletedAtUtc!.Value.AddDays(1), CreatedAtUtc = DateTime.UtcNow};
        var mappingIds = candidates.ToDictionary(c => c.ProviderPlaceId, _ => Guid.NewGuid());
        var mappings = new List<CarrierGeographicMapping>();
        foreach (var c in candidates)
        {
            var choice = request.Choices.Single(x => x.CandidateId == c.Id); var place = places[choice.CanonicalPlaceId];
            if (c.ProviderParentPlaceId != null && !mappingIds.ContainsKey(c.ProviderParentPlaceId)) return Fail("MappingParentInvalid");
            c.CandidateCanonicalPlaceId = place.Id; c.CandidateStatus = CarrierGeographicSyncCandidateStatus.Approved;
            c.ReviewedAtUtc = DateTime.UtcNow; c.ReviewedByUserId = actorId; c.ReviewReason = request.Reason;
            mappings.Add(new CarrierGeographicMapping {Id = mappingIds[c.ProviderPlaceId], MappingReleaseId = release.Id, CarrierProviderId = run.CarrierProviderId,
                CanonicalPlaceId = place.Id, CountryPlaceId = place.CountryPlaceId ?? place.Id, CanonicalPlaceKind = place.PlaceKind,
                ProviderPlaceType = c.ProviderPlaceType, ProviderPlaceId = c.ProviderPlaceId, ProviderPlaceCode = c.ProviderPlaceCode,
                ProviderDisplayName = c.ProviderDisplayName, ProviderNormalizedName = c.ProviderNormalizedName,
                ProviderParentMappingId = c.ProviderParentPlaceId == null ? null : mappingIds[c.ProviderParentPlaceId], RequiredForOperation = "ShipmentRegistration",
                MappingStatus = CarrierGeographicMappingStatus.Active, VerificationStatus = CarrierGeographicVerificationStatus.Reviewed,
                Source = "bosta-sync:" + run.Id, EffectiveFromUtc = release.EffectiveFromUtc, ReviewedByUserId = actorId,
                ReviewedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow});
        }
        _dbContext.CarrierGeographicMappingReleases.Add(release);
        _dbContext.CarrierGeographicMappings.AddRange(mappings);
        await AddAuditAsync("BostaMappingDraftAccepted", nameof(CarrierGeographicMappingRelease), release.Id, actorId, actorRole, request.Reason, mappingReleaseId:release.Id, cancellationToken:cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);
        return Success(release.Id);
    }
}
