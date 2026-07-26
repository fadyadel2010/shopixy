namespace PointOfSale.Core.Entities.Geographic;

public abstract class GeographicEntity
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public enum GeographicPlaceKind
{
    Country = 0,
    AdministrativeArea = 1,
    Locality = 2,
    District = 3,
    Zone = 4
}

public enum GeographicLifecycleStatus
{
    Draft = 0,
    Active = 1,
    Deprecated = 2,
    Merged = 3,
    Retired = 4,
    Rejected = 5
}

public enum GeographicReleaseStatus
{
    Draft = 0,
    Validated = 1,
    Published = 2,
    Superseded = 3,
    RolledBack = 4,
    Rejected = 5
}

public enum GeographicPublicationKind
{
    Initial = 0,
    Publish = 1,
    Rollback = 2,
    Supersede = 3
}

public enum GeographicNameKind
{
    Official = 0,
    Display = 1,
    Short = 2,
    Search = 3,
    Historic = 4,
    Transliteration = 5,
    ProviderLabel = 6
}

public enum GeographicAliasKind
{
    Spelling = 0,
    Legacy = 1,
    Transliteration = 2,
    Abbreviation = 3,
    Historic = 4,
    ProviderLabel = 5,
    MigrationEvidence = 6
}

public enum GeographicPostalCodePolicy
{
    NotUsed = 0,
    Optional = 1,
    Required = 2,
    Forbidden = 3
}

public enum CarrierGeographicEnvironment
{
    Production = 0,
    Sandbox = 1,
    Test = 2
}

public enum CarrierGeographicAccountScopeKind
{
    Global = 0,
    ProviderAccount = 1,
    TenantProviderAccount = 2
}

public enum CarrierGeographicCardinalityPolicy
{
    OneToOne = 0,
    ManyCanonicalToOneProvider = 1,
    OneCanonicalToManyProvider = 2
}

public enum CarrierGeographicMappingStatus
{
    Draft = 0,
    Active = 1,
    Deprecated = 2,
    Retired = 3,
    Rejected = 4
}

public enum CarrierGeographicVerificationStatus
{
    Imported = 0,
    AutoCarried = 1,
    Reviewed = 2,
    ManuallyApproved = 3,
    Rejected = 4
}

public enum CarrierGeographicSyncStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Rejected = 4
}

public enum CarrierGeographicSyncSourceKind
{
    ProviderApi = 0,
    ProviderFile = 1,
    ManualImport = 2,
    VendorDocument = 3
}

public enum CarrierGeographicSyncCandidateStatus
{
    PendingReview = 0,
    Approved = 1,
    Rejected = 2,
    Deferred = 3
}

public enum GeographicOverrideStatus
{
    Draft = 0,
    Approved = 1,
    Published = 2,
    Rejected = 3,
    Retired = 4
}

public enum PlaceMergeKind
{
    Merge = 0,
    Split = 1,
    AdministrativeCorrection = 2
}

public enum PlaceDeprecationKind
{
    Successor = 0,
    NoSuccessor = 1,
    ManualReviewOnly = 2
}

public enum AddressAdmissionStatus
{
    Admitted = 0,
    Rejected = 1,
    ReviewRequired = 2,
    LegacyUnresolved = 3
}

public enum AddressAdmissionOwnerDomain
{
    Checkout = 0,
    CustomerAddressBook = 1,
    Order = 2,
    Shipment = 3,
    Warehouse = 4,
    Store = 5,
    Branch = 6,
    CarrierPickup = 7,
    CommerceHub = 8,
    AffiliateCheckout = 9,
    Return = 10,
    Migration = 11
}

public enum AddressNormalizationRuleKind
{
    Unicode = 0,
    PostalCode = 1,
    Street = 2,
    Search = 3
}

public enum GeographicAuditActorKind
{
    PlatformAdmin = 0,
    System = 1,
    Migration = 2,
    PosAdmin = 3
}

public sealed class GeographicPlace : GeographicEntity
{
    public string StableKey { get; set; } = string.Empty;
    public GeographicPlaceKind PlaceKind { get; set; }
    public Guid? CountryPlaceId { get; set; }
    public GeographicPlace? CountryPlace { get; set; }
    public string? LevelCode { get; set; }
    public string CanonicalCode { get; set; } = string.Empty;
    public string? ExternalStandard { get; set; }
    public string? ExternalStandardCode { get; set; }
    public GeographicLifecycleStatus Status { get; set; }
    public Guid? SuccessorPlaceId { get; set; }
    public GeographicPlace? SuccessorPlace { get; set; }
    public DateTime ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }
    public Guid IntroducedCatalogReleaseId { get; set; }
    public GeographicCatalogRelease IntroducedCatalogRelease { get; set; } = null!;
    public Guid? RetiredCatalogReleaseId { get; set; }
    public GeographicCatalogRelease? RetiredCatalogRelease { get; set; }
    public decimal? CentroidLatitude { get; set; }
    public decimal? CentroidLongitude { get; set; }
    public string? TimeZoneId { get; set; }
    public string Source { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public uint RowVersion { get; set; }
    public List<GeographicPlaceName> Names { get; set; } = new();
    public List<GeographicPlaceAlias> Aliases { get; set; } = new();
}

public sealed class GeographicPlaceLevel : GeographicEntity
{
    public Guid CountryPlaceId { get; set; }
    public GeographicPlace CountryPlace { get; set; } = null!;
    public string LevelCode { get; set; } = string.Empty;
    public string? ParentLevelCode { get; set; }
    public int SortOrder { get; set; }
    public GeographicPlaceKind PlaceKind { get; set; }
    public bool RequiredForDelivery { get; set; }
    public bool RequiredForOperations { get; set; }
    public bool RequiredForTax { get; set; }
    public string DisplayNameKey { get; set; } = string.Empty;
    public bool AllowsChildren { get; set; }
    public bool IsActive { get; set; }
    public Guid IntroducedCatalogReleaseId { get; set; }
    public GeographicCatalogRelease IntroducedCatalogRelease { get; set; } = null!;
    public Guid? RetiredCatalogReleaseId { get; set; }
    public GeographicCatalogRelease? RetiredCatalogRelease { get; set; }
}

public sealed class GeographicPlaceHierarchyEdge : GeographicEntity
{
    public Guid ParentPlaceId { get; set; }
    public GeographicPlace ParentPlace { get; set; } = null!;
    public Guid ChildPlaceId { get; set; }
    public GeographicPlace ChildPlace { get; set; } = null!;
    public Guid CountryPlaceId { get; set; }
    public GeographicPlace CountryPlace { get; set; } = null!;
    public int Depth { get; set; } = 1;
    public Guid IntroducedCatalogReleaseId { get; set; }
    public GeographicCatalogRelease IntroducedCatalogRelease { get; set; } = null!;
    public Guid? RetiredCatalogReleaseId { get; set; }
    public GeographicCatalogRelease? RetiredCatalogRelease { get; set; }
    public bool IsPrimary { get; set; } = true;
}

public sealed class GeographicPlaceName : GeographicEntity
{
    public Guid PlaceId { get; set; }
    public GeographicPlace Place { get; set; } = null!;
    public string LanguageTag { get; set; } = string.Empty;
    public string? ScriptCode { get; set; }
    public GeographicNameKind NameKind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string SearchName { get; set; } = string.Empty;
    public int DisplayPriority { get; set; } = 100;
    public bool IsPreferred { get; set; }
    public DateTime ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }
    public Guid IntroducedCatalogReleaseId { get; set; }
    public GeographicCatalogRelease IntroducedCatalogRelease { get; set; } = null!;
    public Guid? RetiredCatalogReleaseId { get; set; }
    public GeographicCatalogRelease? RetiredCatalogRelease { get; set; }
}

public sealed class GeographicPlaceAlias : GeographicEntity
{
    public Guid PlaceId { get; set; }
    public GeographicPlace Place { get; set; } = null!;
    public Guid CountryPlaceId { get; set; }
    public GeographicPlace CountryPlace { get; set; } = null!;
    public string? LanguageTag { get; set; }
    public GeographicAliasKind AliasKind { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string NormalizedAlias { get; set; } = string.Empty;
    public string SearchAlias { get; set; } = string.Empty;
    public short Confidence { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool IsAmbiguous { get; set; }
    public Guid IntroducedCatalogReleaseId { get; set; }
    public GeographicCatalogRelease IntroducedCatalogRelease { get; set; } = null!;
    public Guid? RetiredCatalogReleaseId { get; set; }
    public GeographicCatalogRelease? RetiredCatalogRelease { get; set; }
}

public sealed class GeographicCountryProfile : GeographicEntity
{
    public Guid CountryPlaceId { get; set; }
    public GeographicPlace CountryPlace { get; set; } = null!;
    public string Iso2 { get; set; } = string.Empty;
    public string Iso3 { get; set; } = string.Empty;
    public string DefaultLanguageTag { get; set; } = string.Empty;
    public string[] SupportedLanguageTags { get; set; } = Array.Empty<string>();
    public string[] RequiredLevelCodesForDelivery { get; set; } = Array.Empty<string>();
    public string[] RequiredLevelCodesForOperations { get; set; } = Array.Empty<string>();
    public GeographicPostalCodePolicy PostalCodePolicy { get; set; }
    public string? PostalCodeRegex { get; set; }
    public bool RequiresStreetLine1 { get; set; }
    public bool AllowsCoordinates { get; set; }
    public bool RequiresCoordinateProvenance { get; set; }
    public bool OfflinePackageEnabled { get; set; }
    public bool IsActive { get; set; }
    public Guid IntroducedCatalogReleaseId { get; set; }
    public GeographicCatalogRelease IntroducedCatalogRelease { get; set; } = null!;
    public Guid? RetiredCatalogReleaseId { get; set; }
    public GeographicCatalogRelease? RetiredCatalogRelease { get; set; }
}

public sealed class GeographicCatalogRelease : GeographicEntity
{
    public long ReleaseNumber { get; set; }
    public string ReleaseKey { get; set; } = string.Empty;
    public GeographicReleaseStatus Status { get; set; }
    public string? CountryScopeIso2 { get; set; }
    public string SourceSummary { get; set; } = string.Empty;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public Guid? SupersedesReleaseId { get; set; }
    public GeographicCatalogRelease? SupersedesRelease { get; set; }
    public Guid? RollbackOfReleaseId { get; set; }
    public GeographicCatalogRelease? RollbackOfRelease { get; set; }
    public uint RowVersion { get; set; }
}

public sealed class GeographicCatalogReleasePlace
{
    public Guid CatalogReleaseId { get; set; }
    public GeographicCatalogRelease CatalogRelease { get; set; } = null!;
    public Guid PlaceId { get; set; }
    public GeographicPlace Place { get; set; } = null!;
    public GeographicLifecycleStatus PlaceStatus { get; set; }
}

public sealed class GeographicCatalogReleaseEdge
{
    public Guid CatalogReleaseId { get; set; }
    public GeographicCatalogRelease CatalogRelease { get; set; } = null!;
    public Guid HierarchyEdgeId { get; set; }
    public GeographicPlaceHierarchyEdge HierarchyEdge { get; set; } = null!;
}

public sealed class GeographicCatalogPublication : GeographicEntity
{
    public Guid CatalogReleaseId { get; set; }
    public GeographicCatalogRelease CatalogRelease { get; set; } = null!;
    public GeographicPublicationKind PublicationKind { get; set; }
    public DateTime PublishedAtUtc { get; set; }
    public Guid PublishedByUserId { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? SupersededAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class CarrierProvider : GeographicEntity
{
    public string CarrierCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool SupportsGeographicMappings { get; set; }
    public bool RequiresMappingForDelivery { get; set; }
    public bool IsActive { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class CarrierGeographicMappingRelease : GeographicEntity
{
    public Guid CarrierProviderId { get; set; }
    public CarrierProvider CarrierProvider { get; set; } = null!;
    public long ReleaseNumber { get; set; }
    public string ReleaseKey { get; set; } = string.Empty;
    public GeographicReleaseStatus Status { get; set; }
    public CarrierGeographicEnvironment Environment { get; set; }
    public CarrierGeographicAccountScopeKind AccountScopeKind { get; set; }
    public Guid? AccountScopeId { get; set; }
    public Guid? TenantId { get; set; }
    public Guid CatalogReleaseId { get; set; }
    public GeographicCatalogRelease CatalogRelease { get; set; } = null!;
    public string SourceSummary { get; set; } = string.Empty;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public DateTime? FreshnessExpiresAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public Guid? SupersedesReleaseId { get; set; }
    public CarrierGeographicMappingRelease? SupersedesRelease { get; set; }
    public Guid? RollbackOfReleaseId { get; set; }
    public CarrierGeographicMappingRelease? RollbackOfRelease { get; set; }
    public uint RowVersion { get; set; }
    public List<CarrierGeographicMapping> Mappings { get; set; } = new();
}

public sealed class CarrierGeographicMappingGroup : GeographicEntity
{
    public Guid MappingReleaseId { get; set; }
    public CarrierGeographicMappingRelease MappingRelease { get; set; } = null!;
    public string GroupKey { get; set; } = string.Empty;
    public CarrierGeographicCardinalityPolicy CardinalityPolicy { get; set; }
    public GeographicPlaceKind CanonicalPlaceKind { get; set; }
    public string ProviderPlaceType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class CarrierGeographicMapping : GeographicEntity
{
    public Guid MappingReleaseId { get; set; }
    public CarrierGeographicMappingRelease MappingRelease { get; set; } = null!;
    public Guid? MappingGroupId { get; set; }
    public CarrierGeographicMappingGroup? MappingGroup { get; set; }
    public Guid CarrierProviderId { get; set; }
    public CarrierProvider CarrierProvider { get; set; } = null!;
    public Guid CanonicalPlaceId { get; set; }
    public GeographicPlace CanonicalPlace { get; set; } = null!;
    public Guid CountryPlaceId { get; set; }
    public GeographicPlace CountryPlace { get; set; } = null!;
    public GeographicPlaceKind CanonicalPlaceKind { get; set; }
    public string ProviderPlaceType { get; set; } = string.Empty;
    public string ProviderPlaceId { get; set; } = string.Empty;
    public string? ProviderPlaceCode { get; set; }
    public string ProviderDisplayName { get; set; } = string.Empty;
    public string ProviderNormalizedName { get; set; } = string.Empty;
    public Guid? ProviderParentMappingId { get; set; }
    public CarrierGeographicMapping? ProviderParentMapping { get; set; }
    public string RequiredForOperation { get; set; } = string.Empty;
    public CarrierGeographicMappingStatus MappingStatus { get; set; }
    public CarrierGeographicVerificationStatus VerificationStatus { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
}

public sealed class CarrierGeographicMappingPublication : GeographicEntity
{
    public Guid MappingReleaseId { get; set; }
    public CarrierGeographicMappingRelease MappingRelease { get; set; } = null!;
    public GeographicPublicationKind PublicationKind { get; set; }
    public DateTime PublishedAtUtc { get; set; }
    public Guid PublishedByUserId { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? SupersededAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class CarrierGeographicSyncRun : GeographicEntity
{
    public Guid CarrierProviderId { get; set; }
    public CarrierProvider CarrierProvider { get; set; } = null!;
    public CarrierGeographicEnvironment Environment { get; set; }
    public CarrierGeographicAccountScopeKind AccountScopeKind { get; set; }
    public Guid? AccountScopeId { get; set; }
    public Guid? TenantId { get; set; }
    public CarrierGeographicSyncSourceKind SourceKind { get; set; }
    public string? SourceUri { get; set; }
    public string? SourceVersion { get; set; }
    public string? SourceHashSha256 { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public CarrierGeographicSyncStatus Status { get; set; }
    public string? RawEvidenceObjectKey { get; set; }
    public int AddedCount { get; set; }
    public int ChangedCount { get; set; }
    public int RemovedCount { get; set; }
    public int AmbiguousCount { get; set; }
}

public sealed class CarrierGeographicSyncCandidate : GeographicEntity
{
    public Guid SyncRunId { get; set; }
    public CarrierGeographicSyncRun SyncRun { get; set; } = null!;
    public Guid CarrierProviderId { get; set; }
    public CarrierProvider CarrierProvider { get; set; } = null!;
    public string ProviderPlaceType { get; set; } = string.Empty;
    public string ProviderPlaceId { get; set; } = string.Empty;
    public string? ProviderPlaceCode { get; set; }
    public string ProviderDisplayName { get; set; } = string.Empty;
    public string ProviderNormalizedName { get; set; } = string.Empty;
    public string? ProviderParentPlaceId { get; set; }
    public Guid? CandidateCanonicalPlaceId { get; set; }
    public GeographicPlace? CandidateCanonicalPlace { get; set; }
    public short Confidence { get; set; }
    public CarrierGeographicSyncCandidateStatus CandidateStatus { get; set; }
    public string? ReviewReason { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
}

public sealed class CarrierGeographicOverride : GeographicEntity
{
    public Guid CarrierProviderId { get; set; }
    public CarrierProvider CarrierProvider { get; set; } = null!;
    public CarrierGeographicEnvironment Environment { get; set; }
    public CarrierGeographicAccountScopeKind AccountScopeKind { get; set; }
    public Guid? AccountScopeId { get; set; }
    public Guid? TenantId { get; set; }
    public Guid CanonicalPlaceId { get; set; }
    public GeographicPlace CanonicalPlace { get; set; } = null!;
    public string ProviderPlaceType { get; set; } = string.Empty;
    public string ProviderPlaceId { get; set; } = string.Empty;
    public string? ProviderPlaceCode { get; set; }
    public string ProviderDisplayName { get; set; } = string.Empty;
    public GeographicOverrideStatus Status { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
}

public sealed class PlaceMergeHistory : GeographicEntity
{
    public Guid SourcePlaceId { get; set; }
    public GeographicPlace SourcePlace { get; set; } = null!;
    public Guid TargetPlaceId { get; set; }
    public GeographicPlace TargetPlace { get; set; } = null!;
    public Guid CatalogReleaseId { get; set; }
    public GeographicCatalogRelease CatalogRelease { get; set; } = null!;
    public PlaceMergeKind MergeKind { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class PlaceDeprecation : GeographicEntity
{
    public Guid PlaceId { get; set; }
    public GeographicPlace Place { get; set; } = null!;
    public Guid? SuccessorPlaceId { get; set; }
    public GeographicPlace? SuccessorPlace { get; set; }
    public Guid CatalogReleaseId { get; set; }
    public GeographicCatalogRelease CatalogRelease { get; set; } = null!;
    public PlaceDeprecationKind DeprecationKind { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class AddressAdmissionEvidence : GeographicEntity
{
    public Guid? TenantId { get; set; }
    public AddressAdmissionOwnerDomain OwnerDomain { get; set; }
    public Guid? OwnerId { get; set; }
    public string AddressFingerprintSha256 { get; set; } = string.Empty;
    public AddressAdmissionStatus Status { get; set; }
    public string? FailureCode { get; set; }
    public Guid CatalogReleaseId { get; set; }
    public GeographicCatalogRelease CatalogRelease { get; set; } = null!;
    public Guid? CountryPlaceId { get; set; }
    public GeographicPlace? CountryPlace { get; set; }
    public Guid? AdministrativeAreaPlaceId { get; set; }
    public GeographicPlace? AdministrativeAreaPlace { get; set; }
    public Guid? LocalityPlaceId { get; set; }
    public GeographicPlace? LocalityPlace { get; set; }
    public Guid? DistrictPlaceId { get; set; }
    public GeographicPlace? DistrictPlace { get; set; }
    public Guid? ZonePlaceId { get; set; }
    public GeographicPlace? ZonePlace { get; set; }
    public DateTime AdmittedAtUtc { get; set; }
    public string? NormalizationRuleVersion { get; set; }
    public string? RawInputEvidenceObjectKey { get; set; }
}

public sealed class AddressNormalizationRule : GeographicEntity
{
    public Guid CountryPlaceId { get; set; }
    public GeographicPlace CountryPlace { get; set; } = null!;
    public string? LanguageTag { get; set; }
    public AddressNormalizationRuleKind RuleKind { get; set; }
    public long RuleVersion { get; set; }
    public string RuleDefinitionJson { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public Guid IntroducedCatalogReleaseId { get; set; }
    public GeographicCatalogRelease IntroducedCatalogRelease { get; set; } = null!;
    public Guid? RetiredCatalogReleaseId { get; set; }
    public GeographicCatalogRelease? RetiredCatalogRelease { get; set; }
}

public sealed class GeographicAudit : GeographicEntity
{
    public Guid? TenantId { get; set; }
    public GeographicAuditActorKind ActorKind { get; set; }
    public Guid? ActorId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public Guid? CatalogReleaseId { get; set; }
    public GeographicCatalogRelease? CatalogRelease { get; set; }
    public Guid? MappingReleaseId { get; set; }
    public CarrierGeographicMappingRelease? MappingRelease { get; set; }
    public string? CountryScopeIso2 { get; set; }
    public string? ProviderCode { get; set; }
    public string? Environment { get; set; }
    public string? AccountScope { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? ValidationChecksumSha256 { get; set; }
    public string? EvidenceObjectKey { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
