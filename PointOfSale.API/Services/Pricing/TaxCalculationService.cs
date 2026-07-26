using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Pricing;

public interface ITaxCalculationService
{
    Task<TaxComputationResult> CalculateAsync(
        Guid tenantId,
        decimal subtotal,
        string? countryCode,
        CancellationToken cancellationToken = default);
}

public sealed record TaxComputationResult(
    decimal TaxRate,
    decimal TaxAmount,
    string Source,
    string? CountryCode);

public sealed class TaxCalculationService : ITaxCalculationService
{
    private const int CurrencyScale = 2;
    private readonly ApplicationDbContext _dbContext;
    private readonly StorefrontPricingOptions _options;
    private readonly ILogger<TaxCalculationService> _logger;

    public TaxCalculationService(
        ApplicationDbContext dbContext,
        IOptions<StorefrontPricingOptions> options,
        ILogger<TaxCalculationService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TaxComputationResult> CalculateAsync(
        Guid tenantId,
        decimal subtotal,
        string? countryCode,
        CancellationToken cancellationToken = default)
    {
        if (subtotal <= 0m)
        {
            return new TaxComputationResult(0m, 0m, "empty-subtotal", NormalizeCountryCode(countryCode));
        }

        var normalizedCountry = NormalizeCountryCode(countryCode);
        var settings = await _dbContext.SystemSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(setting =>
                setting.TenantId == tenantId &&
                (setting.Key == "Tax.Enable" || setting.Key == "Tax.Rate"))
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);

        if (settings.TryGetValue("Tax.Enable", out var enableValue) &&
            bool.TryParse(enableValue, out var enableTax) &&
            !enableTax)
        {
            return CreateResult(0m, "tenant-disabled", normalizedCountry, subtotal);
        }

        if (settings.TryGetValue("Tax.Rate", out var configuredRate))
        {
            if (decimal.TryParse(configuredRate, NumberStyles.Number, CultureInfo.InvariantCulture, out var tenantRate))
            {
                return CreateResult(Math.Max(0m, tenantRate), "tenant-setting", normalizedCountry, subtotal);
            }

            _logger.LogWarning(
                "Ignoring invalid tenant tax rate setting | TenantId: {TenantId} | Value: {Value}",
                tenantId,
                configuredRate);
        }

        var effectiveCountry = normalizedCountry ?? await ResolveTenantCountryAsync(tenantId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(effectiveCountry) &&
            _options.CountryTaxRates.TryGetValue(effectiveCountry, out var countryRate))
        {
            return CreateResult(Math.Max(0m, countryRate), "country", effectiveCountry, subtotal);
        }

        return CreateResult(Math.Max(0m, _options.DefaultTaxRate), "default", effectiveCountry, subtotal);
    }

    private async Task<string?> ResolveTenantCountryAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenantCountry = await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Country)
            .FirstOrDefaultAsync(cancellationToken);

        return NormalizeCountryCode(tenantCountry);
    }

    private static TaxComputationResult CreateResult(decimal rate, string source, string? countryCode, decimal subtotal)
    {
        // Normalize percentage-style rates (e.g., 15) to fractional rates (e.g., 0.15)
        // This ensures consistency between config-backed and DB-backed tax rates
        var normalizedRate = rate >= 1m ? rate / 100m : rate;

        var taxAmount = normalizedRate <= 0m
            ? 0m
            : RoundCurrency(subtotal * normalizedRate);

        return new TaxComputationResult(normalizedRate, taxAmount, source, countryCode);
    }

    private static string? NormalizeCountryCode(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return null;
        }

        return CountryCodeHelper.TryNormalizeCountryCode(countryCode, out var normalizedCountry)
            ? normalizedCountry
            : countryCode.Trim().ToUpperInvariant();
    }

    private static decimal RoundCurrency(decimal value)
    {
        return Math.Round(value, CurrencyScale, MidpointRounding.AwayFromZero);
    }
}
