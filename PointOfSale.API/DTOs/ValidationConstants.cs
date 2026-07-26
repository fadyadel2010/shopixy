namespace PointOfSale.API.DTOs;

/// <summary>
/// Centralized validation constants for DTO attributes
/// </summary>
public static class ValidationConstants
{
    /// <summary>
    /// Minimum decimal amount for Range validation (0.01)
    /// </summary>
    public const string DecimalAmountMin = "0.01";

    /// <summary>
    /// Maximum decimal amount for Range validation (decimal.MaxValue)
    /// </summary>
    public const string DecimalAmountMax = "79228162514264337593543950335";
}
