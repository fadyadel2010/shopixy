using System.Text.Json;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Cms;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Admin;

public sealed class AdminCmsService : IAdminCmsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICmsPageService _cmsPageService;
    private readonly ICmsPageReadService _cmsPageReadService;
    private readonly ICmsPagePreviewTokenService _cmsPagePreviewTokenService;
    private readonly NavigationAdmissionService _navigationAdmission;
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
        NavigationAdmissionService navigationAdmission,
        IEventPublisher eventPublisher,
        ILogger<AdminCmsService> logger)
    {
        _dbContext = dbContext;
        _cmsPageService = cmsPageService;
        _cmsPageReadService = cmsPageReadService;
        _cmsPagePreviewTokenService = cmsPagePreviewTokenService;
        _navigationAdmission = navigationAdmission;
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

        var response = config == null
            ? GetDefaultNavigation()
            : ExtractNavigationFromConfig(config.CustomizationJson, config.RowVersion);
        var admittedDraft = await AdmitNavigationForLoadAsync(
            tenantId,
            response.RowVersion,
            "draft",
            response.Draft,
            NavigationAdmissionMode.Draft,
            cancellationToken);
        response.Draft = new AdminNavigationConfigResponse
        {
            Header = admittedDraft.Navigation.Header,
            Footer = admittedDraft.Navigation.Footer
        };
        response.ValidationWarnings.AddRange(admittedDraft.Warnings);

        if (response.Live != null)
        {
            var admittedLive = await AdmitNavigationForLoadAsync(
                tenantId,
                response.RowVersion,
                "live",
                response.Live,
                NavigationAdmissionMode.Publish,
                cancellationToken);
            response.Live = new AdminNavigationConfigResponse
            {
                Header = admittedLive.Navigation.Header,
                Footer = admittedLive.Navigation.Footer
            };
            response.ValidationWarnings.AddRange(admittedLive.Warnings);
        }
        response.BrokenLinks = response.ValidationWarnings
            .Where(warning => warning.Status == "invalid")
            .ToList();

        return response;
    }

    private async Task<NavigationLoadAdmissionResult> AdmitNavigationForLoadAsync(
        Guid tenantId,
        string rowVersion,
        string snapshot,
        AdminNavigationConfigResponse navigation,
        NavigationAdmissionMode mode,
        CancellationToken cancellationToken)
    {
        var request = new AdminNavigationRequest
        {
            RowVersion = rowVersion,
            Header = navigation.Header,
            Footer = navigation.Footer
        };
        NavigationAdmissionException? documentFailure = null;

        try
        {
            var admitted = await _navigationAdmission.AdmitAsync(
                tenantId,
                request,
                mode,
                cancellationToken);
            return new NavigationLoadAdmissionResult(admitted, []);
        }
        catch (NavigationAdmissionException ex)
        {
            documentFailure = ex;
        }

        var warnings = new List<AdminNavigationValidationWarningResponse>();
        var admittedNavigation = new AdminNavigationRequest
        {
            RowVersion = rowVersion,
            Header = await AdmitMenuForLoadAsync(
                tenantId,
                rowVersion,
                snapshot,
                "header",
                navigation.Header,
                mode,
                warnings,
                cancellationToken),
            Footer = await AdmitMenuForLoadAsync(
                tenantId,
                rowVersion,
                snapshot,
                "footer",
                navigation.Footer,
                mode,
                warnings,
                cancellationToken)
        };

        if (documentFailure is not null &&
            documentFailure.Index.HasValue &&
            !warnings.Any(warning =>
                string.Equals(warning.Snapshot, snapshot, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(warning.Menu, documentFailure.Menu, StringComparison.OrdinalIgnoreCase) &&
                warning.Index == documentFailure.Index &&
                string.Equals(warning.Code, documentFailure.Code, StringComparison.OrdinalIgnoreCase)))
        {
            var documentWarning = ToNavigationValidationWarning(
                documentFailure,
                snapshot,
                documentFailure.Menu,
                documentFailure.Index.Value,
                ResolveLoadLink(admittedNavigation, documentFailure.Menu, documentFailure.Index.Value));
            warnings.Add(documentWarning);
            MarkInvalidLink(admittedNavigation, documentWarning);
        }

        return new NavigationLoadAdmissionResult(admittedNavigation, warnings);
    }

    private async Task<List<AdminNavigationLinkRequest>> AdmitMenuForLoadAsync(
        Guid tenantId,
        string rowVersion,
        string snapshot,
        string menu,
        IReadOnlyList<AdminNavigationLinkRequest> links,
        NavigationAdmissionMode mode,
        List<AdminNavigationValidationWarningResponse> warnings,
        CancellationToken cancellationToken)
    {
        var admitted = new List<AdminNavigationLinkRequest>(links.Count);

        for (var index = 0; index < links.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var link = links[index];
            var request = new AdminNavigationRequest
            {
                RowVersion = rowVersion,
                Header = menu == "header" ? [link] : [],
                Footer = menu == "footer" ? [link] : []
            };

            try
            {
                var admittedSingle = await _navigationAdmission.AdmitAsync(
                    tenantId,
                    request,
                    mode,
                    cancellationToken);
                var admittedLink = menu == "header"
                    ? admittedSingle.Header.Single()
                    : admittedSingle.Footer.Single();
                admittedLink.Order = index;
                admitted.Add(admittedLink);
            }
            catch (NavigationAdmissionException ex)
            {
                var preserved = PreserveInvalidNavigationLink(link, index, ex);
                var warning = ToNavigationValidationWarning(ex, snapshot, menu, index, preserved);
                preserved.ValidationStatus = warning.Status;
                preserved.ValidationCode = warning.Code;
                preserved.ValidationMessage = warning.Message;
                warnings.Add(warning);
                admitted.Add(preserved);
            }
        }

        return admitted;
    }

    private static AdminNavigationLinkRequest PreserveInvalidNavigationLink(
        AdminNavigationLinkRequest? link,
        int index,
        NavigationAdmissionException exception)
    {
        var type = NavigationStorageAdapter.NormalizeAuthoringType(link?.Type);
        if (string.IsNullOrWhiteSpace(type))
        {
            type = string.IsNullOrWhiteSpace(exception.LinkType) ? "custom" : exception.LinkType;
        }

        return new AdminNavigationLinkRequest
        {
            Label = string.IsNullOrWhiteSpace(link?.Label) ? "Invalid navigation link" : link.Label.Trim(),
            LabelAr = string.IsNullOrWhiteSpace(link?.LabelAr) ? null : link.LabelAr.Trim(),
            Type = type,
            TargetId = link?.TargetId ?? exception.TargetId,
            CollectionKey = string.IsNullOrWhiteSpace(link?.CollectionKey) ? null : link.CollectionKey.Trim(),
            Path = string.IsNullOrWhiteSpace(link?.Path) ? null : link.Path.Trim(),
            Slug = string.IsNullOrWhiteSpace(link?.Slug) ? null : link.Slug.Trim(),
            OpenInNewTab = link?.OpenInNewTab == true,
            Order = index,
            Enabled = link?.Enabled ?? true,
            ValidationStatus = "invalid",
            ValidationCode = exception.Code,
            ValidationMessage = exception.Message
        };
    }

    private static AdminNavigationValidationWarningResponse ToNavigationValidationWarning(
        NavigationAdmissionException exception,
        string snapshot,
        string menu,
        int index,
        AdminNavigationLinkRequest? link) =>
        new()
        {
            Code = exception.Code,
            Message = exception.Message,
            Snapshot = snapshot,
            Menu = menu,
            Index = index,
            Field = exception.Field,
            LinkType = string.IsNullOrWhiteSpace(exception.LinkType)
                ? NavigationStorageAdapter.NormalizeAuthoringType(link?.Type)
                : exception.LinkType,
            TargetId = exception.TargetId ?? link?.TargetId,
            Status = "invalid"
        };

    private static AdminNavigationLinkRequest? ResolveLoadLink(
        AdminNavigationRequest navigation,
        string menu,
        int index)
    {
        var links = menu == "header"
            ? navigation.Header
            : menu == "footer"
                ? navigation.Footer
                : null;
        return links != null && index >= 0 && index < links.Count
            ? links[index]
            : null;
    }

    private static void MarkInvalidLink(
        AdminNavigationRequest navigation,
        AdminNavigationValidationWarningResponse warning)
    {
        if (!warning.Index.HasValue)
        {
            return;
        }

        var link = ResolveLoadLink(navigation, warning.Menu, warning.Index.Value);
        if (link == null)
        {
            return;
        }

        link.ValidationStatus = warning.Status;
        link.ValidationCode = warning.Code;
        link.ValidationMessage = warning.Message;
    }

    private sealed record NavigationLoadAdmissionResult(
        AdminNavigationRequest Navigation,
        List<AdminNavigationValidationWarningResponse> Warnings);

    public async Task<AdminNavigationResponse> UpdateNavigationAsync(
        Guid tenantId,
        AdminNavigationRequest? request,
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
        var admitted = await _navigationAdmission.AdmitAsync(
            tenantId,
            request,
            NavigationAdmissionMode.Draft,
            cancellationToken);

        var root = ParseCustomizationJson(config.CustomizationJson);
        var navigation = root.TryGetValue("navigation", out var navigationObj)
            ? ConvertToDictionary(navigationObj)
            : new Dictionary<string, object>();

        navigation["draft"] = new Dictionary<string, object>
        {
            ["header"] = new Dictionary<string, object>
            {
                ["links"] = admitted.Header.Select(NavigationStorageAdapter.ToStorage).ToList()
            },
            ["footer"] = new Dictionary<string, object>
            {
                ["links"] = admitted.Footer.Select(NavigationStorageAdapter.ToStorage).ToList()
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

    public Task<AdminNavigationTargetsResponse> GetNavigationTargetsAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "GetNavigationTargets | TenantId: {TenantId} | TraceId: {TraceId}",
            tenantId,
            traceId);

        return _navigationAdmission.GetTargetsAsync(tenantId, cancellationToken);
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
        if (navigation.TryGetValue("live", out var liveObj) &&
            JsonElement.DeepEquals(draftElement, JsonSerializer.SerializeToElement(liveObj, JsonOptions)))
        {
            return ExtractNavigationFromConfig(config.CustomizationJson, config.RowVersion);
        }

        var draft = NavigationStorageAdapter.FromStorage(draftElement);
        var admitted = await _navigationAdmission.AdmitAsync(
            tenantId,
            new AdminNavigationRequest
            {
                Header = draft.Header,
                Footer = draft.Footer
            },
            NavigationAdmissionMode.Publish,
            cancellationToken);
        var admittedDraft = new Dictionary<string, object>
        {
            ["header"] = new Dictionary<string, object>
            {
                ["links"] = admitted.Header.Select(NavigationStorageAdapter.ToStorage).ToList()
            },
            ["footer"] = new Dictionary<string, object>
            {
                ["links"] = admitted.Footer.Select(NavigationStorageAdapter.ToStorage).ToList()
            }
        };
        navigation["draft"] = admittedDraft;
        navigation["live"] = JsonSerializer.Deserialize<object>(
            JsonSerializer.Serialize(admittedDraft, JsonOptions),
            JsonOptions) ?? new Dictionary<string, object>();
        navigation["lastPublishedAt"] = DateTime.UtcNow.ToString("O");

        root["navigation"] = navigation;
        config.CustomizationJson = JsonSerializer.Serialize(root, JsonOptions);
        config.UpdatedAtUtc = DateTime.UtcNow;
        AppendVersionSnapshot(config, "Navigation Published");

        await StageNavigationPublishedEventAsync(config, config.LiveVersion, traceId, cancellationToken);
        await SaveStoreConfigChangesAsync(config, cancellationToken);

        return ExtractNavigationFromConfig(config.CustomizationJson, config.RowVersion);
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
            return GetDefaultNavigation(rowVersion);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("navigation", out var navigationElement))
            {
                return GetDefaultNavigation(rowVersion);
            }

            var response = new AdminNavigationResponse
            {
                RowVersion = rowVersion.ToString(CultureInfo.InvariantCulture)
            };

            if (navigationElement.TryGetProperty("draft", out var draftElement))
            {
                response.Draft = NavigationStorageAdapter.FromStorage(draftElement);
            }

            if (navigationElement.TryGetProperty("live", out var liveElement) && liveElement.ValueKind == JsonValueKind.Object)
            {
                response.Live = NavigationStorageAdapter.FromStorage(liveElement);
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
            return GetDefaultNavigation(rowVersion);
        }
    }

    private static AdminNavigationResponse GetDefaultNavigation(uint rowVersion = 0)
    {
        return new AdminNavigationResponse
        {
            RowVersion = rowVersion.ToString(CultureInfo.InvariantCulture),
            Draft = new AdminNavigationConfigResponse
            {
                Header =
                [
                    new AdminNavigationLinkRequest { Label = "Home", Type = "custom", Path = "/", Order = 0, Enabled = true },
                    new AdminNavigationLinkRequest { Label = "Shop", Type = "collection", CollectionKey = NavigationAdmissionService.AllProductsCollectionKey, Path = "/products", Order = 1, Enabled = true }
                ],
                Footer = []
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

    private void ApplyExpectedRowVersion(Core.Entities.StoreConfig config, uint expectedRowVersion)
    {
        if (config.RowVersion != expectedRowVersion)
        {
            throw new ConcurrencyConflictException("StoreConfig", config.Id);
        }

        _dbContext.Entry(config)
            .Property(item => item.RowVersion)
            .OriginalValue = expectedRowVersion;
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
