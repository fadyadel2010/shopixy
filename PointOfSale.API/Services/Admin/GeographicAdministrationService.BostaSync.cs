using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.GeographicAdministration;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Services.Admin;

public sealed partial class GeographicAdministrationService
{
    public async Task<GeographicOperationResultDto> SynchronizeBostaReferenceAsync(Guid tenantId, Guid accountId, Guid? credentialReferenceId,
        BostaGeographyResult reference, CarrierGeographicEnvironment environment, bool registerProvider, string reason, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        if (!ValidActor(actorId, actorRole) || !ValidReason(reason)) return Fail("MappingActorAndReasonRequired");
        if (!reference.Succeeded || reference.ObservedAtUtc < DateTime.UtcNow.AddMinutes(-5) || reference.ObservedAtUtc > DateTime.UtcNow ||
            reference.Places.Count is < 3 or > 11001 || reference.Places.Any(p => !ImportText(p.Id, 200) || !ImportText(p.Name, 200) ||
                JsonSerializer.Serialize(p).Length > 512 || p.Type is not ("country" or "city" or "district")) ||
            reference.Places.Select(p => p.Id).Distinct().Count() != reference.Places.Count)
            return Fail("BOSTA_GEOGRAPHY_RESPONSE_INVALID");
        var countries = reference.Places.Where(p => p.Type == "country").ToArray();
        if (countries.Length != 1 || countries[0].Code != "EG" || countries[0].ParentId != null ||
            !reference.Places.Any(p => p.Type == "district") || reference.Places.Any(p => p.Type != "country" &&
                !reference.Places.Any(parent => parent.Id == p.ParentId && parent.Type == (p.Type == "city" ? "country" : "city"))))
            return Fail("BOSTA_REFERENCE_PARENT_INVALID");
        await using var tx = await BeginCatalogTransactionAsync(cancellationToken);
        // Recheck persisted identity after the network call, under the same transaction as staging.
        var account = await _dbContext.CarrierAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId &&
            a.TenantId == tenantId && a.CarrierCode == "bosta" && !a.IsDeleted, cancellationToken);
        if (account == null || !credentialReferenceId.HasValue || account.ActiveCredentialReferenceId != credentialReferenceId ||
            environment is not (CarrierGeographicEnvironment.Production or CarrierGeographicEnvironment.Sandbox) || account.Environment?.ToString() != environment.ToString())
            return Fail("BOSTA_SAVED_CONFIGURATION_CHANGED");
        var providers = await _dbContext.CarrierProviders.Where(p => p.CarrierCode.ToLower() == "bosta").ToArrayAsync(cancellationToken);
        if (providers.Length > 1 || providers.Any(p => p.CarrierCode != "bosta")) return Fail("BOSTA_GEOGRAPHY_PROVIDER_AMBIGUOUS");
        var provider = providers.SingleOrDefault(); var now = DateTime.UtcNow;
        if (provider == null)
        {
            if (!registerProvider) return Fail("UnknownCarrierProvider");
            // IsActive is geographic metadata eligibility, NOT CarrierAccount activation/booking.
            provider = new() { Id = Guid.NewGuid(), CarrierCode = "bosta", DisplayName = "Bosta", SupportsGeographicMappings = true,
                RequiresMappingForDelivery = true, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
            _dbContext.CarrierProviders.Add(provider);
        }
        else if (!provider.IsActive) return Fail("BOSTA_GEOGRAPHY_PROVIDER_DISABLED_REVIEW_REQUIRED");
        var hash = Checksum(JsonSerializer.Serialize(reference.Places.OrderBy(p => p.Type).ThenBy(p => p.Id)));
        var run = new CarrierGeographicSyncRun { Id = Guid.NewGuid(), CarrierProviderId = provider.Id, TenantId = tenantId,
            AccountScopeKind = CarrierGeographicAccountScopeKind.TenantProviderAccount, AccountScopeId = tenantId,
            Environment = environment,
            SourceKind = CarrierGeographicSyncSourceKind.ProviderApi,
            SourceUri = (environment == CarrierGeographicEnvironment.Sandbox ? "https://staging-api.bosta.co" : "https://app.bosta.co/api/v2") + "/cities",
            SourceVersion = $"bosta-v1:{account.Id:N}:{credentialReferenceId:N}", SourceHashSha256 = hash,
            StartedAtUtc = reference.ObservedAtUtc, CompletedAtUtc = reference.ObservedAtUtc, Status = CarrierGeographicSyncStatus.Completed,
            AddedCount = reference.Places.Count, CreatedAtUtc = now };
        _dbContext.CarrierGeographicSyncRuns.Add(run);
        foreach (var p in reference.Places)
        {
            var candidate = new CarrierGeographicSyncCandidate { Id = Guid.NewGuid(), SyncRunId = run.Id, CarrierProviderId = provider.Id,
                ProviderPlaceType = p.Type, ProviderPlaceId = p.Id, ProviderPlaceCode = p.Code, ProviderDisplayName = p.Name,
                ProviderNormalizedName = p.Name.ToUpperInvariant(), ProviderParentPlaceId = p.ParentId,
                CandidateStatus = p.DropOffAvailable ? CarrierGeographicSyncCandidateStatus.PendingReview : CarrierGeographicSyncCandidateStatus.Rejected,
                ReviewReason = p.DropOffAvailable ? null : "BOSTA_DESTINATION_UNSERVICEABLE", CreatedAtUtc = now };
            _dbContext.CarrierGeographicSyncCandidates.Add(candidate);
            _dbContext.GeographicAudit.Add(new() { Id = Guid.NewGuid(), TenantId = tenantId, ActorId = actorId,
                ActorKind = GeographicAuditActorKind.PosAdmin, ActorRole = actorRole, Action = "BostaReferenceDiscoveredV1",
                AggregateType = nameof(CarrierGeographicSyncCandidate), AggregateId = candidate.Id,
                Reason = JsonSerializer.Serialize(p), ValidationChecksumSha256 = hash, CorrelationId = run.Id.ToString("N"),
                EvidenceObjectKey = p.Type == "district" ? run.SourceUri + "/" + Uri.EscapeDataString(p.ParentId!) + "/districts" :
                    p.Type == "country" ? run.SourceUri.Replace("/cities", "/pickup-locations", StringComparison.Ordinal) : run.SourceUri,
                OccurredAtUtc = now, CreatedAtUtc = now });
        }
        if (registerProvider) _dbContext.GeographicAudit.Add(new() { Id = Guid.NewGuid(), TenantId = tenantId, ActorId = actorId,
            ActorKind = GeographicAuditActorKind.PosAdmin, ActorRole = actorRole, Action = "BostaGeographyBootstrapReviewedV1",
            AggregateType = nameof(CarrierProvider), AggregateId = provider.Id, Reason = reason,
            ValidationChecksumSha256 = hash, CorrelationId = run.Id.ToString("N"), OccurredAtUtc = now, CreatedAtUtc = now });
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);
        return Success(run.Id);
    }
}
