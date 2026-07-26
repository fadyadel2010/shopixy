namespace PointOfSale.API.Helpers;

/// <summary>
/// DEPRECATED: Use PointOfSale.Core.Helpers.CountryCodeHelper instead.
/// This class is kept for backward compatibility and delegates to the Core implementation.
/// 
/// Migration Status: All internal callers have been migrated to use PointOfSale.Core.Helpers.CountryCodeHelper.
/// The [Obsolete] attribute has been removed to keep builds warning-clean.
/// This wrapper can be removed in a future cleanup phase.
/// </summary>
public static class CountryCodeHelper
{
    public static bool TryNormalizeCountryCode(string? countryCode, out string normalizedCountryCode)
        => Core.Helpers.CountryCodeHelper.TryNormalizeCountryCode(countryCode, out normalizedCountryCode);

    public static string NormalizeOrDefault(string? countryCode, string? fallbackCountryCode = null)
        => Core.Helpers.CountryCodeHelper.NormalizeOrDefault(countryCode, fallbackCountryCode);

    public static string? NormalizeCountry(string? country)
        => Core.Helpers.CountryCodeHelper.NormalizeCountry(country);
}
