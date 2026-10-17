using PointOfSale.Core.Entities.Geographic;

namespace PointOfSale.API.DTOs.GeographicAdministration;

// Canonical IDs and hierarchy come from the reviewed import, never from a carrier label.
public sealed record CatalogImportName(string LanguageTag, string Name);
public sealed record CatalogImportPlace(Guid Id, string StableKey, GeographicPlaceKind PlaceKind,
    Guid? CountryPlaceId, Guid? ParentPlaceId, string? LevelCode, string CanonicalCode,
    string? ExternalStandard, string? ExternalStandardCode, IReadOnlyList<CatalogImportName> Names);
public sealed record CatalogImportProfile(Guid CountryPlaceId, string Iso2, string Iso3,
    string DefaultLanguageTag, string[] SupportedLanguageTags, string[] RequiredLevelCodesForDelivery,
    string[] RequiredLevelCodesForOperations, GeographicPostalCodePolicy PostalCodePolicy,
    string? PostalCodeRegex, bool RequiresStreetLine1, bool AllowsCoordinates,
    bool RequiresCoordinateProvenance, bool OfflinePackageEnabled);
public sealed record CatalogImportManifest(long ReleaseNumber, string ReleaseKey, string CountryScopeIso2,
    string SourceSummary, string SourceEvidenceUri, string SourceEvidenceSha256,
    IReadOnlyList<CatalogImportPlace> Places, IReadOnlyList<CatalogImportProfile> CountryProfiles);
public sealed record CatalogImportIssue(string Code, string? StableKey = null);
public sealed record CatalogImportPreviewDto(bool Succeeded, string ManifestChecksumSha256,
    int PlaceCount, int EdgeCount, IReadOnlyList<CatalogImportIssue> Issues);
public sealed record StageCatalogImportRequest(CatalogImportManifest Manifest,
    string ExpectedManifestChecksumSha256, string Reason);
public sealed record CatalogReleaseSummaryDto(Guid Id, string ReleaseKey, long ReleaseNumber,
    GeographicReleaseStatus Status, string? CountryScopeIso2, Guid CreatedByUserId, DateTime CreatedAtUtc);
public sealed record CatalogReviewDto(CatalogReleaseSummaryDto Release, string SourceSummary,
    string? SourceEvidenceUri, string? SourceEvidenceSha256, IReadOnlyList<CatalogImportPlace> Places,
    IReadOnlyList<CatalogImportProfile> CountryProfiles, CatalogValidationPreviewDto Validation);
