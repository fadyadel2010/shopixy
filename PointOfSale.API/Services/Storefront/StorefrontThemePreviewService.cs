using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

public interface IStorefrontThemePreviewService
{
    Task<StorefrontThemePreviewConfigResult> GetConfigAsync(
        Guid tenantId,
        string themeId,
        CancellationToken cancellationToken = default);
}

public sealed class StorefrontThemePreviewConfigResult
{
    public bool Found { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public StorefrontStoreConfigDto? Config { get; init; }
}

public sealed class StorefrontThemePreviewService : IStorefrontThemePreviewService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<StorefrontThemePreviewService> _logger;

    public StorefrontThemePreviewService(
        ApplicationDbContext dbContext,
        ILogger<StorefrontThemePreviewService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<StorefrontThemePreviewConfigResult> GetConfigAsync(
        Guid tenantId,
        string themeId,
        CancellationToken cancellationToken = default)
    {
        var authority = await _dbContext.StoreConfigs
            .AsNoTracking()
            .Where(config => config.TenantId == tenantId)
            .Join(
                _dbContext.Tenants.AsNoTracking(),
                config => config.TenantId,
                tenant => tenant.Id,
                (config, tenant) => new { Config = config, Tenant = tenant })
            .Where(authority =>
                authority.Tenant.IsActive &&
                authority.Tenant.Status == TenantStatus.Active &&
                authority.Tenant.DeletedAtUtc == null)
            .SingleOrDefaultAsync(cancellationToken);

        if (authority == null)
        {
            return new StorefrontThemePreviewConfigResult
            {
                Found = false,
                ErrorCode = "STOREFRONT_CONFIG_NOT_FOUND",
                ErrorMessage = "Store configuration was not found."
            };
        }

        if (string.IsNullOrWhiteSpace(authority.Tenant.Currency))
        {
            return new StorefrontThemePreviewConfigResult
            {
                Found = false,
                ErrorCode = "STOREFRONT_CURRENCY_NOT_CONFIGURED",
                ErrorMessage = "Store currency is not configured."
            };
        }

        var preset = await _dbContext.ThemePresets
            .AsNoTracking()
            .Where(item => item.Key == authority.Config.ThemePresetKey || item.Key == "default")
            .OrderByDescending(item => item.Key == authority.Config.ThemePresetKey)
            .FirstOrDefaultAsync(cancellationToken);

        var config = StorefrontDraftConfigMapper.Map(
            authority.Config,
            preset,
            authority.Tenant.Currency.Trim().ToUpperInvariant(),
            CountryCodeHelper.NormalizeOrDefault(authority.Tenant.Country),
            (exception, operation) => _logger.LogWarning(
                exception,
                "Failed to map theme preview storefront configuration | TenantId: {TenantId} | Operation: {Operation}",
                tenantId,
                operation),
            storefrontThemeOverride: themeId);

        return new StorefrontThemePreviewConfigResult
        {
            Found = true,
            Config = config
        };
    }
}
