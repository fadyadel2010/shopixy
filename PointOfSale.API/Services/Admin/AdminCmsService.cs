using System.Text.Json;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Catalog;
using PointOfSale.API.Services.Cms;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.Admin;

public sealed class AdminCmsService : IAdminCmsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICmsPageService _cmsPageService;
    private readonly ICmsPageReadService _cmsPageReadService;
    private readonly ICmsPagePreviewTokenService _cmsPagePreviewTokenService;
    private readonly IStoreConfigCacheService _cacheService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<AdminCmsService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public AdminCmsService(
        ApplicationDbContext dbContext,
        ICmsPageService cmsPageService,
        ICmsPageReadService cmsPageReadService,
        ICmsPagePreviewTokenService cmsPagePreviewTokenService,
        IStoreConfigCacheService cacheService,
        IEventPublisher eventPublisher,
        ILogger<AdminCmsService> logger)
    {
        _dbContext = dbContext;
        _cmsPageService = cmsPageService;
        _cmsPageReadService = cmsPageReadService;
        _cmsPagePreviewTokenService = cmsPagePreviewTokenService;
        _cacheService = cacheService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public Task<AdminCmsPageResponse?> GetCmsPageAsync(
        Guid tenantId,
        Guid pageId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "GetCmsPage | TenantId: {TenantId} | PageId: {PageId} | TraceId: {TraceId}",
            tenantId,
            pageId,
            traceId);

        return _cmsPageService.GetPageAsync(tenantId, pageId, cancellationToken);
    }

    public Task<AdminCmsPageResponse?> GetCmsPageBySlugAsync(
        Guid tenantId,
        string slug,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "GetCmsPageBySlug | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
            tenantId,
            slug,
            traceId);

        return _cmsPageService.GetPageBySlugAsync(tenantId, slug, cancellationToken);
    }

    public Task<IReadOnlyList<AdminCmsPageListItemResponse>> ListCmsPagesAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "ListCmsPages | TenantId: {TenantId} | TraceId: {TraceId}",
            tenantId,
            traceId);

        return _cmsPageService.ListPagesAsync(tenantId, cancellationToken);
    }

    public Task<AdminCmsPageVersionHistoryResponse?> GetCmsPageVersionHistoryAsync(
        Guid tenantId,
        Guid pageId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "GetCmsPageVersionHistory | TenantId: {TenantId} | PageId: {PageId} | TraceId: {TraceId}",
            tenantId,
            pageId,
            traceId);

        return _cmsPageService.GetVersionHistoryAsync(tenantId, pageId, cancellationToken);
    }

    public Task<AdminCmsPageResponse> CreateCmsPageAsync(
        Guid tenantId,
        AdminCmsPageRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "CreateCmsPage | TenantId: {TenantId} | Slug: {Slug} | TraceId: {TraceId}",
            tenantId,
            request.Slug,
            traceId);

        return _cmsPageService.CreatePageAsync(tenantId, request, cancellationToken);
    }

    public Task<AdminCmsPageResponse> UpdateCmsPageAsync(
        Guid tenantId,
        Guid pageId,
        AdminCmsPageRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "UpdateCmsPage | TenantId: {TenantId} | PageId: {PageId} | TraceId: {TraceId}",
            tenantId,
            pageId,
            traceId);

        return _cmsPageService.UpdatePageAsync(tenantId, pageId, request, cancellationToken);
    }

    public Task<AdminCmsPageResponse> PublishCmsPageAsync(
        Guid tenantId,
        Guid pageId,
        int expectedVersion,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "PublishCmsPage | TenantId: {TenantId} | PageId: {PageId} | TraceId: {TraceId}",
            tenantId,
            pageId,
            traceId);

        return _cmsPageService.PublishPageAsync(tenantId, pageId, expectedVersion, cancellationToken);
    }

    public Task<AdminCmsPageResponse> RollbackCmsPageAsync(
        Guid tenantId,
        Guid pageId,
        int rollbackVersion,
        int expectedVersion,
        string? reason,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "RollbackCmsPage | TenantId: {TenantId} | PageId: {PageId} | RollbackVersion: {RollbackVersion} | TraceId: {TraceId}",
            tenantId,
            pageId,
            rollbackVersion,
            traceId);

        return _cmsPageService.RollbackPageAsync(
            tenantId,
            pageId,
            rollbackVersion,
            expectedVersion,
            reason,
            cancellationToken);
    }

    public async Task<AdminCmsPageResponse?> GetCmsPagePreviewAsync(
        Guid tenantId,
        Guid pageId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "GetCmsPagePreview | TenantId: {TenantId} | PageId: {PageId} | TraceId: {TraceId}",
            tenantId,
            pageId,
            traceId);

        var page = await _cmsPageService.GetPageAsync(tenantId, pageId, cancellationToken);
        if (page == null)
        {
            return null;
        }

        var previewDto = await _cmsPageReadService.GetPreviewPageByIdAsync(
            tenantId,
            pageId,
            pageVersion: null,
            cancellationToken);
        if (previewDto == null)
        {
            return page;
        }

        page.Draft ??= new AdminCmsPageVersionResponse
        {
            RecordId = Guid.Empty,
            Slug = previewDto.Slug,
            Title = previewDto.Title,
            ContentType = previewDto.ContentType,
            Content = previewDto.Content,
            Version = page.Published?.Version ?? 1,
            SchemaVersion = page.Published?.SchemaVersion ?? 1,
            CreatedAtUtc = page.UpdatedAtUtc,
            UpdatedAtUtc = previewDto.UpdatedAtUtc,
            PublishedAtUtc = previewDto.PublishedAtUtc,
            Seo = new AdminCmsPageSeoRequest
            {
                MetaTitle = previewDto.Seo.MetaTitle,
                MetaDescription = previewDto.Seo.MetaDescription,
                MetaKeywords = previewDto.Seo.MetaKeywords,
                OpenGraphImageUrl = previewDto.Seo.OpenGraphImageUrl
            }
        };

        return page;
    }

    public async Task<AdminCmsPreviewAccessResponse?> CreateCmsPagePreviewAccessAsync(
        Guid tenantId,
        Guid pageId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "CreateCmsPagePreviewAccess | TenantId: {TenantId} | PageId: {PageId} | TraceId: {TraceId}",
            tenantId,
            pageId,
            traceId);

        var page = await _cmsPageService.GetPageAsync(tenantId, pageId, cancellationToken);
        if (page == null)
        {
            return null;
        }

        var version = page.Draft ?? page.Published;
        if (version == null)
        {
            return null;
        }

        var previewToken = _cmsPagePreviewTokenService.CreatePreviewToken(
            tenantId,
            page.Id,
            version.Slug,
            version.Version);

        return new AdminCmsPreviewAccessResponse
        {
            Slug = version.Slug,
            PreviewToken = previewToken.Token,
            ExpiresAtUtc = previewToken.ExpiresAtUtc
        };
    }

    public Task DeleteCmsPageAsync(
        Guid tenantId,
        Guid pageId,
        int expectedVersion,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "DeleteCmsPage | TenantId: {TenantId} | PageId: {PageId} | TraceId: {TraceId}",
            tenantId,
            pageId,
            traceId);

        return _cmsPageService.DeletePageAsync(tenantId, pageId, expectedVersion, cancellationToken);
    }

    public async Task<AdminNavigationResponse> GetNavigationAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "GetNavigation | TenantId: {TenantId} | TraceId: {TraceId}",
            tenantId,
            traceId);

        var config = await _dbContext.StoreConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);

        return config == null
            ? GetDefaultNavigation()
            : ExtractNavigationFromConfig(config.CustomizationJson, config.RowVersion);
    }

    public async Task<AdminNavigationResponse> UpdateNavigationAsync(
        Guid tenantId,
        AdminNavigationRequest request,
        uint expectedRowVersion,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "UpdateNavigation | TenantId: {TenantId} | TraceId: {TraceId}",
            tenantId,
            traceId);

        var config = await _dbContext.StoreConfigs
            .FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);

        if (config == null)
        {
            throw new InvalidOperationException("Store config not found");
        }

        ApplyExpectedRowVersion(config, expectedRowVersion);
        ValidateNavigationShape(request);

        var existingCmsSlugs = await GetExistingCmsPageSlugsAsync(tenantId, cancellationToken);
        var liveCategorySlugs = await GetLiveCategorySlugsAsync(tenantId, cancellationToken);
        ValidateNavigationMenu(request.Header, existingCmsSlugs, liveCategorySlugs, requirePublishedCms: false);
        ValidateNavigationMenu(request.Footer, existingCmsSlugs, liveCategorySlugs, requirePublishedCms: false);

        var root = ParseCustomizationJson(config.CustomizationJson);
        var navigation = root.TryGetValue("navigation", out var navigationObj)
            ? ConvertToDictionary(navigationObj)
            : new Dictionary<string, object>();

        navigation["draft"] = new Dictionary<string, object>
        {
            ["header"] = new Dictionary<string, object>
            {
                ["links"] = request.Header.Select(MapNavigationLink).ToList()
            },
            ["footer"] = new Dictionary<string, object>
            {
                ["links"] = request.Footer.Select(MapNavigationLink).ToList()
            }
        };

        if (!navigation.ContainsKey("live"))
        {
            navigation["live"] = new Dictionary<string, object>
            {
                ["header"] = new Dictionary<string, object> { ["links"] = new List<object>() },
                ["footer"] = new Dictionary<string, object> { ["links"] = new List<object>() }
            };
        }

        root["navigation"] = navigation;
        config.CustomizationJson = JsonSerializer.Serialize(root, JsonOptions);
        config.UpdatedAtUtc = DateTime.UtcNow;

        await SaveStoreConfigChangesAsync(config, cancellationToken);

        return ExtractNavigationFromConfig(config.CustomizationJson, config.RowVersion);
    }

    public async Task<AdminNavigationResponse> PublishNavigationAsync(
        Guid tenantId,
        uint expectedRowVersion,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "PublishNavigation | TenantId: {TenantId} | TraceId: {TraceId}",
            tenantId,
            traceId);

        var config = await _dbContext.StoreConfigs
            .FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);

        if (config == null)
        {
            throw new InvalidOperationException("Store config not found");
        }

        ApplyExpectedRowVersion(config, expectedRowVersion);

        var root = ParseCustomizationJson(config.CustomizationJson);
        if (!root.TryGetValue("navigation", out var navigationObj))
        {
            throw new InvalidOperationException("No navigation configuration found");
        }

        var navigation = ConvertToDictionary(navigationObj);
        if (!navigation.TryGetValue("draft", out var draftObj))
        {
            throw new InvalidOperationException("No draft navigation to publish");
        }

        var draftElement = JsonSerializer.SerializeToElement(draftObj, JsonOptions);
        var draft = ExtractNavigationConfig(draftElement);
        ValidateNavigationShape(new AdminNavigationRequest
        {
            Header = draft.Header,
            Footer = draft.Footer
        });
        var publishedCmsSlugs = await GetPublishedCmsPageSlugsAsync(tenantId, cancellationToken);
        var liveCategorySlugs = await GetLiveCategorySlugsAsync(tenantId, cancellationToken);
        ValidateNavigationMenu(draft.Header, publishedCmsSlugs, liveCategorySlugs, requirePublishedCms: true);
        ValidateNavigationMenu(draft.Footer, publishedCmsSlugs, liveCategorySlugs, requirePublishedCms: true);

        var draftJson = JsonSerializer.Serialize(draftObj, JsonOptions);
        navigation["live"] = JsonSerializer.Deserialize<object>(draftJson, JsonOptions)
            ?? new Dictionary<string, object>();
        navigation["lastPublishedAt"] = DateTime.UtcNow.ToString("O");

        root["navigation"] = navigation;
        config.CustomizationJson = JsonSerializer.Serialize(root, JsonOptions);
        config.UpdatedAtUtc = DateTime.UtcNow;
        AppendVersionSnapshot(config, "Navigation Published");

        await StageNavigationPublishedEventAsync(config, config.LiveVersion, traceId, cancellationToken);
        await SaveStoreConfigChangesAsync(config, cancellationToken);
        await InvalidateCacheAsync(tenantId, traceId);

        return ExtractNavigationFromConfig(config.CustomizationJson, config.RowVersion);
    }

    private async Task<HashSet<string>> GetExistingCmsPageSlugsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var pageSlugs = await _dbContext.CmsPages
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Where(item => !item.IsDeleted && (item.IsDraft || item.IsPublished))
            .Select(item => item.Slug)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var slug in pageSlugs)
        {
            slugs.Add(slug);
        }

        return slugs;
    }

    private async Task<HashSet<string>> GetPublishedCmsPageSlugsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var slugs = await _dbContext.CmsPages
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Where(item => !item.IsDeleted && item.IsPublished && !item.IsDraft && item.PublishedAt != null)
            .Select(item => item.Slug)
            .Distinct()
            .ToListAsync(cancellationToken);

        return slugs.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<HashSet<string>> GetLiveCategorySlugsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var lineage = await CategoryLineageAuthority.LoadTenantSnapshotAsync(
            _dbContext,
            tenantId,
            cancellationToken);

        return lineage.Rows
            .Where(row => lineage.IsLiveInLineage(row.CategoryId))
            .Select(row => GenerateCategorySlug(row.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object> MapNavigationLink(AdminNavigationLinkRequest link, int index)
    {
        return new Dictionary<string, object>
        {
            ["label"] = link.Label.Trim(),
            ["labelAr"] = link.LabelAr?.Trim() ?? string.Empty,
            ["type"] = link.Type.Trim().ToLowerInvariant(),
            ["path"] = link.Path?.Trim() ?? string.Empty,
            ["slug"] = link.Slug?.Trim().ToLowerInvariant() ?? string.Empty,
            ["openInNewTab"] = link.OpenInNewTab,
            ["order"] = index,
            ["enabled"] = link.Enabled
        };
    }

    private static Dictionary<string, object> ParseCustomizationJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            return new Dictionary<string, object>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, object>>(json, JsonOptions)
            ?? new Dictionary<string, object>();
    }

    private static Dictionary<string, object> ConvertToDictionary(object? obj)
    {
        if (obj is Dictionary<string, object> dictionary)
        {
            return dictionary;
        }

        if (obj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement.GetRawText(), JsonOptions)
                ?? new Dictionary<string, object>();
        }

        if (obj == null)
        {
            return new Dictionary<string, object>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, object>>(
                   JsonSerializer.Serialize(obj, JsonOptions),
                   JsonOptions)
               ?? new Dictionary<string, object>();
    }

    private AdminNavigationResponse ExtractNavigationFromConfig(string? json, uint rowVersion = 0)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return GetDefaultNavigation();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("navigation", out var navigationElement))
            {
                return GetDefaultNavigation();
            }

            var response = new AdminNavigationResponse
            {
                RowVersion = rowVersion.ToString(CultureInfo.InvariantCulture)
            };

            if (navigationElement.TryGetProperty("draft", out var draftElement))
            {
                response.Draft = ExtractNavigationConfig(draftElement);
            }

            if (navigationElement.TryGetProperty("live", out var liveElement) && liveElement.ValueKind == JsonValueKind.Object)
            {
                response.Live = ExtractNavigationConfig(liveElement);
            }

            response.HasPendingChanges = navigationElement.TryGetProperty("draft", out var draft)
                && (!navigationElement.TryGetProperty("live", out var live) ||
                    live.ValueKind != JsonValueKind.Object ||
                    !JsonElement.DeepEquals(draft, live));

            return response;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse navigation configuration.");
            return GetDefaultNavigation();
        }
    }

    private static AdminNavigationConfigResponse ExtractNavigationConfig(JsonElement element)
    {
        return new AdminNavigationConfigResponse
        {
            Header = element.TryGetProperty("header", out var headerElement)
                ? ExtractNavigationLinks(headerElement)
                : new List<AdminNavigationLinkRequest>(),
            Footer = element.TryGetProperty("footer", out var footerElement)
                ? ExtractNavigationLinks(footerElement)
                : new List<AdminNavigationLinkRequest>()
        };
    }

    private static List<AdminNavigationLinkRequest> ExtractNavigationLinks(JsonElement menuElement)
    {
        JsonElement linksElement;
        if (menuElement.TryGetProperty("links", out var nestedLinks))
        {
            linksElement = nestedLinks;
        }
        else if (menuElement.ValueKind == JsonValueKind.Array)
        {
            linksElement = menuElement;
        }
        else
        {
            return new List<AdminNavigationLinkRequest>();
        }

        if (linksElement.ValueKind != JsonValueKind.Array)
        {
            return new List<AdminNavigationLinkRequest>();
        }

        return linksElement
            .EnumerateArray()
            .Select(link => new AdminNavigationLinkRequest
            {
                Label = GetStringProperty(link, "label", string.Empty) ?? string.Empty,
                LabelAr = GetStringProperty(link, "labelAr", null),
                Type = GetStringProperty(link, "type", "internal") ?? "internal",
                Path = GetStringProperty(link, "path", null),
                Slug = GetStringProperty(link, "slug", null),
                OpenInNewTab = GetBoolProperty(link, "openInNewTab", false),
                Order = GetIntProperty(link, "order", 0),
                Enabled = GetBoolProperty(link, "enabled", true)
            })
            .OrderBy(link => link.Order)
            .ToList();
    }

    private static AdminNavigationResponse GetDefaultNavigation()
    {
        return new AdminNavigationResponse
        {
            Draft = new AdminNavigationConfigResponse
            {
                Header =
                [
                    new AdminNavigationLinkRequest { Label = "Home", Type = "internal", Path = "/", Order = 0, Enabled = true },
                    new AdminNavigationLinkRequest { Label = "Shop", Type = "internal", Path = "/products", Order = 1, Enabled = true }
                ],
                Footer =
                [
                    new AdminNavigationLinkRequest { Label = "About Us", Type = "cms", Slug = "about", Order = 0, Enabled = true },
                    new AdminNavigationLinkRequest { Label = "Contact", Type = "cms", Slug = "contact", Order = 1, Enabled = true },
                    new AdminNavigationLinkRequest { Label = "Privacy Policy", Type = "cms", Slug = "privacy", Order = 2, Enabled = true },
                    new AdminNavigationLinkRequest { Label = "Terms & Conditions", Type = "cms", Slug = "terms", Order = 3, Enabled = true }
                ]
            }
        };
    }

    private async Task SaveStoreConfigChangesAsync(
        Core.Entities.StoreConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("StoreConfig", config.Id, ex);
        }
    }

    private async Task StageNavigationPublishedEventAsync(
        Core.Entities.StoreConfig config,
        int version,
        string traceId,
        CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(
            new NavigationPublishedEvent
            {
                TenantId = config.TenantId,
                StoreConfigId = config.Id,
                EventVersion = version, // EventVersion is the preferred property per IntegrationEvent documentation
                PublishedAtUtc = DateTime.UtcNow,
                CorrelationId = traceId,
                IdempotencyKey = $"NAVIGATION_PUBLISHED:{config.TenantId:N}:{config.Id:N}:{version}"
            },
            cancellationToken);

        _logger.LogInformation(
            "NavigationPublishedEvent staged for outbox (v{Version}) | TenantId: {TenantId} | StoreConfigId: {StoreConfigId} | TraceId: {TraceId}",
            version,
            config.TenantId,
            config.Id,
            traceId);
    }

    private static void AppendVersionSnapshot(Core.Entities.StoreConfig config, string changeType)
    {
        var versions = JsonSerializer.Deserialize<List<VersionSnapshot>>(config.VersionsJson ?? "[]")
                       ?? new List<VersionSnapshot>();

        const int MaxVersions = 50;
        if (versions.Count >= MaxVersions)
        {
            versions = versions
                .OrderByDescending(snapshot => snapshot.Version)
                .Take(MaxVersions - 1)
                .OrderBy(snapshot => snapshot.Version)
                .ToList();
        }

        var newVersionNumber = (versions.Max(snapshot => (int?)snapshot.Version) ?? 0) + 1;
        versions.Add(new VersionSnapshot
        {
            Version = newVersionNumber,
            PublishedAt = DateTime.UtcNow,
            PublishedBy = "POS Admin",
            ChangeType = changeType,
            ThemePresetKey = config.ThemeLivePresetKey,
            ThemeLiveOverridesJson = config.ThemeLiveOverridesJson,
            CustomizationJson = config.CustomizationJson
        });

        config.VersionsJson = JsonSerializer.Serialize(versions);
        config.LiveVersion = newVersionNumber;
    }

    private async Task InvalidateCacheAsync(Guid tenantId, string traceId)
    {
        try
        {
            await _cacheService.InvalidateStoreConfigAsync(tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate store config cache | TenantId: {TenantId} | TraceId: {TraceId}", tenantId, traceId);
        }
    }

    private void ApplyExpectedRowVersion(Core.Entities.StoreConfig config, uint expectedRowVersion)
    {
        _dbContext.Entry(config)
            .Property(item => item.RowVersion)
            .OriginalValue = expectedRowVersion;
    }

    private static void ValidateNavigationShape(AdminNavigationRequest request)
    {
        if (request.Header.Count > 50 || request.Footer.Count > 50)
        {
            throw new InvalidNavigationLinkException(
                "menu",
                string.Empty,
                "Header and footer navigation are limited to 50 links each.");
        }
    }

    private static void ValidateNavigationMenu(
        IReadOnlyList<AdminNavigationLinkRequest> links,
        HashSet<string> cmsSlugs,
        HashSet<string> liveCategorySlugs,
        bool requirePublishedCms)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in links)
        {
            ValidateNavigationLink(link, cmsSlugs, liveCategorySlugs, requirePublishedCms);
            var targetKey = GetNavigationTargetKey(link);
            if (!targets.Add(targetKey))
            {
                throw new InvalidNavigationLinkException(
                    link.Type,
                    targetKey,
                    $"Duplicate navigation target '{targetKey}' is not allowed in the same menu.");
            }
        }
    }

    private static void ValidateNavigationLink(
        AdminNavigationLinkRequest link,
        HashSet<string> cmsSlugs,
        HashSet<string> liveCategorySlugs,
        bool requirePublishedCms)
    {
        var linkType = link.Type?.Trim().ToLowerInvariant() ?? string.Empty;
        var label = link.Label?.Trim() ?? string.Empty;
        if (label.Length is < 1 or > 120)
        {
            throw new InvalidNavigationLinkException(linkType, label, "Navigation link labels must be between 1 and 120 characters.");
        }

        switch (linkType)
        {
            case "cms":
                var slug = link.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(slug))
                {
                    throw new InvalidNavigationLinkException(linkType, slug, $"CMS page link '{link.Label}' requires a slug.");
                }

                if (!cmsSlugs.Contains(slug))
                {
                    var state = requirePublishedCms ? "published" : "saved";
                    throw new InvalidNavigationLinkException(linkType, slug, $"CMS page '{slug}' is not currently {state} for this tenant.");
                }
                break;

            case "category":
                var categorySlug = link.Slug?.Trim().Trim('/').ToLowerInvariant() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(categorySlug) || !liveCategorySlugs.Contains(categorySlug))
                {
                    throw new InvalidNavigationLinkException(linkType, categorySlug, $"Category '{categorySlug}' is not currently visible for this tenant.");
                }
                break;

            case "external":
                var url = link.Path?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(url))
                {
                    throw new InvalidNavigationLinkException(linkType, url, $"External link '{link.Label}' requires a URL.");
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) ||
                    (parsedUrl.Scheme != Uri.UriSchemeHttp && parsedUrl.Scheme != Uri.UriSchemeHttps) ||
                    !string.IsNullOrEmpty(parsedUrl.UserInfo))
                {
                    throw new InvalidNavigationLinkException(linkType, url, $"External link '{link.Label}' must be an http(s) URL without embedded credentials.");
                }
                break;

            case "internal":
                var path = link.Path?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new InvalidNavigationLinkException(linkType, path, $"Internal link '{link.Label}' requires a path.");
                }

                if (!IsSafeInternalPath(path))
                {
                    throw new InvalidNavigationLinkException(linkType, path, $"Internal link '{link.Label}' must be a safe storefront-relative path.");
                }
                break;

            default:
                throw new InvalidNavigationLinkException(linkType, string.Empty, $"Navigation link type '{link.Type}' is not supported.");
        }
    }

    private static string GetNavigationTargetKey(AdminNavigationLinkRequest link)
    {
        var type = link.Type.Trim().ToLowerInvariant();
        var target = type is "cms" or "category"
            ? link.Slug?.Trim().Trim('/').ToLowerInvariant()
            : link.Path?.Trim();
        return $"{type}:{target}";
    }

    private static bool IsSafeInternalPath(string path)
    {
        if (!path.StartsWith("/", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains('\\') ||
            path.Any(char.IsControl))
        {
            return false;
        }

        if (!Uri.TryCreate(path, UriKind.Relative, out _))
        {
            return false;
        }

        var lowered = path.ToLowerInvariant();
        return !lowered.StartsWith("/api/", StringComparison.Ordinal) &&
               !lowered.StartsWith("/admin", StringComparison.Ordinal) &&
               !lowered.Contains("preview=true", StringComparison.Ordinal) &&
               !lowered.Contains("previewtoken=", StringComparison.Ordinal);
    }

    private static string GenerateCategorySlug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "category";
        }

        var slug = name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("&", "and");

        return System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\-]", "");
    }

    private static string? GetStringProperty(JsonElement element, string propertyName, string? defaultValue)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? defaultValue
            : defaultValue;
    }

    private static bool GetBoolProperty(JsonElement element, string propertyName, bool defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return defaultValue;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    private static int GetIntProperty(JsonElement element, string propertyName, int defaultValue)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : defaultValue;
    }

    private sealed class VersionSnapshot
    {
        public int Version { get; set; }
        public DateTime PublishedAt { get; set; }
        public string PublishedBy { get; set; } = "POS Admin";
        public string? PublishedByUserId { get; set; }
        public string ChangeType { get; set; } = "Unknown";
        public int? RestoredFromVersion { get; set; }
        public string ThemePresetKey { get; set; } = string.Empty;
        public string ThemeLiveOverridesJson { get; set; } = "{}";
        public string CustomizationJson { get; set; } = "{}";
    }
}
