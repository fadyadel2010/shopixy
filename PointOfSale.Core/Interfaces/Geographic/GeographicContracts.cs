using PointOfSale.Core.Entities.Geographic;

namespace PointOfSale.Core.Interfaces.Geographic;

public sealed record GeographicPlaceLineage(
    Guid CountryPlaceId,
    Guid? AdministrativeAreaPlaceId,
    Guid? LocalityPlaceId,
    Guid? DistrictPlaceId,
    Guid? ZonePlaceId);

public sealed record GeographicAddressCandidate(
    Guid? TenantId,
    AddressAdmissionOwnerDomain OwnerDomain,
    Guid? OwnerId,
    GeographicPlaceLineage Lineage,
    string AddressFingerprintSha256,
    string? PostalCode,
    decimal? Latitude,
    decimal? Longitude,
    string? CoordinateSource);

public sealed record GeographicAdmissionResult(
    bool Succeeded,
    Guid? AddressAdmissionEvidenceId,
    Guid? CatalogReleaseId,
    string? FailureCode);

public sealed record GeographicCatalogSearchRequest(
    Guid CountryPlaceId,
    Guid? ParentPlaceId,
    string LanguageTag,
    string SearchText,
    Guid? CatalogReleaseId);

public sealed record GeographicPlaceReadModel(
    Guid PlaceId,
    GeographicPlaceKind PlaceKind,
    Guid? ParentPlaceId,
    string CanonicalCode,
    string DisplayName,
    Guid CatalogReleaseId);

public sealed record CarrierGeographicMappingRequest(
    string CarrierCode,
    CarrierGeographicEnvironment Environment,
    CarrierGeographicAccountScopeKind AccountScopeKind,
    Guid? AccountScopeId,
    Guid? TenantId,
    string RequiredForOperation,
    Guid CatalogReleaseId,
    GeographicPlaceLineage Lineage,
    string[] RequiredProviderPlaceTypes,
    string? PostalCode,
    bool HasStreetLine1);

public sealed record CarrierGeographicMappingItem(
    Guid MappingId,
    Guid? MappingGroupId,
    Guid CanonicalPlaceId,
    Guid CountryPlaceId,
    string ProviderPlaceType,
    string ProviderPlaceId,
    string? ProviderPlaceCode,
    string ProviderDisplayName,
    Guid? ProviderParentMappingId);

public sealed record CarrierGeographicMappingResult(
    bool Succeeded,
    Guid? MappingReleaseId,
    long? MappingReleaseVersion,
    string? ReleaseKey,
    CarrierGeographicEnvironment? Environment,
    CarrierGeographicAccountScopeKind? AccountScopeKind,
    Guid? AccountScopeId,
    DateTime? FreshnessExpiresAtUtc,
    string? CountryIso2,
    IReadOnlyList<CarrierGeographicMappingItem> Items,
    string? FailureCode);

public sealed record GeographicPublicationResult(
    bool Succeeded,
    Guid? PublicationId,
    Guid? ActivatedReleaseId,
    string? FailureCode);

public sealed record CarrierGeographicSyncRunRequest(
    Guid CarrierProviderId,
    CarrierGeographicEnvironment Environment,
    CarrierGeographicAccountScopeKind AccountScopeKind,
    Guid? AccountScopeId,
    Guid? TenantId,
    CarrierGeographicSyncSourceKind SourceKind,
    string? SourceUri,
    string? SourceVersion,
    string? SourceHashSha256);

public interface IGeographicRepository
{
    IQueryable<GeographicCatalogRelease> CatalogReleases { get; }
    IQueryable<GeographicPlace> Places { get; }
    IQueryable<GeographicPlaceHierarchyEdge> HierarchyEdges { get; }
    IQueryable<GeographicCountryProfile> CountryProfiles { get; }
    IQueryable<CarrierProvider> CarrierProviders { get; }
    IQueryable<CarrierGeographicMappingRelease> MappingReleases { get; }
    IQueryable<CarrierGeographicMappingPublication> MappingPublications { get; }
    IQueryable<CarrierGeographicMapping> Mappings { get; }
    Task AddAddressAdmissionEvidenceAsync(AddressAdmissionEvidence evidence, CancellationToken cancellationToken = default);
}

public interface IGeographicCatalogService
{
    Task<IReadOnlyList<GeographicPlaceReadModel>> SearchPublishedPlacesAsync(
        GeographicCatalogSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<GeographicCatalogRelease?> GetActivePublishedCatalogReleaseAsync(
        string? countryIso2,
        CancellationToken cancellationToken = default);
}

public interface IGeographicResolver
{
    Task<GeographicAdmissionResult> ValidatePublishedLineageAsync(
        GeographicAddressCandidate candidate,
        CancellationToken cancellationToken = default);
}

public interface IGeographicPublicationService
{
    Task<GeographicPublicationResult> PublishCatalogReleaseAsync(
        Guid catalogReleaseId,
        Guid actorUserId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<GeographicPublicationResult> RollbackCatalogReleaseAsync(
        Guid targetCatalogReleaseId,
        Guid actorUserId,
        string reason,
        CancellationToken cancellationToken = default);
}

public interface ICarrierGeographicMappingService
{
    Task<CarrierGeographicMappingResult> ResolvePublishedMappingAsync(
        CarrierGeographicMappingRequest request,
        CancellationToken cancellationToken = default);
}

public interface IGeographicSynchronizationService
{
    Task<CarrierGeographicSyncRun> RecordSyncRunAsync(
        CarrierGeographicSyncRunRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAddressAdmissionService
{
    Task<GeographicAdmissionResult> AdmitAsync(
        GeographicAddressCandidate candidate,
        CancellationToken cancellationToken = default);
}
