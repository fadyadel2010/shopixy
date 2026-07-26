using PointOfSale.Core.Financial;

namespace PointOfSale.Infrastructure.Utilities;

/// <summary>
/// Centralized currency normalization and validation utility.
/// Ensures consistent currency handling across all projection consumers.
/// </summary>
public static class CurrencyNormalizationHelper
{
    /// <summary>
    /// Normalizes a currency code to uppercase and trims whitespace.
    /// Throws an exception if the currency is null or whitespace.
    /// </summary>
    /// <param name="currency">The currency code to normalize</param>
    /// <param name="context">Context information for error messages (e.g., "TenantId: {tenantId}, ProductId: {productId}")</param>
    /// <param name="validateIso">If true, validates that the currency is a valid ISO 4217 code</param>
    /// <returns>Normalized currency code in uppercase</returns>
    /// <exception cref="InvalidOperationException">Thrown when currency is null, whitespace, or invalid ISO 4217 code (if validateIso is true)</exception>
    public static string NormalizeCurrencyOrThrow(string? currency, string context, bool validateIso = false)
        => FinancialCurrencyGuard.RequireCurrency(currency, "currency", context, validateIso);

    /// <summary>
    /// Silent default currency injection is forbidden in production flows.
    /// </summary>
    /// <exception cref="InvalidOperationException">Always thrown because silent defaults are no longer allowed.</exception>
    [Obsolete("Silent currency fallback is forbidden. Use NormalizeCurrencyOrThrow with an explicit currency instead.", error: true)]
    public static string NormalizeCurrencyOrDefault(string? currency, bool validateIso = false, string? context = null)
        => throw new InvalidOperationException(
            $"Silent currency fallback is forbidden. Use {nameof(NormalizeCurrencyOrThrow)} with an explicit currency. Context: {context ?? "unknown"}");
}
