namespace PointOfSale.Core.Financial;

/// <summary>
/// Provides authoritative ISO 4217 currency code validation.
/// Source: ISO 4217 standard (active currency codes as of 2024).
/// </summary>
public static class Iso4217CurrencyProvider
{
    /// <summary>
    /// Complete set of active ISO 4217 currency codes.
    /// Maintained from official ISO 4217 currency list.
    /// </summary>
    private static readonly HashSet<string> ActiveCurrencyCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        // A
        "AED", "AFN", "ALL", "AMD", "ANG", "AOA", "ARS", "AUD", "AWG", "AZN",
        // B
        "BAM", "BBD", "BDT", "BGN", "BHD", "BIF", "BMD", "BND", "BOB", "BOV", "BRL", "BSD", "BTN", "BWP", "BYN", "BZD",
        // C
        "CAD", "CDF", "CHE", "CHF", "CHW", "CLF", "CLP", "CNY", "COP", "COU", "CRC", "CUC", "CUP", "CVE", "CZK",
        // D
        "DJF", "DKK", "DOP", "DZD",
        // E
        "EGP", "ERN", "ETB", "EUR",
        // F
        "FJD", "FKP",
        // G
        "GBP", "GEL", "GHS", "GIP", "GMD", "GNF", "GTQ", "GYD",
        // H
        "HKD", "HNL", "HRK", "HTG", "HUF",
        // I
        "IDR", "ILS", "INR", "IQD", "IRR", "ISK",
        // J
        "JMD", "JOD", "JPY",
        // K
        "KES", "KGS", "KHR", "KMF", "KPW", "KRW", "KWD", "KYD", "KZT",
        // L
        "LAK", "LBP", "LKR", "LRD", "LSL", "LYD",
        // M
        "MAD", "MDL", "MGA", "MKD", "MMK", "MNT", "MOP", "MRU", "MUR", "MVR", "MWK", "MXN", "MXV", "MYR", "MZN",
        // N
        "NAD", "NGN", "NIO", "NOK", "NPR", "NZD",
        // O
        "OMR",
        // P
        "PAB", "PEN", "PGK", "PHP", "PKR", "PLN", "PYG",
        // Q
        "QAR",
        // R
        "RON", "RSD", "RUB", "RWF",
        // S
        "SAR", "SBD", "SCR", "SDG", "SEK", "SGD", "SHP", "SLE", "SLL", "SOS", "SPL", "SRD", "STN", "SYP", "SZL",
        // T
        "THB", "TJS", "TMT", "TND", "TOP", "TRY", "TTD", "TVD", "TWD", "TZS",
        // U
        "UAH", "UGX", "USD", "USN", "UYI", "UYU", "UYW", "UZS",
        // V
        "VED", "VES", "VND", "VUV",
        // W
        "WST",
        // X (special currencies)
        "XAF", "XAG", "XAU", "XBA", "XBB", "XBC", "XBD", "XCD", "XDR", "XOF", "XPD", "XPF", "XPT", "XSU", "XTS", "XUA", "XXX",
        // Y
        "YER",
        // Z
        "ZAR", "ZMW", "ZWL"
    };

    /// <summary>
    /// Validates if a currency code is a valid ISO 4217 currency.
    /// </summary>
    /// <param name="currencyCode">The currency code to validate (case-insensitive).</param>
    /// <returns>True if the currency code is valid, false otherwise.</returns>
    public static bool IsValidCurrency(string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return false;
        }

        return ActiveCurrencyCodes.Contains(currencyCode.Trim());
    }

    /// <summary>
    /// Gets all valid ISO 4217 currency codes.
    /// </summary>
    /// <returns>Read-only collection of valid currency codes.</returns>
    public static IReadOnlySet<string> GetAllCurrencies()
    {
        return new HashSet<string>(ActiveCurrencyCodes, StringComparer.OrdinalIgnoreCase);
    }
}
