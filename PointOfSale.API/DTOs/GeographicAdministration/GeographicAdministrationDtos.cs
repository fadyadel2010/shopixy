using PointOfSale.Core.Entities.Geographic;

namespace PointOfSale.API.DTOs.GeographicAdministration;

public sealed record GeographicOperationResultDto(
    bool Succeeded,
    string? FailureCode,
    Guid? ResourceId = null,
    Guid? PublicationId = null,
    Guid? ActivatedReleaseId = null);

public sealed record GeographicAuditDto(
    Guid Id,
    Guid? TenantId,
    GeographicAuditActorKind ActorKind,
    Guid? ActorId,
    string ActorRole,
    string Action,
    string AggregateType,
    Guid AggregateId,
    Guid? CatalogReleaseId,
    Guid? MappingReleaseId,
    string Reason,
    string CorrelationId,
    DateTime OccurredAtUtc);

public sealed record CreateCatalogDraftRequest(
    long ReleaseNumber,
    string ReleaseKey,
    string? CountryScopeIso2,
    string SourceSummary,
    string? Reason);

public sealed record EditCatalogDraftRequest(
    string SourceSummary,
    string? Reason);

public sealed record CatalogPlaceRequest(
    Guid CatalogReleaseId,
    Guid? PlaceId,
    string StableKey,
    GeographicPlaceKind PlaceKind,
    Guid? CountryPlaceId,
    string? LevelCode,
    string CanonicalCode,
    string? ExternalStandard,
    string? ExternalStandardCode,
    GeographicLifecycleStatus Status,
    Guid? SuccessorPlaceId,
    DateTime ValidFromUtc,
    DateTime? ValidToUtc,
    string Source,
    string? Reason);

public sealed record LocalizationRequest(
    Guid CatalogReleaseId,
    Guid PlaceId,
    string LanguageTag,
    string? ScriptCode,
    GeographicNameKind NameKind,
    string Name,
    string NormalizedName,
    string SearchName,
    int DisplayPriority,
    bool IsPreferred,
    DateTime ValidFromUtc,
    DateTime? ValidToUtc,
    string? Reason);

public sealed record AliasRequest(
    Guid CatalogReleaseId,
    Guid PlaceId,
    Guid CountryPlaceId,
    string? LanguageTag,
    GeographicAliasKind AliasKind,
    string Alias,
    string NormalizedAlias,
    string SearchAlias,
    short Confidence,
    string Source,
    bool IsAmbiguous,
    string? Reason);

public sealed record CountryProfileRequest(
    Guid CatalogReleaseId,
    Guid CountryPlaceId,
    string Iso2,
    string Iso3,
    string DefaultLanguageTag,
    string[] SupportedLanguageTags,
    string[] RequiredLevelCodesForDelivery,
    string[] RequiredLevelCodesForOperations,
    GeographicPostalCodePolicy PostalCodePolicy,
    string? PostalCodeRegex,
    bool RequiresStreetLine1,
    bool AllowsCoordinates,
    bool RequiresCoordinateProvenance,
    bool OfflinePackageEnabled,
    bool IsActive,
    string? Reason);

public sealed record ReleaseActionRequest(string Reason);

public sealed record PlaceMergeRequest(
    Guid CatalogReleaseId,
    Guid SourcePlaceId,
    Guid TargetPlaceId,
    PlaceMergeKind MergeKind,
    string Reason);

public sealed record PlaceDeprecationRequest(
    Guid CatalogReleaseId,
    Guid PlaceId,
    Guid? SuccessorPlaceId,
    PlaceDeprecationKind DeprecationKind,
    string Reason);

public sealed record CreateMappingDraftRequest(
    Guid CarrierProviderId,
    long ReleaseNumber,
    string ReleaseKey,
    CarrierGeographicEnvironment Environment,
    CarrierGeographicAccountScopeKind AccountScopeKind,
    Guid? AccountScopeId,
    Guid? TenantId,
    Guid CatalogReleaseId,
    string SourceSummary,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    DateTime? FreshnessExpiresAtUtc,
    string? Reason);

public sealed record MappingRequest(
    Guid MappingReleaseId,
    Guid? MappingGroupId,
    Guid CarrierProviderId,
    Guid CanonicalPlaceId,
    Guid CountryPlaceId,
    GeographicPlaceKind CanonicalPlaceKind,
    string ProviderPlaceType,
    string ProviderPlaceId,
    string? ProviderPlaceCode,
    string ProviderDisplayName,
    string ProviderNormalizedName,
    Guid? ProviderParentMappingId,
    string RequiredForOperation,
    CarrierGeographicMappingStatus MappingStatus,
    CarrierGeographicVerificationStatus VerificationStatus,
    string Source,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    string? Reason);

public sealed record SyncRunRequest(
    Guid CarrierProviderId,
    CarrierGeographicEnvironment Environment,
    CarrierGeographicAccountScopeKind AccountScopeKind,
    Guid? AccountScopeId,
    Guid? TenantId,
    CarrierGeographicSyncSourceKind SourceKind,
    string? SourceUri,
    string? SourceVersion,
    string? SourceHashSha256,
    string? RawEvidenceObjectKey,
    string? Reason);

public sealed record SyncCandidateRequest(
    Guid SyncRunId,
    Guid CarrierProviderId,
    string ProviderPlaceType,
    string ProviderPlaceId,
    string? ProviderPlaceCode,
    string ProviderDisplayName,
    string ProviderNormalizedName,
    string? ProviderParentPlaceId,
    Guid? CandidateCanonicalPlaceId,
    short Confidence,
    string? Reason);

public sealed record CandidateReviewRequest(string Reason);

public sealed record OverrideRequest(
    Guid CarrierProviderId,
    CarrierGeographicEnvironment Environment,
    CarrierGeographicAccountScopeKind AccountScopeKind,
    Guid? AccountScopeId,
    Guid? TenantId,
    Guid CanonicalPlaceId,
    string ProviderPlaceType,
    string ProviderPlaceId,
    string? ProviderPlaceCode,
    string ProviderDisplayName,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    string Reason);
