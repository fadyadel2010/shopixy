namespace PointOfSale.Infrastructure.Shipping;

// Only bounded identifiers/public geographic labels; no customer, phone, street or token fields.
public sealed record BostaPickupLocationOption(string Id, bool IsDefault, string? CountryCode,
    string? CityId, string? CityName, string? DistrictId, string? DistrictName)
{
    public string? CountryId { get; init; }
}
public sealed record BostaPickupLocationResult(bool Succeeded, IReadOnlyList<BostaPickupLocationOption> Locations, string? ErrorCode);
public interface IBostaPickupLocationDiscovery
{
    Task<BostaPickupLocationResult> GetPickupLocationsAsync(CarrierCredentials credentials, CancellationToken ct = default);
}
