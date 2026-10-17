namespace PointOfSale.Infrastructure.Shipping;

public sealed record BostaPickupSelection(BostaPickupLocationOption? Location, bool UsesProviderDefault, string? ErrorCode);

public static class BostaPickupSelections
{
    public static bool ValidId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 &&
        value == value.Trim() && !value.Any(char.IsControl);

    public static BostaPickupSelection Resolve(CarrierCredentials credentials, BostaPickupLocationResult discovery)
    {
        var explicitSelection = credentials.ExtraConfig.TryGetValue("businessLocationId", out var selected);
        if (explicitSelection && !ValidId(selected)) return new(null, false, "BOSTA_BUSINESS_LOCATION_INVALID");
        if (!discovery.Succeeded) return new(null, !explicitSelection, discovery.ErrorCode ?? "BOSTA_PICKUP_VERIFICATION_REQUIRED");
        if (discovery.Locations.Any(p => !ValidId(p.Id)) || discovery.Locations.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != discovery.Locations.Count)
            return new(null, !explicitSelection, "BOSTA_PICKUP_RESPONSE_INVALID");
        var matches = discovery.Locations.Where(p => explicitSelection ? p.Id == selected : p.IsDefault).ToArray();
        if (matches.Length != 1) return new(null, !explicitSelection,
            explicitSelection ? "BOSTA_PICKUP_NOT_OWNED" : matches.Length == 0 ? "BOSTA_DEFAULT_PICKUP_MISSING" : "BOSTA_DEFAULT_PICKUP_AMBIGUOUS");
        var location = matches[0];
        return location.CountryCode == "EG" && ValidId(location.CountryId) && ValidId(location.CityId) && ValidId(location.DistrictId)
            ? new(location, !explicitSelection, null) : new(null, !explicitSelection, "BOSTA_PICKUP_GEOGRAPHY_INCOMPLETE");
    }
}
