using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Interfaces.Geographic;

namespace PointOfSale.API.DTOs;

public sealed class WarehouseAddressAdmissionRequest
{
    [Required, StringLength(300)]
    public string StreetLine1 { get; set; } = string.Empty;
    [StringLength(32)]
    public string? PostalCode { get; set; }
    public Guid CountryPlaceId { get; set; }
    public Guid AdministrativeAreaPlaceId { get; set; }
    public Guid LocalityPlaceId { get; set; }
    public Guid? DistrictPlaceId { get; set; }
    public Guid? ZonePlaceId { get; set; }
    [Required, StringLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

public sealed record WarehouseAddressAdmissionResponse(Guid WarehouseId, Guid AddressAdmissionEvidenceId,
    Guid CatalogReleaseId, GeographicPlaceLineage Lineage, string? PostalCode, string AddressFingerprintSha256);

public sealed record WarehouseAddressPlacesResponse(Guid CatalogReleaseId,
    IReadOnlyList<WarehouseAddressPlaceOption> Places);

public sealed record WarehouseAddressPlaceOption(Guid PlaceId, GeographicPlaceKind PlaceKind,
    string CanonicalCode, string DisplayName, string? CountryIso2, GeographicPostalCodePolicy? PostalCodePolicy,
    string? PostalCodeRegex, string[]? RequiredLevelCodesForDelivery, string[]? RequiredLevelCodesForOperations);
