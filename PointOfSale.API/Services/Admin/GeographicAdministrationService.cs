using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.GeographicAdministration;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Admin;

public interface IGeographicAdministrationService
{
    Task<CatalogImportPreviewDto> PreviewCatalogImportAsync(CatalogImportManifest manifest, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogReleaseSummaryDto>> ListCatalogReleasesAsync(CancellationToken cancellationToken = default);
    Task<CatalogReviewDto?> ReadCatalogReleaseAsync(Guid releaseId, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> StageCatalogImportAsync(StageCatalogImportRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> SynchronizeBostaReferenceAsync(Guid tenantId, Guid accountId, Guid? credentialReferenceId,
        PointOfSale.Infrastructure.Shipping.BostaGeographyResult reference, CarrierGeographicEnvironment environment, bool registerProvider, string reason, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> CreateCatalogDraftAsync(CreateCatalogDraftRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> EditCatalogDraftAsync(Guid catalogReleaseId, EditCatalogDraftRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> AddCatalogPlaceAsync(CatalogPlaceRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> AddLocalizationAsync(LocalizationRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> AddAliasAsync(AliasRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> AddCountryProfileAsync(CountryProfileRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> AddHierarchyEdgeAsync(CatalogHierarchyEdgeRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<CatalogValidationPreviewDto> InspectCatalogDraftAsync(Guid catalogReleaseId, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> ValidateCatalogDraftAsync(Guid catalogReleaseId, CatalogValidationRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> AddMergeAsync(PlaceMergeRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> AddDeprecationAsync(PlaceDeprecationRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> PublishCatalogReleaseAsync(Guid catalogReleaseId, ReleaseActionRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> RollbackCatalogReleaseAsync(Guid targetCatalogReleaseId, ReleaseActionRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> CreateMappingDraftAsync(CreateMappingDraftRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> AddMappingAsync(MappingRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<MappingValidationPreviewDto> InspectMappingDraftAsync(Guid mappingReleaseId, CancellationToken cancellationToken = default);
    Task<bool> MappingBelongsToTenantAsync(Guid mappingReleaseId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> AcceptBostaMappingAsync(Guid tenantId, Guid accountId, Guid? credentialReferenceId, PointOfSale.API.Controllers.BostaMappingDraftRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> ValidateMappingDraftAsync(Guid mappingReleaseId, MappingValidationRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> PublishMappingReleaseAsync(Guid mappingReleaseId, ReleaseActionRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> RollbackMappingReleaseAsync(Guid targetMappingReleaseId, ReleaseActionRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> RecordSyncRunAsync(SyncRunRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> AddSyncCandidateAsync(SyncCandidateRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> ReviewSyncCandidateAsync(Guid syncCandidateId, CarrierGeographicSyncCandidateStatus status, CandidateReviewRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> CreateOverrideAsync(OverrideRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<GeographicOperationResultDto> ApproveOverrideAsync(Guid overrideId, ReleaseActionRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GeographicAuditDto>> GetAuditAsync(int limit, CancellationToken cancellationToken = default);
    Task<bool> SyncBelongsToTenantAsync(Guid id, Guid tenantId, bool candidate, CancellationToken cancellationToken = default);
    Task<bool> OverrideBelongsToTenantAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GeographicAuditDto>> GetTenantAuditAsync(Guid tenantId, int limit, CancellationToken cancellationToken = default);
}

public sealed partial class GeographicAdministrationService : IGeographicAdministrationService
{
    private readonly ApplicationDbContext _dbContext;

    public GeographicAdministrationService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<GeographicOperationResultDto> CreateCatalogDraftAsync(CreateCatalogDraftRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginCatalogTransactionAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.ReleaseKey) || string.IsNullOrWhiteSpace(request.SourceSummary))
        {
            return Fail("CatalogDraftRequiresReleaseKeyAndSource");
        }

        var now = DateTime.UtcNow;
        var release = new GeographicCatalogRelease
        {
            Id = Guid.NewGuid(),
            ReleaseNumber = request.ReleaseNumber,
            ReleaseKey = request.ReleaseKey.Trim(),
            Status = GeographicReleaseStatus.Draft,
            CountryScopeIso2 = NormalizeIso2(request.CountryScopeIso2),
            SourceSummary = request.SourceSummary.Trim(),
            ChecksumSha256 = Checksum(request.ReleaseKey, request.SourceSummary, request.CountryScopeIso2),
            CreatedAtUtc = now,
            CreatedByUserId = actorId
        };

        await _dbContext.GeographicCatalogReleases.AddAsync(release, cancellationToken);
        await AddAuditAsync("CatalogDraftCreated", "GeographicCatalogRelease", release.Id, actorId, actorRole, request.Reason, catalogReleaseId: release.Id, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return Success(release.Id);
    }

    public async Task<GeographicOperationResultDto> EditCatalogDraftAsync(Guid catalogReleaseId, EditCatalogDraftRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginCatalogTransactionAsync(cancellationToken);
        var release = await LoadCatalogReleaseAsync(catalogReleaseId, cancellationToken);
        if (release is null) return Fail("CatalogReleaseNotFound");
        if (release.Status != GeographicReleaseStatus.Draft) return Fail("CatalogDraftIsolationRequired");

        release.SourceSummary = request.SourceSummary.Trim();
        release.ChecksumSha256 = Checksum(release.ReleaseKey, release.SourceSummary, release.CountryScopeIso2);
        release.Status = GeographicReleaseStatus.Draft;
        await AddAuditAsync("CatalogDraftEdited", "GeographicCatalogRelease", release.Id, actorId, actorRole, request.Reason, catalogReleaseId: release.Id, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return Success(release.Id);
    }

    public async Task<GeographicOperationResultDto> AddCatalogPlaceAsync(CatalogPlaceRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginCatalogTransactionAsync(cancellationToken);
        var release = await RequireDraftCatalogReleaseAsync(request.CatalogReleaseId, cancellationToken);
        if (release is null) return Fail("CatalogDraftRequired");

        var now = DateTime.UtcNow;
        var place = new GeographicPlace
        {
            Id = request.PlaceId.GetValueOrDefault(Guid.NewGuid()),
            StableKey = request.StableKey.Trim(),
            PlaceKind = request.PlaceKind,
            CountryPlaceId = request.CountryPlaceId,
            LevelCode = request.LevelCode,
            CanonicalCode = request.CanonicalCode.Trim(),
            ExternalStandard = request.ExternalStandard,
            ExternalStandardCode = request.ExternalStandardCode,
            Status = request.Status,
            SuccessorPlaceId = request.SuccessorPlaceId,
            ValidFromUtc = request.ValidFromUtc,
            ValidToUtc = request.ValidToUtc,
            IntroducedCatalogReleaseId = request.CatalogReleaseId,
            Source = request.Source.Trim(),
            CreatedByUserId = actorId,
            UpdatedByUserId = actorId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _dbContext.GeographicPlaces.AddAsync(place, cancellationToken);
        await _dbContext.GeographicCatalogReleasePlaces.AddAsync(new GeographicCatalogReleasePlace { CatalogReleaseId = request.CatalogReleaseId, PlaceId = place.Id, PlaceStatus = place.Status }, cancellationToken);
        await AddAuditAsync("CatalogPlaceStaged", "GeographicPlace", place.Id, actorId, actorRole, request.Reason, catalogReleaseId: request.CatalogReleaseId, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return Success(place.Id);
    }

    public Task<GeographicOperationResultDto> AddLocalizationAsync(LocalizationRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        var entity = new GeographicPlaceName
        {
            Id = Guid.NewGuid(),
            PlaceId = request.PlaceId,
            LanguageTag = request.LanguageTag.Trim(),
            ScriptCode = request.ScriptCode,
            NameKind = request.NameKind,
            Name = request.Name.Trim(),
            NormalizedName = request.NormalizedName.Trim(),
            SearchName = request.SearchName.Trim(),
            DisplayPriority = request.DisplayPriority,
            IsPreferred = request.IsPreferred,
            ValidFromUtc = request.ValidFromUtc,
            ValidToUtc = request.ValidToUtc,
            IntroducedCatalogReleaseId = request.CatalogReleaseId,
            CreatedAtUtc = DateTime.UtcNow
        };

        return AddCatalogChildAsync(entity, request.CatalogReleaseId, "LocalizationStaged", "GeographicPlaceName", entity.Id, actorId, actorRole, request.Reason, cancellationToken);
    }

    public Task<GeographicOperationResultDto> AddAliasAsync(AliasRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        var entity = new GeographicPlaceAlias
        {
            Id = Guid.NewGuid(),
            PlaceId = request.PlaceId,
            CountryPlaceId = request.CountryPlaceId,
            LanguageTag = request.LanguageTag,
            AliasKind = request.AliasKind,
            Alias = request.Alias.Trim(),
            NormalizedAlias = request.NormalizedAlias.Trim(),
            SearchAlias = request.SearchAlias.Trim(),
            Confidence = request.Confidence,
            Source = request.Source.Trim(),
            IsAmbiguous = request.IsAmbiguous,
            IntroducedCatalogReleaseId = request.CatalogReleaseId,
            CreatedAtUtc = DateTime.UtcNow
        };

        return AddCatalogChildAsync(entity, request.CatalogReleaseId, "AliasStaged", "GeographicPlaceAlias", entity.Id, actorId, actorRole, request.Reason, cancellationToken);
    }

    public Task<GeographicOperationResultDto> AddCountryProfileAsync(CountryProfileRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        var entity = new GeographicCountryProfile
        {
            Id = Guid.NewGuid(),
            CountryPlaceId = request.CountryPlaceId,
            Iso2 = request.Iso2.Trim().ToUpperInvariant(),
            Iso3 = request.Iso3.Trim().ToUpperInvariant(),
            DefaultLanguageTag = request.DefaultLanguageTag.Trim(),
            SupportedLanguageTags = request.SupportedLanguageTags,
            RequiredLevelCodesForDelivery = request.RequiredLevelCodesForDelivery,
            RequiredLevelCodesForOperations = request.RequiredLevelCodesForOperations,
            PostalCodePolicy = request.PostalCodePolicy,
            PostalCodeRegex = request.PostalCodeRegex,
            RequiresStreetLine1 = request.RequiresStreetLine1,
            AllowsCoordinates = request.AllowsCoordinates,
            RequiresCoordinateProvenance = request.RequiresCoordinateProvenance,
            OfflinePackageEnabled = request.OfflinePackageEnabled,
            IsActive = request.IsActive,
            IntroducedCatalogReleaseId = request.CatalogReleaseId,
            CreatedAtUtc = DateTime.UtcNow
        };

        return AddCatalogChildAsync(entity, request.CatalogReleaseId, "CountryProfileStaged", "GeographicCountryProfile", entity.Id, actorId, actorRole, request.Reason, cancellationToken);
    }

    public async Task<GeographicOperationResultDto> AddMergeAsync(PlaceMergeRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginCatalogTransactionAsync(cancellationToken);
        if (await RequireDraftCatalogReleaseAsync(request.CatalogReleaseId, cancellationToken) == null) return Fail("CatalogDraftRequired");
        if (request.SourcePlaceId == request.TargetPlaceId) return Fail("SelfMergeForbidden");
        var merge = new PlaceMergeHistory { Id = Guid.NewGuid(), CatalogReleaseId = request.CatalogReleaseId, SourcePlaceId = request.SourcePlaceId, TargetPlaceId = request.TargetPlaceId, MergeKind = request.MergeKind, Reason = request.Reason, CreatedAtUtc = DateTime.UtcNow };
        await _dbContext.PlaceMergeHistory.AddAsync(merge, cancellationToken);
        await AddAuditAsync("PlaceMergeStaged", "PlaceMergeHistory", merge.Id, actorId, actorRole, request.Reason, catalogReleaseId: request.CatalogReleaseId, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return Success(merge.Id);
    }

    public async Task<GeographicOperationResultDto> AddDeprecationAsync(PlaceDeprecationRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginCatalogTransactionAsync(cancellationToken);
        if (await RequireDraftCatalogReleaseAsync(request.CatalogReleaseId, cancellationToken) == null) return Fail("CatalogDraftRequired");
        if (request.SuccessorPlaceId == request.PlaceId) return Fail("SelfSuccessorForbidden");
        var deprecation = new PlaceDeprecation { Id = Guid.NewGuid(), CatalogReleaseId = request.CatalogReleaseId, PlaceId = request.PlaceId, SuccessorPlaceId = request.SuccessorPlaceId, DeprecationKind = request.DeprecationKind, Reason = request.Reason, CreatedAtUtc = DateTime.UtcNow };
        await _dbContext.PlaceDeprecations.AddAsync(deprecation, cancellationToken);
        await AddAuditAsync("PlaceDeprecationStaged", "PlaceDeprecation", deprecation.Id, actorId, actorRole, request.Reason, catalogReleaseId: request.CatalogReleaseId, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return Success(deprecation.Id);
    }

    public async Task<GeographicOperationResultDto> PublishCatalogReleaseAsync(Guid catalogReleaseId, ReleaseActionRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginCatalogTransactionAsync(cancellationToken);
        var release = await LoadCatalogReleaseAsync(catalogReleaseId, cancellationToken);
        if (release is null) return Fail("CatalogReleaseNotFound");
        if (release.Status != GeographicReleaseStatus.Validated) return Fail("ValidatedCatalogReleaseRequired");
        if (release.CreatedByUserId == actorId) return Fail("FourEyesApprovalRequired");
        if (request.ExpectedContentChecksumSha256 != null && request.ExpectedContentChecksumSha256 != release.ChecksumSha256 ||
            request.ExpectedContentChecksumSha256 == null && await _dbContext.GeographicAudit.AnyAsync(a => a.CatalogReleaseId == release.Id && a.Action == "CatalogImportStagedV1", cancellationToken))
            return Fail("CatalogContentChanged");
        var validationFailure = await RequireInitialCatalogPublicationAsync(release, actorId, actorRole, request.Reason, cancellationToken);
        if (validationFailure != null) return Fail(validationFailure);

        var now = DateTime.UtcNow;
        release.Status = GeographicReleaseStatus.Published;
        release.PublishedAtUtc = now;
        release.PublishedByUserId = actorId;
        var publication = new GeographicCatalogPublication { Id = Guid.NewGuid(), CatalogReleaseId = catalogReleaseId, PublicationKind = GeographicPublicationKind.Publish, PublishedAtUtc = now, ActivatedAtUtc = now, PublishedByUserId = actorId, Reason = request.Reason, CreatedAtUtc = now };
        await _dbContext.GeographicCatalogPublications.AddAsync(publication, cancellationToken);
        await AddAuditAsync("CatalogReleasePublished", "GeographicCatalogRelease", release.Id, actorId, actorRole, request.Reason, catalogReleaseId: release.Id, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return new GeographicOperationResultDto(true, null, release.Id, publication.Id, release.Id);
    }

    public Task<GeographicOperationResultDto> RollbackCatalogReleaseAsync(Guid targetCatalogReleaseId, ReleaseActionRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        // Runtime catalog selection does not consume rollback publication history. Recording
        // an "activated" rollback would falsely acknowledge a pointer switch that never occurs.
        return Task.FromResult(Fail("CatalogReplacementAndRollbackWorkflowRequired"));
    }

    public async Task<GeographicOperationResultDto> CreateMappingDraftAsync(CreateMappingDraftRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var release = new CarrierGeographicMappingRelease
        {
            Id = Guid.NewGuid(),
            CarrierProviderId = request.CarrierProviderId,
            ReleaseNumber = request.ReleaseNumber,
            ReleaseKey = request.ReleaseKey.Trim(),
            Status = GeographicReleaseStatus.Draft,
            Environment = request.Environment,
            AccountScopeKind = request.AccountScopeKind,
            AccountScopeId = request.AccountScopeId,
            TenantId = request.TenantId,
            CatalogReleaseId = request.CatalogReleaseId,
            SourceSummary = request.SourceSummary.Trim(),
            ChecksumSha256 = Checksum(request.ReleaseKey, request.SourceSummary, request.CatalogReleaseId.ToString()),
            EffectiveFromUtc = request.EffectiveFromUtc,
            EffectiveToUtc = request.EffectiveToUtc,
            FreshnessExpiresAtUtc = request.FreshnessExpiresAtUtc,
            CreatedByUserId = actorId,
            CreatedAtUtc = now
        };

        await _dbContext.CarrierGeographicMappingReleases.AddAsync(release, cancellationToken);
        await AddAuditAsync("MappingDraftCreated", "CarrierGeographicMappingRelease", release.Id, actorId, actorRole, request.Reason, mappingReleaseId: release.Id, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Success(release.Id);
    }

    public async Task<GeographicOperationResultDto> AddMappingAsync(MappingRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginCatalogTransactionAsync(cancellationToken);
        var release = await RequireDraftMappingReleaseAsync(request.MappingReleaseId, cancellationToken);
        if (release is null) return Fail("MappingDraftRequired");
        var mapping = new CarrierGeographicMapping
        {
            Id = Guid.NewGuid(),
            MappingReleaseId = request.MappingReleaseId,
            MappingGroupId = request.MappingGroupId,
            CarrierProviderId = request.CarrierProviderId,
            CanonicalPlaceId = request.CanonicalPlaceId,
            CountryPlaceId = request.CountryPlaceId,
            CanonicalPlaceKind = request.CanonicalPlaceKind,
            ProviderPlaceType = request.ProviderPlaceType.Trim(),
            ProviderPlaceId = request.ProviderPlaceId.Trim(),
            ProviderPlaceCode = request.ProviderPlaceCode,
            ProviderDisplayName = request.ProviderDisplayName.Trim(),
            ProviderNormalizedName = request.ProviderNormalizedName.Trim(),
            ProviderParentMappingId = request.ProviderParentMappingId,
            RequiredForOperation = request.RequiredForOperation.Trim(),
            MappingStatus = request.MappingStatus,
            VerificationStatus = request.VerificationStatus,
            Source = request.Source.Trim(),
            EffectiveFromUtc = request.EffectiveFromUtc,
            EffectiveToUtc = request.EffectiveToUtc,
            ReviewedAtUtc = DateTime.UtcNow,
            ReviewedByUserId = actorId,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _dbContext.CarrierGeographicMappings.AddAsync(mapping, cancellationToken);
        await AddAuditAsync("CarrierMappingStaged", "CarrierGeographicMapping", mapping.Id, actorId, actorRole, request.Reason, mappingReleaseId: request.MappingReleaseId, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return Success(mapping.Id);
    }

    public Task<GeographicOperationResultDto> PublishMappingReleaseAsync(Guid mappingReleaseId, ReleaseActionRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        return PublishOrRollbackMappingReleaseAsync(mappingReleaseId, request, actorId, actorRole, false, cancellationToken);
    }

    public Task<GeographicOperationResultDto> RollbackMappingReleaseAsync(Guid targetMappingReleaseId, ReleaseActionRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        return PublishOrRollbackMappingReleaseAsync(targetMappingReleaseId, request, actorId, actorRole, true, cancellationToken);
    }

    public async Task<GeographicOperationResultDto> RecordSyncRunAsync(SyncRunRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        var run = new CarrierGeographicSyncRun { Id = Guid.NewGuid(), CarrierProviderId = request.CarrierProviderId, Environment = request.Environment, AccountScopeKind = request.AccountScopeKind, AccountScopeId = request.AccountScopeId, TenantId = request.TenantId, SourceKind = request.SourceKind, SourceUri = request.SourceUri, SourceVersion = request.SourceVersion, SourceHashSha256 = request.SourceHashSha256, RawEvidenceObjectKey = request.RawEvidenceObjectKey, StartedAtUtc = DateTime.UtcNow, Status = CarrierGeographicSyncStatus.Pending, CreatedAtUtc = DateTime.UtcNow };
        await _dbContext.CarrierGeographicSyncRuns.AddAsync(run, cancellationToken);
        await AddAuditAsync("CarrierSyncRunRecorded", "CarrierGeographicSyncRun", run.Id, actorId, actorRole, request.Reason, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Success(run.Id);
    }

    public async Task<GeographicOperationResultDto> AddSyncCandidateAsync(SyncCandidateRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        var run = await _dbContext.CarrierGeographicSyncRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == request.SyncRunId && r.CarrierProviderId == request.CarrierProviderId, cancellationToken);
        if (run == null) return Fail("SyncRunNotFound");
        if (await _dbContext.CarrierProviders.AnyAsync(p => p.Id == run.CarrierProviderId && p.CarrierCode == "bosta", cancellationToken))
            return Fail("BOSTA_AUTHENTICATED_DISCOVERY_REQUIRED");
        var candidate = new CarrierGeographicSyncCandidate { Id = Guid.NewGuid(), SyncRunId = request.SyncRunId, CarrierProviderId = request.CarrierProviderId, ProviderPlaceType = request.ProviderPlaceType.Trim(), ProviderPlaceId = request.ProviderPlaceId.Trim(), ProviderPlaceCode = request.ProviderPlaceCode, ProviderDisplayName = request.ProviderDisplayName.Trim(), ProviderNormalizedName = request.ProviderNormalizedName.Trim(), ProviderParentPlaceId = request.ProviderParentPlaceId, CandidateCanonicalPlaceId = request.CandidateCanonicalPlaceId, Confidence = request.Confidence, CandidateStatus = CarrierGeographicSyncCandidateStatus.PendingReview, CreatedAtUtc = DateTime.UtcNow };
        await _dbContext.CarrierGeographicSyncCandidates.AddAsync(candidate, cancellationToken);
        await AddAuditAsync("CarrierSyncCandidateRecorded", "CarrierGeographicSyncCandidate", candidate.Id, actorId, actorRole, request.Reason, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Success(candidate.Id);
    }

    public async Task<GeographicOperationResultDto> ReviewSyncCandidateAsync(Guid syncCandidateId, CarrierGeographicSyncCandidateStatus status, CandidateReviewRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        if (status is not (CarrierGeographicSyncCandidateStatus.Approved or CarrierGeographicSyncCandidateStatus.Rejected or CarrierGeographicSyncCandidateStatus.Deferred)) return Fail("InvalidCandidateReviewStatus");
        var candidate = await _dbContext.CarrierGeographicSyncCandidates.FirstOrDefaultAsync(x => x.Id == syncCandidateId, cancellationToken);
        if (candidate is null) return Fail("SyncCandidateNotFound");
        if (await _dbContext.CarrierProviders.AnyAsync(p => p.Id == candidate.CarrierProviderId && p.CarrierCode == "bosta", cancellationToken))
            return Fail("BOSTA_REVIEWED_MAPPING_WORKFLOW_REQUIRED");
        candidate.CandidateStatus = status;
        candidate.ReviewReason = request.Reason;
        candidate.ReviewedByUserId = actorId;
        candidate.ReviewedAtUtc = DateTime.UtcNow;
        await AddAuditAsync($"CarrierSyncCandidate{status}", "CarrierGeographicSyncCandidate", candidate.Id, actorId, actorRole, request.Reason, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Success(candidate.Id);
    }

    public async Task<GeographicOperationResultDto> CreateOverrideAsync(OverrideRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        var entity = new CarrierGeographicOverride { Id = Guid.NewGuid(), CarrierProviderId = request.CarrierProviderId, Environment = request.Environment, AccountScopeKind = request.AccountScopeKind, AccountScopeId = request.AccountScopeId, TenantId = request.TenantId, CanonicalPlaceId = request.CanonicalPlaceId, ProviderPlaceType = request.ProviderPlaceType.Trim(), ProviderPlaceId = request.ProviderPlaceId.Trim(), ProviderPlaceCode = request.ProviderPlaceCode, ProviderDisplayName = request.ProviderDisplayName.Trim(), EffectiveFromUtc = request.EffectiveFromUtc, EffectiveToUtc = request.EffectiveToUtc, Status = GeographicOverrideStatus.Draft, Reason = request.Reason, CreatedAtUtc = DateTime.UtcNow };
        await _dbContext.CarrierGeographicOverrides.AddAsync(entity, cancellationToken);
        await AddAuditAsync("CarrierOverrideCreated", "CarrierGeographicOverride", entity.Id, actorId, actorRole, request.Reason, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Success(entity.Id);
    }

    public async Task<GeographicOperationResultDto> ApproveOverrideAsync(Guid overrideId, ReleaseActionRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CarrierGeographicOverrides.FirstOrDefaultAsync(x => x.Id == overrideId, cancellationToken);
        if (entity is null) return Fail("CarrierOverrideNotFound");
        if (entity.Status != GeographicOverrideStatus.Draft) return Fail("DraftOverrideRequired");
        entity.Status = GeographicOverrideStatus.Approved;
        entity.ApprovedByUserId = actorId;
        entity.ApprovedAtUtc = DateTime.UtcNow;
        await AddAuditAsync("CarrierOverrideApproved", "CarrierGeographicOverride", entity.Id, actorId, actorRole, request.Reason, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Success(entity.Id);
    }

    public async Task<IReadOnlyList<GeographicAuditDto>> GetAuditAsync(int limit, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, 200);
        return await _dbContext.GeographicAudit
            .AsNoTracking()
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(take)
            .Select(x => new GeographicAuditDto(x.Id, x.TenantId, x.ActorKind, x.ActorId, x.ActorRole, x.Action, x.AggregateType, x.AggregateId, x.CatalogReleaseId, x.MappingReleaseId, x.Reason, x.CorrelationId, x.OccurredAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<GeographicOperationResultDto> AddCatalogChildAsync<TEntity>(TEntity entity, Guid catalogReleaseId, string action, string aggregateType, Guid aggregateId, Guid actorId, string actorRole, string? reason, CancellationToken cancellationToken) where TEntity : class
    {
        await using var transaction = await BeginCatalogTransactionAsync(cancellationToken);
        var release = await RequireDraftCatalogReleaseAsync(catalogReleaseId, cancellationToken);
        if (release is null) return Fail("CatalogDraftRequired");
        var placeId = entity switch
        {
            GeographicPlaceName name => name.PlaceId,
            GeographicPlaceAlias alias => alias.PlaceId,
            GeographicCountryProfile profile => profile.CountryPlaceId,
            _ => Guid.Empty
        };
        if (!await _dbContext.GeographicPlaces.AnyAsync(p => p.Id == placeId &&
                p.IntroducedCatalogReleaseId == catalogReleaseId, cancellationToken)) return Fail("CatalogPlaceNotInDraft");
        await _dbContext.Set<TEntity>().AddAsync(entity, cancellationToken);
        await AddAuditAsync(action, aggregateType, aggregateId, actorId, actorRole, reason, catalogReleaseId: catalogReleaseId, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return Success(aggregateId);
    }

    private async Task<GeographicOperationResultDto> PublishOrRollbackMappingReleaseAsync(Guid mappingReleaseId, ReleaseActionRequest request, Guid actorId, string actorRole, bool rollback, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginCatalogTransactionAsync(cancellationToken);
        var release = await _dbContext.CarrierGeographicMappingReleases.FirstOrDefaultAsync(x => x.Id == mappingReleaseId, cancellationToken);
        if (release is null) return Fail("MappingReleaseNotFound");
        await _dbContext.Entry(release).ReloadAsync(cancellationToken);
        if (!ValidActor(actorId, actorRole) || !ValidReason(request.Reason)) return Fail("MappingActorAndReasonRequired");
        if (rollback)
        {
            if (release.Status != GeographicReleaseStatus.Published) return Fail("PublishedMappingReleaseRequired");
        }
        else
        {
            if (release.Status != GeographicReleaseStatus.Validated) return Fail("ValidatedMappingReleaseRequired");
        }
        if (release.CreatedByUserId == actorId || await _dbContext.CarrierGeographicMappings.AnyAsync(m => m.MappingReleaseId == release.Id && m.ReviewedByUserId == actorId, cancellationToken))
            return Fail("FourEyesApprovalRequired");
        var preview = await InspectMappingDraftAsync(mappingReleaseId, cancellationToken);
        if (!preview.Succeeded) return Fail(preview.FailureCode!);
        if (preview.ContentChecksumSha256 != request.ExpectedContentChecksumSha256 || preview.ContentChecksumSha256 != release.ChecksumSha256)
            return Fail("MappingContentChanged");
        var validation = await _dbContext.GeographicAudit.AsNoTracking().AnyAsync(a => a.MappingReleaseId == release.Id &&
            a.Action == "MappingReleaseValidatedV1" && a.ValidationChecksumSha256 == preview.ContentChecksumSha256, cancellationToken);
        if (!validation) return Fail("MappingValidationEvidenceRequired");
        if (!rollback)
        {
            release.Status = GeographicReleaseStatus.Published;
            release.PublishedAtUtc = DateTime.UtcNow;
            release.PublishedByUserId = actorId;
        }

        var now = DateTime.UtcNow;
        var lastActivation = await (from p in _dbContext.CarrierGeographicMappingPublications.AsNoTracking()
            join r in _dbContext.CarrierGeographicMappingReleases on p.MappingReleaseId equals r.Id
            where p.SupersededAtUtc == null && r.CarrierProviderId == release.CarrierProviderId && r.TenantId == release.TenantId &&
                r.Environment == release.Environment && r.AccountScopeKind == release.AccountScopeKind && r.AccountScopeId == release.AccountScopeId
            select p.ActivatedAtUtc).MaxAsync(cancellationToken);
        // Publication evidence is append-only. A newer activation supersedes the
        // previous scope at read time; never rewrite a historical publication.
        if (lastActivation >= now) now = lastActivation.Value.AddMilliseconds(1);
        var publication = new CarrierGeographicMappingPublication { Id = Guid.NewGuid(), MappingReleaseId = mappingReleaseId, PublicationKind = rollback ? GeographicPublicationKind.Rollback : GeographicPublicationKind.Publish, PublishedAtUtc = now, ActivatedAtUtc = now, PublishedByUserId = actorId, Reason = request.Reason, CreatedAtUtc = now };
        await _dbContext.CarrierGeographicMappingPublications.AddAsync(publication, cancellationToken);
        await AddAuditAsync(rollback ? "MappingReleaseRollbackActivated" : "MappingReleasePublished", "CarrierGeographicMappingRelease", release.Id, actorId, actorRole, request.Reason, mappingReleaseId: release.Id, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return new GeographicOperationResultDto(true, null, release.Id, publication.Id, release.Id);
    }

    private async Task<GeographicCatalogRelease?> RequireDraftCatalogReleaseAsync(Guid id, CancellationToken cancellationToken)
    {
        var release = await LoadCatalogReleaseAsync(id, cancellationToken);
        return release?.Status == GeographicReleaseStatus.Draft ? release : null;
    }

    private async Task<CarrierGeographicMappingRelease?> RequireDraftMappingReleaseAsync(Guid id, CancellationToken cancellationToken)
    {
        var release = await _dbContext.CarrierGeographicMappingReleases.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (release != null) await _dbContext.Entry(release).ReloadAsync(cancellationToken);
        return release?.Status == GeographicReleaseStatus.Draft ? release : null;
    }

    private async Task AddAuditAsync(string action, string aggregateType, Guid aggregateId, Guid actorId, string actorRole, string? reason, Guid? catalogReleaseId = null, Guid? mappingReleaseId = null, CancellationToken cancellationToken = default)
    {
        var tenantId = mappingReleaseId.HasValue ? _dbContext.ChangeTracker.Entries<CarrierGeographicMappingRelease>()
            .FirstOrDefault(entry => entry.Entity.Id == mappingReleaseId)?.Entity.TenantId : null;
        await _dbContext.GeographicAudit.AddAsync(new GeographicAudit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorKind = GeographicAuditActorKind.PosAdmin,
            ActorId = actorId,
            ActorRole = actorRole,
            Action = action,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            CatalogReleaseId = catalogReleaseId,
            MappingReleaseId = mappingReleaseId,
            Reason = string.IsNullOrWhiteSpace(reason) ? "POS Admin geographic administration" : reason.Trim(),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    private static GeographicOperationResultDto Success(Guid resourceId) => new(true, null, resourceId);
    private static GeographicOperationResultDto Fail(string code) => new(false, code);
    private static string? NormalizeIso2(string? iso2) => string.IsNullOrWhiteSpace(iso2) ? null : iso2.Trim().ToUpperInvariant();
    private static string Checksum(params string?[] parts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)))).ToLowerInvariant();
}
