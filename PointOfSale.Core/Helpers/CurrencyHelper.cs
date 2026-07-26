namespace PointOfSale.Core.Helpers;

/// <summary>
/// Provides currency-related utility methods for consistent rounding and formatting.
/// </summary>
public static class CurrencyHelper
{
    /// <summary>
    /// Rounds a decimal value to 2 decimal places using AwayFromZero rounding.
    /// This ensures consistent currency rounding across the application.
    /// </summary>
    /// <param name="value">The value to round.</param>
    /// <returns>The rounded value with 2 decimal places.</returns>
    public static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
