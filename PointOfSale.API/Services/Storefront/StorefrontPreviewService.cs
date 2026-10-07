using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Service for handling draft preview mode in storefront.
/// Phase: Storefront Admin Hardening - P3
/// 
/// CRITICAL: This service reads directly from PostgreSQL (draft data).
/// It must NEVER write data or be used for live traffic.
/// </summary>
public interface IStorefrontPreviewService
{
    /// <summary>
    /// Gets a CMS page draft for preview.
    /// </summary>
    Task<StorefrontPreviewPageResult> GetPagePreviewAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the store configuration draft for preview.
    /// </summary>
    Task<StorefrontPreviewConfigResult> GetConfigPreviewAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<StorefrontPreviewConfigResult> GetConfigPreviewAsync(
        Guid tenantId, CancellationToken cancellationToken, string storefrontThemeOverride);

    /// <summary>
    /// Gets the navigation draft for preview.
    /// </summary>
    Task<StorefrontPreviewNavigationResult> GetNavigationPreviewAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

public class StorefrontPreviewPageResult
{
    public bool Found { get; set; }
    public CmsPageDto? Page { get; set; }
    public string? ErrorMessage { get; set; }
}

public class StorefrontPreviewConfigResult
{
    public bool Found { get; set; }
    public StorefrontStoreConfigDto? Config { get; set; }
    public string? ErrorMessage { get; set; }
}

public class StorefrontPreviewNavigationResult
{
    public bool Found { get; set; }
    public object? Navigation { get; set; }
    public string? ErrorMessage { get; set; }
}

public class StorefrontPreviewService : IStorefrontPreviewService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICmsPageReadService _cmsPageReadService;
    private readonly ILogger<StorefrontPreviewService> _logger;
    private readonly IR2StorageService? _storage;
    private readonly IStorefrontFooterReadService _footer;

    public StorefrontPreviewService(
        ApplicationDbContext dbContext,
        ICmsPageReadService cmsPageReadService,
        ILogger<StorefrontPreviewService> logger,
        IR2StorageService? storage = null,
        IStorefrontFooterReadService? footer = null)
    {
        _dbContext = dbContext;
        _cmsPageReadService = cmsPageReadService;
        _logger = logger;
        _storage = storage;
        _footer = footer ?? new StorefrontFooterReadService(dbContext,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StorefrontFooterReadService>.Instance);
    }

    public async Task<StorefrontPreviewPageResult> GetPagePreviewAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "GetPagePreview (DRAFT) | TenantId: {TenantId} | Slug: {Slug}",
            tenantId, slug);

        try
        {
            var page = await _cmsPageReadService.GetPreviewPageAsync(tenantId, slug, cancellationToken);

            if (page == null)
            {
                return new StorefrontPreviewPageResult
                {
                    Found = false,
                    ErrorMessage = $"Page '{slug}' not found in drafts"
                };
            }

            return new StorefrontPreviewPageResult
            {
                Found = true,
                Page = page
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetPagePreview failed | TenantId: {TenantId} | Slug: {Slug}", tenantId, slug);
            return new StorefrontPreviewPageResult
            {
                Found = false,
                ErrorMessage = "Failed to load preview"
            };
        }
    }

    public Task<StorefrontPreviewConfigResult> GetConfigPreviewAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => GetConfigPreviewCoreAsync(tenantId, cancellationToken, null);

    public Task<StorefrontPreviewConfigResult> GetConfigPreviewAsync(
        Guid tenantId, CancellationToken cancellationToken, string storefrontThemeOverride)
        => GetConfigPreviewCoreAsync(tenantId, cancellationToken, storefrontThemeOverride);

    private async Task<StorefrontPreviewConfigResult> GetConfigPreviewCoreAsync(
        Guid tenantId, CancellationToken cancellationToken, string? storefrontThemeOverride)
    {
        _logger.LogInformation("GetConfigPreview (DRAFT) | TenantId: {TenantId}", tenantId);

        try
        {
            var config = await _dbContext.StoreConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

            if (config == null)
            {
                return new StorefrontPreviewConfigResult
                {
                    Found = false,
                    ErrorMessage = "Store configuration not found"
                };
            }

            var tenant = await _dbContext.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => new
                {
                    t.Currency,
                    t.Country
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (tenant == null)
            {
                return new StorefrontPreviewConfigResult
                {
                    Found = false,
                    ErrorMessage = "Tenant not found"
                };
            }

            var presetKey = string.IsNullOrWhiteSpace(config.ThemePresetKey) ? "default" : config.ThemePresetKey;
            var preset = await _dbContext.ThemePresets
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Key == presetKey, cancellationToken);

            if (preset == null && !string.Equals(presetKey, "default", StringComparison.OrdinalIgnoreCase))
            {
                preset = await _dbContext.ThemePresets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Key == "default", cancellationToken);
            }

            var previewConfig = StorefrontDraftConfigMapper.Map(
                config,
                preset,
                currency: tenant.Currency ?? string.Empty,
                defaultCountryCode: CountryCodeHelper.NormalizeOrDefault(tenant.Country),
                storefrontThemeOverride: storefrontThemeOverride);

            previewConfig.PreviewChrome = new StorefrontPreviewChromeDto
            {
                Navigation = ResolveDraftNavigation(config.CustomizationJson, config.UpdatedAtUtc),
                // Footer content has its own existing immediate/atomic save boundary, not
                // a StoreConfig draft branch. Reuse its SQL read resolver in both locales.
                FooterByLanguage = new()
                {
                    ["en"] = await _footer.GetFooterAsync(tenantId, "en", cancellationToken),
                    ["ar"] = await _footer.GetFooterAsync(tenantId, "ar", cancellationToken)
                }
            };

            if (_storage != null)
            {
                foreach (var section in previewConfig.Pages.Home.Sections)
                {
                    foreach (var key in section.Settings.Keys.ToArray())
                    {
                        var value = section.Settings[key]?.ToString();
                        if (!HomepageMediaReferences.TryRead(value, out var id)) continue;
                        var asset = await _dbContext.HomepageMediaAssets.AsNoTracking()
                            .SingleOrDefaultAsync(asset => asset.Id == id && asset.TenantId == tenantId, cancellationToken);
                        section.Settings[key] = asset == null ? "" : await _storage.CreateDownloadUrlAsync(asset.ObjectKey, TimeSpan.FromMinutes(5), cancellationToken);
                    }
                }
            }

            return new StorefrontPreviewConfigResult
            {
                Found = true,
                Config = previewConfig
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetConfigPreview failed | TenantId: {TenantId}", tenantId);
            return new StorefrontPreviewConfigResult
            {
                Found = false,
                ErrorMessage = "Failed to load preview"
            };
        }
    }

    public async Task<StorefrontPreviewNavigationResult> GetNavigationPreviewAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetNavigationPreview (DRAFT) | TenantId: {TenantId}", tenantId);

        try
        {
            var config = await _dbContext.StoreConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

            if (config == null)
            {
                return new StorefrontPreviewNavigationResult
                {
                    Found = false,
                    ErrorMessage = "Store configuration not found"
                };
            }

            var navigation = ExtractDraftNavigation(config.CustomizationJson);

            return new StorefrontPreviewNavigationResult
            {
                Found = true,
                Navigation = new
                {
                    PreviewMode = true,
                    Header = navigation?.Header ?? new { Links = new List<object>() },
                    Footer = navigation?.Footer ?? new { Links = new List<object>() }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetNavigationPreview failed | TenantId: {TenantId}", tenantId);
            return new StorefrontPreviewNavigationResult
            {
                Found = false,
                ErrorMessage = "Failed to load preview"
            };
        }
    }

    private (object? Header, object? Footer)? ExtractDraftNavigation(string? customizationJson)
    {
        if (string.IsNullOrWhiteSpace(customizationJson))
            return null;

        try
        {
            var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(customizationJson);
            if (root == null || !root.TryGetValue("navigation", out var navElement))
                return null;

            var nav = navElement.Deserialize<Dictionary<string, JsonElement>>();
            if (nav == null)
                return null;

            if (nav.TryGetValue("draft", out var draftElement))
            {
                var content = draftElement.Deserialize<Dictionary<string, object>>();
                if (content == null)
                {
                    return null;
                }

                return (
                    content.TryGetValue("header", out var header) ? header : null,
                    content.TryGetValue("footer", out var footer) ? footer : null
                );
            }

            // Legacy flat navigation is allowed only when draft/live wrappers do not exist yet.
            if (!nav.ContainsKey("live"))
            {
                var content = nav.ToDictionary(pair => pair.Key, pair => (object)pair.Value);
                return (
                    content.TryGetValue("header", out var header) ? header : null,
                    content.TryGetValue("footer", out var footer) ? footer : null
                );
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract draft navigation");
            return null;
        }
    }

    internal static NavigationDto ResolveDraftNavigation(string? json, DateTime updatedAt)
    {
        var result = new NavigationDto { UpdatedAtUtc = updatedAt };
        if (string.IsNullOrWhiteSpace(json)) return result;
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("navigation", out var navigation)) return result;
        // An explicitly empty draft stays empty. Only absence of a draft uses live.
        var content = navigation.TryGetProperty("draft", out var draft) ? draft
            : navigation.TryGetProperty("live", out var live) ? live : navigation;
        if (content.ValueKind != JsonValueKind.Object) return result;
        result.Header = Controllers.StorefrontNavigationController.ExtractMenu(content, "header");
        result.Footer = Controllers.StorefrontNavigationController.ExtractMenu(content, "footer");
        return result;
    }

}
