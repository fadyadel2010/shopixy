using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.Services.Pricing;

public sealed class StorefrontPricingOptions
{
    public const string SectionName = "StorefrontPricing";

    [Range(0.0, 1.0, ErrorMessage = "DefaultTaxRate must be between 0 and 1 (e.g., 0.15 for 15%)")]
    public decimal DefaultTaxRate { get; set; } = 0m;

    public Dictionary<string, decimal> CountryTaxRates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes percentage-style inputs (e.g., 8 or 15) to fractional rates (e.g., 0.08 or 0.15).
    /// This method modifies the options in place.
    /// </summary>
    public void NormalizeTaxRates()
    {
        if (DefaultTaxRate > 1m)
        {
            DefaultTaxRate /= 100m;
        }

        var countriesToNormalize = CountryTaxRates
            .Where(kvp => kvp.Value > 1m)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var country in countriesToNormalize)
        {
            CountryTaxRates[country] /= 100m;
        }
    }

    /// <summary>
    /// Validates that all country tax rates are within valid range and use ISO alpha-2 codes.
    /// </summary>
    public bool ValidateCountryTaxRates(out string? errorMessage)
    {
        foreach (var (country, rate) in CountryTaxRates)
        {
            if (string.IsNullOrWhiteSpace(country))
            {
                errorMessage = "Country code cannot be empty in CountryTaxRates";
                return false;
            }

            // Validate ISO alpha-2 format: exactly 2 letters
            if (country.Length != 2 || !country.All(char.IsLetter))
            {
                errorMessage = $"Country code '{country}' must be ISO alpha-2 (e.g., 'US', 'SA', 'GB')";
                return false;
            }

            if (rate < 0m || rate > 1m)
            {
                errorMessage = $"Tax rate for country '{country}' must be between 0 and 1 (e.g., 0.15 for 15%), got {rate}";
                return false;
            }
        }

        errorMessage = null;
        return true;
    }
}
