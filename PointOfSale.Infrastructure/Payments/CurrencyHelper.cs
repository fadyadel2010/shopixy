namespace PointOfSale.Infrastructure.Payments;

/// <summary>
/// Helper for currency-specific operations including decimal places and monetary tolerance.
/// </summary>
public static class CurrencyHelper
{
    /// <summary>
    /// Gets the number of decimal places for a given currency code.
    /// </summary>
    /// <param name="currencyCode">ISO 4217 currency code (e.g., "USD", "JPY", "BHD")</param>
    /// <returns>Number of decimal places (0, 2, or 3)</returns>
    public static int GetDecimalPlaces(string? currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return 2; // Default to 2 decimal places
        }

        var normalized = currencyCode.Trim().ToUpperInvariant();

        // Zero decimal currencies (no minor units)
        if (IsZeroDecimalCurrency(normalized))
        {
            return 0;
        }

        // Three decimal currencies
        if (IsThreeDecimalCurrency(normalized))
        {
            return 3;
        }

        // Default: two decimal currencies (most common)
        return 2;
    }

    /// <summary>
    /// Gets the monetary tolerance for a given currency based on its decimal places.
    /// The tolerance is half of one minor unit (10^(-decimalPlaces) / 2), providing a reasonable
    /// rounding tolerance while preventing payments from being a full minor unit short.
    /// </summary>
    /// <param name="currencyCode">ISO 4217 currency code (e.g., "USD", "JPY", "BHD")</param>
    /// <returns>Monetary tolerance (e.g., 0.005 for USD, 0.5 for JPY, 0.0005 for BHD)</returns>
    public static decimal GetMonetaryTolerance(string? currencyCode)
    {
        var decimalPlaces = GetDecimalPlaces(currencyCode);
        return (decimal)Math.Pow(10, -decimalPlaces) / 2M;
    }

    /// <summary>
    /// Checks if a currency uses zero decimal places (no minor units).
    /// </summary>
    private static bool IsZeroDecimalCurrency(string normalizedCurrencyCode)
    {
        return normalizedCurrencyCode switch
        {
            "BIF" => true, // Burundian Franc
            "CLP" => true, // Chilean Peso
            "DJF" => true, // Djiboutian Franc
            "GNF" => true, // Guinean Franc
            "ISK" => true, // Icelandic Króna
            "JPY" => true, // Japanese Yen
            "KMF" => true, // Comorian Franc
            "KRW" => true, // South Korean Won
            "PYG" => true, // Paraguayan Guaraní
            "RWF" => true, // Rwandan Franc
            "UGX" => true, // Ugandan Shilling
            "VND" => true, // Vietnamese Dong
            "VUV" => true, // Vanuatu Vatu
            "XAF" => true, // Central African CFA Franc
            "XOF" => true, // West African CFA Franc
            "XPF" => true, // CFP Franc
            _ => false
        };
    }

    /// <summary>
    /// Checks if a currency uses three decimal places.
    /// </summary>
    private static bool IsThreeDecimalCurrency(string normalizedCurrencyCode)
    {
        return normalizedCurrencyCode switch
        {
            "BHD" => true, // Bahraini Dinar
            "IQD" => true, // Iraqi Dinar
            "JOD" => true, // Jordanian Dinar
            "KWD" => true, // Kuwaiti Dinar
            "LYD" => true, // Libyan Dinar
            "OMR" => true, // Omani Rial
            "TND" => true, // Tunisian Dinar
            _ => false
        };
    }
}
