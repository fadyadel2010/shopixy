namespace PointOfSale.Core.Helpers;

/// <summary>
/// Shared ISO 3166-1 alpha-2 country code normalization.
/// Keeps country handling consistent across checkout, shipping, and payment flows.
/// </summary>
public static class CountryCodeHelper
{
    private const string SafeDefaultCountryCode = "EG";

    /// <summary>
    /// Valid ISO 3166-1 alpha-2 country codes.
    /// Comprehensive list of all officially assigned two-letter country codes.
    /// </summary>
    private static readonly HashSet<string> ValidIsoCodes = new(StringComparer.Ordinal)
    {
        "AD", "AE", "AF", "AG", "AI", "AL", "AM", "AO", "AQ", "AR", "AS", "AT", "AU", "AW", "AX", "AZ",
        "BA", "BB", "BD", "BE", "BF", "BG", "BH", "BI", "BJ", "BL", "BM", "BN", "BO", "BQ", "BR", "BS", "BT", "BV", "BW", "BY", "BZ",
        "CA", "CC", "CD", "CF", "CG", "CH", "CI", "CK", "CL", "CM", "CN", "CO", "CR", "CU", "CV", "CW", "CX", "CY", "CZ",
        "DE", "DJ", "DK", "DM", "DO", "DZ",
        "EC", "EE", "EG", "EH", "ER", "ES", "ET",
        "FI", "FJ", "FK", "FM", "FO", "FR",
        "GA", "GB", "GD", "GE", "GF", "GG", "GH", "GI", "GL", "GM", "GN", "GP", "GQ", "GR", "GS", "GT", "GU", "GW", "GY",
        "HK", "HM", "HN", "HR", "HT", "HU",
        "ID", "IE", "IL", "IM", "IN", "IO", "IQ", "IR", "IS", "IT",
        "JE", "JM", "JO", "JP",
        "KE", "KG", "KH", "KI", "KM", "KN", "KP", "KR", "KW", "KY", "KZ",
        "LA", "LB", "LC", "LI", "LK", "LR", "LS", "LT", "LU", "LV", "LY",
        "MA", "MC", "MD", "ME", "MF", "MG", "MH", "MK", "ML", "MM", "MN", "MO", "MP", "MQ", "MR", "MS", "MT", "MU", "MV", "MW", "MX", "MY", "MZ",
        "NA", "NC", "NE", "NF", "NG", "NI", "NL", "NO", "NP", "NR", "NU", "NZ",
        "OM",
        "PA", "PE", "PF", "PG", "PH", "PK", "PL", "PM", "PN", "PR", "PS", "PT", "PW", "PY",
        "QA",
        "RE", "RO", "RS", "RU", "RW",
        "SA", "SB", "SC", "SD", "SE", "SG", "SH", "SI", "SJ", "SK", "SL", "SM", "SN", "SO", "SR", "SS", "ST", "SV", "SX", "SY", "SZ",
        "TC", "TD", "TF", "TG", "TH", "TJ", "TK", "TL", "TM", "TN", "TO", "TR", "TT", "TV", "TW", "TZ",
        "UA", "UG", "UM", "US", "UY", "UZ",
        "VA", "VC", "VE", "VG", "VI", "VN", "VU",
        "WF", "WS",
        "YE", "YT",
        "ZA", "ZM", "ZW"
    };

    /// <summary>
    /// Common country name to ISO 3166-1 alpha-2 code mappings.
    /// Supports the most frequently used country names in e-commerce.
    /// </summary>
    private static readonly Dictionary<string, string> CountryNameToCodeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Middle East & North Africa
        { "Egypt", "EG" },
        { "Saudi Arabia", "SA" },
        { "United Arab Emirates", "AE" },
        { "UAE", "AE" },
        { "Kuwait", "KW" },
        { "Qatar", "QA" },
        { "Bahrain", "BH" },
        { "Oman", "OM" },
        { "Jordan", "JO" },
        { "Lebanon", "LB" },
        { "Iraq", "IQ" },
        { "Palestine", "PS" },
        { "Morocco", "MA" },
        { "Tunisia", "TN" },
        { "Algeria", "DZ" },
        { "Libya", "LY" },
        
        // Major Markets
        { "United States", "US" },
        { "United States of America", "US" },
        { "USA", "US" },
        { "United Kingdom", "GB" },
        { "UK", "GB" },
        { "Canada", "CA" },
        { "Australia", "AU" },
        { "Germany", "DE" },
        { "France", "FR" },
        { "Italy", "IT" },
        { "Spain", "ES" },
        { "Netherlands", "NL" },
        { "Belgium", "BE" },
        { "Switzerland", "CH" },
        { "Austria", "AT" },
        { "Sweden", "SE" },
        { "Norway", "NO" },
        { "Denmark", "DK" },
        { "Finland", "FI" },
        { "Poland", "PL" },
        { "Turkey", "TR" },
        
        // Asia Pacific
        { "China", "CN" },
        { "Japan", "JP" },
        { "South Korea", "KR" },
        { "India", "IN" },
        { "Singapore", "SG" },
        { "Malaysia", "MY" },
        { "Thailand", "TH" },
        { "Indonesia", "ID" },
        { "Philippines", "PH" },
        { "Vietnam", "VN" },
        { "Pakistan", "PK" },
        { "Bangladesh", "BD" },
        
        // Latin America
        { "Brazil", "BR" },
        { "Mexico", "MX" },
        { "Argentina", "AR" },
        { "Chile", "CL" },
        { "Colombia", "CO" },
        { "Peru", "PE" },
        
        // Africa
        { "South Africa", "ZA" },
        { "Nigeria", "NG" },
        { "Kenya", "KE" },
        { "Ghana", "GH" },
        { "Ethiopia", "ET" }
    };

    public static bool TryNormalizeCountryCode(string? countryCode, out string normalizedCountryCode)
    {
        normalizedCountryCode = string.Empty;

        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return false;
        }

        var trimmedCountryCode = countryCode.Trim();
        
        // First, check if it's a valid 2-letter ISO code
        if (trimmedCountryCode.Length == 2)
        {
            var normalized = trimmedCountryCode.ToUpperInvariant();
            if (ValidIsoCodes.Contains(normalized))
            {
                normalizedCountryCode = normalized;
                return true;
            }
        }

        // Second, try to map from full country name to ISO code
        if (CountryNameToCodeMap.TryGetValue(trimmedCountryCode, out var mappedCode))
        {
            normalizedCountryCode = mappedCode;
            return true;
        }

        return false;
    }

    public static string NormalizeOrDefault(string? countryCode, string? fallbackCountryCode = null)
    {
        if (TryNormalizeCountryCode(countryCode, out var normalizedCountryCode))
        {
            return normalizedCountryCode;
        }

        if (TryNormalizeCountryCode(fallbackCountryCode, out normalizedCountryCode))
        {
            return normalizedCountryCode;
        }

        return SafeDefaultCountryCode;
    }

    /// <summary>
    /// Normalizes country input to ISO 3166-1 alpha-2 format.
    /// Returns null for empty input or throws for invalid/unknown values.
    /// </summary>
    /// <param name="country">Country code or full name</param>
    /// <returns>ISO 3166-1 alpha-2 code or null if input is empty</returns>
    /// <exception cref="ArgumentException">Thrown when country value is invalid or unknown</exception>
    public static string? NormalizeCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return null;
        }

        if (TryNormalizeCountryCode(country, out var normalizedCode))
        {
            return normalizedCode;
        }

        throw new ArgumentException(
            $"Invalid or unknown country value: '{country}'. Expected ISO 3166-1 alpha-2 code (e.g., 'EG', 'US') or recognized country name (e.g., 'Egypt', 'United States').",
            nameof(country));
    }
}
