using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Security;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Services.Admin;

/// <summary>
/// Admin Store Config Service Implementation.
/// Phase 7.1: Theme & Store Config System
/// 
/// Flow:
/// 1. Get/Update store config in PostgreSQL
/// 2. Invalidate Redis cache
/// 3. Emit StoreConfigUpdatedEvent
/// </summary>
public class AdminStoreConfigService : IAdminStoreConfigService
{
    private static readonly Regex BuilderSectionIdRegex = new(@"^section_[A-Za-z0-9_-]{8,80}$", RegexOptions.Compiled);
    private static readonly Regex HexColorRegex = new(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$", RegexOptions.Compiled);
    private const int BuilderShortTextMaxLength = 160;
    private const int BuilderLongTextMaxLength = 5000;
    private const int BuilderUrlMaxLength = 2048;
    private const int BuilderIdMaxLength = 100;
    private static readonly HashSet<string> ReservedBuilderLayoutAuthorityKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "layoutId",
        "containerId",
        "containerIds",
        "containers",
        "gridId",
        "grid",
        "gridStructure",
        "columnId",
        "columnIds",
        "columnStructure",
        "responsive",
        "responsiveRules",
        "breakpoints",
        "visibilityRules"
    };

    private static readonly HashSet<string> AllowedBuilderSectionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "hero-banner",
        "product-grid",
        "category-grid",
        "subcategory-grid",
        "text-block",
        "image-block",
        "video-block",
        "newsletter-signup",
        "testimonials",
        "featured-products",
        "product-hero",
        "product-gallery",
        "product-specs",
        "product-video",
        "customer-reviews",
        "related-products"
    };

    private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedBuilderSectionSettingKeys =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["hero-banner"] = SettingKeys("title", "subtitle", "ctaText", "ctaLink", "backgroundImage", "backgroundColor", "overlayOpacity", "textColor", "alignment", "height"),
            ["product-grid"] = SettingKeys("title", "productSource", "categoryId", "productIds", "columns", "rows", "showPrice", "showRating", "showAddToCart"),
            ["category-grid"] = SettingKeys("title", "columns", "categoryIds", "showProductCount", "cardStyle"),
            ["subcategory-grid"] = SettingKeys("title", "columns", "showProductCount", "cardStyle"),
            ["text-block"] = SettingKeys("heading", "content", "alignment", "backgroundColor", "textColor", "maxWidth"),
            ["image-block"] = SettingKeys("images", "layout", "aspectRatio", "fullWidth"),
            ["video-block"] = SettingKeys("videoUrl", "autoplay", "showControls"),
            ["newsletter-signup"] = SettingKeys("heading", "subheading", "buttonText", "backgroundColor", "textColor", "showName"),
            ["testimonials"] = SettingKeys("heading", "testimonials", "layout", "showRating", "autoplay"),
            ["featured-products"] = SettingKeys("title", "productIds", "limit", "showPrice", "showRating", "showBadge", "badgeText"),
            ["product-hero"] = SettingKeys("showDescription", "showVariantSelector", "showAddToCart", "showSku", "showStockStatus"),
            ["product-gallery"] = SettingKeys("title", "showThumbnails", "maxImages"),
            ["product-specs"] = SettingKeys("title", "useDescription", "items"),
            ["product-video"] = SettingKeys("title", "videoUrl", "autoplay", "showControls"),
            ["customer-reviews"] = SettingKeys("title", "reviews"),
            ["related-products"] = SettingKeys("title", "limit", "showPrice", "showRating")
        };

    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly IPublishLockService _publishLockService;
    private readonly IStoreConfigAuditService _auditService;
    private readonly IStorefrontPreviewService _previewService;
    private readonly StorefrontPublishTelemetry _publishTelemetry;
    private readonly ILogger<AdminStoreConfigService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AdminStoreConfigService(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        IPublishLockService publishLockService,
        IStoreConfigAuditService auditService,
        IStorefrontPreviewService previewService,
        StorefrontPublishTelemetry publishTelemetry,
        ILogger<AdminStoreConfigService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _publishLockService = publishLockService;
        _auditService = auditService;
        _previewService = previewService;
        _publishTelemetry = publishTelemetry;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public async Task<AdminStoreConfigResponse> GetStoreConfigAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "GetStoreConfig | TenantId: {TenantId} | TraceId: {TraceId}",
            tenantId, traceId);

        var config = await _dbContext.StoreConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (config == null)
        {
            _logger.LogInformation(
                "StoreConfig not found, returning default read candidate | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);

            config = BuildDefaultConfig(tenantId);
        }

        return await MapToResponseAsync(config, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AdminStoreConfigResponse> UpdateStoreConfigAsync(
        Guid tenantId,
        AdminStoreConfigRequest request,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null)
    {
        _logger.LogInformation(
            "UpdateStoreConfig | TenantId: {TenantId} | ThemeId: {ThemeId} | TraceId: {TraceId}",
            tenantId, request.ThemeId, traceId);

        // =====================================================================
        // Phase 8.4: Enforce email verification for storefront publishing
        // =====================================================================
        var requestedStorefrontStatus = string.IsNullOrWhiteSpace(request.StorefrontStatus) 
            ? "live" 
            : request.StorefrontStatus.ToLowerInvariant();
        
        if (requestedStorefrontStatus == "live")
        {
            // Check if owner (tenant creator) has verified email
            var owner = await _dbContext.Users
                .Where(u => u.TenantId == tenantId && u.Role == UserRole.Owner)
                .FirstOrDefaultAsync(cancellationToken);
            
            if (owner != null && owner.EmailVerificationStatus == EmailVerificationStatus.Unverified)
            {
                _logger.LogWarning(
                    "StorefrontPublishBlocked | TenantId: {TenantId} | OwnerId: {OwnerId} | Reason: Email not verified | TraceId: {TraceId}",
                    tenantId, owner.Id, traceId);
                
                throw new StorefrontVerificationException(
                    "Please verify your email before publishing your storefront.");
            }
        }

        var config = await _dbContext.StoreConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        var isNew = config == null;

        if (isNew)
        {
            // A read-only default candidate reports RowVersion 0. Allow that value to create the first
            // persisted config, but fail closed for any nonzero version that claims a missing row exists.
            if (expectedRowVersion.HasValue && expectedRowVersion.Value != 0)
            {
                _logger.LogWarning(
                    "Store config concurrency conflict - expected version {ExpectedVersion} but config does not exist | TenantId: {TenantId} | TraceId: {TraceId}",
                    expectedRowVersion.Value,
                    tenantId,
                    traceId);
                throw new ConcurrencyConflictException("StoreConfig", tenantId);
            }

            config = BuildDefaultConfig(tenantId);
            _dbContext.StoreConfigs.Add(config);
        }
        else
        {
            ApplyExpectedRowVersion(config!, expectedRowVersion);
        }

        // Update config from request
        config!.StoreName = request.StoreName;
        config.StoreDescription = request.StoreDescription;
        config.ThemeId = request.ThemeId;
        config.Theme = MapThemeFromDto(request.Theme);
        config.Behavior = MapBehaviorFromDto(request.Behavior);
        config.CustomizationJson = SetStorefrontThemeInCustomizationJson(
            config.CustomizationJson,
            request.StorefrontTheme,
            fallbackThemeId: config.ThemeId);
        config.StorefrontStatus = requestedStorefrontStatus;
        config.MaintenanceMessage = request.MaintenanceMessage;
        
        // Phase 7: Commerce AI
        config.CommerceAiEnabled = request.CommerceAi.Enabled;
        config.CommerceAiWidgetStyle = request.CommerceAi.WidgetStyle;
        config.CommerceAiWidgetPosition = request.CommerceAi.WidgetPosition;
        config.CommerceAiAutoGreetingEnabled = request.CommerceAi.AutoGreetingEnabled;
        config.CommerceAiAutoGreetingMessage = request.CommerceAi.AutoGreetingMessage;
        config.CommerceAiPersonality = request.CommerceAi.Personality;
        
        config.UpdatedAtUtc = DateTime.UtcNow;

        await PersistStoreConfigChangeAsync(
            config,
            isNew ? StoreConfigPublishAction.Created : StoreConfigPublishAction.Updated,
            traceId,
            cancellationToken);

        _logger.LogInformation(
            "StoreConfig {Operation} | TenantId: {TenantId} | ConfigId: {ConfigId} | ThemeId: {ThemeId} | TraceId: {TraceId}",
            isNew ? "CREATED" : "UPDATED", tenantId, config.Id, config.ThemeId, traceId);

        return await MapToResponseAsync(config, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AdminStoreConfigResponse> UpdatePageLayoutAsync(
        Guid tenantId,
        string pageId,
        Dictionary<string, object> pageConfig,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null)
    {
        _logger.LogInformation("UpdatePageLayout (Draft) | TenantId: {TenantId} | PageId: {PageId}", tenantId, pageId);

        var config = await GetRequiredStoreConfigForMutationAsync(
            tenantId,
            expectedRowVersion,
            cancellationToken);

        // Parse CustomizationJson
        var json = config.CustomizationJson ?? "{}";
        var root = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();

        if (TryGetProductTemplateKey(pageId, out var templateId))
        {
            if (!root.ContainsKey("product_templates")) root["product_templates"] = new Dictionary<string, object>();

            var templates = GetCustomizationNode(root, "product_templates");
            if (!templates.ContainsKey(templateId)) templates[templateId] = new Dictionary<string, object>();

            var templateConfig = new Dictionary<string, object>(pageConfig)
            {
                ["templateId"] = templateId
            };

            if (!templateConfig.ContainsKey("name"))
            {
                templateConfig["name"] = templateId;
            }

            var templateContent = templates[templateId];
            templateContent["draft"] = NormalizeBuilderPageComposition(templateConfig, templateContent, authoritativeTemplateId: templateId);

            if (!templateContent.ContainsKey("live")) templateContent["live"] = new Dictionary<string, object>();

            root["product_templates"] = templates;
        }
        else
        {
            if (!root.ContainsKey("pages")) root["pages"] = new Dictionary<string, object>();

            var pages = GetCustomizationNode(root, "pages");
            if (!pages.ContainsKey(pageId)) pages[pageId] = new Dictionary<string, object>();

            // Update DRAFT only
            var pageContent = pages[pageId];
            pageContent["draft"] = NormalizeBuilderPageComposition(pageConfig, pageContent); // Replace draft entirely

            // Ensure LIVE exists if not present (don't break it)
            if (!pageContent.ContainsKey("live")) pageContent["live"] = new Dictionary<string, object>();

            root["pages"] = pages;
        }

        config.CustomizationJson = System.Text.Json.JsonSerializer.Serialize(root);
        config.UpdatedAtUtc = DateTime.UtcNow;

        await SaveDraftStoreConfigAsync(config, traceId, cancellationToken);
        
        return await MapToResponseAsync(config, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AdminStoreConfigResponse> PublishPageAsync(
        Guid tenantId,
        string pageId,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null)
    {
        _logger.LogInformation("PublishPage (Live) | TenantId: {TenantId} | PageId: {PageId}", tenantId, pageId);

        var lockResult = await _publishLockService.TryAcquireAsync(tenantId, $"page:{pageId}");
        if (!lockResult.Acquired)
        {
            _logger.LogWarning("PublishPage rejected - lock not acquired | TenantId: {TenantId} | PageId: {PageId}", tenantId, pageId);
            throw new ConcurrencyConflictException("StoreConfigPublish", tenantId);
        }

        try
        {
            var config = await GetRequiredStoreConfigForMutationAsync(
                tenantId,
                expectedRowVersion,
                cancellationToken);

            var json = config.CustomizationJson ?? "{}";
            var root = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json) 
                      ?? new Dictionary<string, object>();

            Dictionary<string, Dictionary<string, object>>? container;
            string containerKey;
            string entryKey;

            if (TryGetProductTemplateKey(pageId, out var templateId))
            {
                containerKey = "product_templates";
                entryKey = templateId;
            }
            else
            {
                containerKey = "pages";
                entryKey = pageId;
            }

            if (!root.ContainsKey(containerKey))
            {
                throw new KeyNotFoundException($"Publish target '{pageId}' was not found.");
            }

            container = GetCustomizationNode(root, containerKey);

            if (!container.ContainsKey(entryKey))
            {
                throw new KeyNotFoundException($"Publish target '{pageId}' was not found.");
            }

            var pageContent = container[entryKey];
        
            if (!pageContent.TryGetValue("draft", out var draftObj)) 
            {
                 throw new ArgumentException($"No draft content to publish for {pageId}");
            }

            // Copy Draft -> Live
            var draftJson = System.Text.Json.JsonSerializer.Serialize(draftObj);
            var liveObj = System.Text.Json.JsonSerializer.Deserialize<object>(draftJson); 
        
            pageContent["live"] = liveObj!;
            pageContent["lastPublishedAt"] = DateTime.UtcNow;

            root[containerKey] = container;
            config.CustomizationJson = System.Text.Json.JsonSerializer.Serialize(root);
            config.UpdatedAtUtc = DateTime.UtcNow;

            var actor = ResolveCurrentActor();

            // Create version snapshot BEFORE saving changes to DB
            CreateAndAppendVersionSnapshot(config, "Page Published", actor);

            await PersistStoreConfigChangeAsync(
                config,
                StoreConfigPublishAction.Published,
                traceId,
                cancellationToken);

            // Audit the page publish - failures are logged but do not propagate to caller
            // since the DB commit already succeeded
            var requestMetadata = GetCurrentRequestMetadata();
            try
            {
                await _auditService.RecordAuditAsync(
                    tenantId,
                    AuditActionTypes.PagePublished,
                    pageId,
                    beforeSnapshot: null,
                    afterSnapshot: config.CustomizationJson,
                    config.LiveVersion,
                    actor.UserId,
                    actor.DisplayName,
                    traceId,
                    requestMetadata.IpAddress,
                    requestMetadata.UserAgent,
                    cancellationToken);
            }
            catch (Exception auditEx)
            {
                _logger.LogError(
                    auditEx,
                    "PagePublishAuditFailed | TenantId: {TenantId} | PageId: {PageId} | LiveVersion: {LiveVersion} | TraceId: {TraceId} | Publish succeeded but audit recording failed",
                    tenantId, pageId, config.LiveVersion, traceId);
            }

            return await MapToResponseAsync(config, cancellationToken);
        }
        finally
        {
            if (lockResult.LockToken != null)
            {
                await _publishLockService.ReleaseAsync(tenantId, lockResult.LockToken);
            }
        }
    }

    /// <inheritdoc />
    public async Task<AdminStoreConfigResponse> DeleteProductTemplateAsync(
        Guid tenantId,
        string templateId,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null)
    {
        _logger.LogInformation("DeleteProductTemplate | TenantId: {TenantId} | TemplateId: {TemplateId}", tenantId, templateId);

        var config = await GetRequiredStoreConfigForMutationAsync(
            tenantId,
            expectedRowVersion,
            cancellationToken);

        var json = config.CustomizationJson ?? "{}";
        var root = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                  ?? new Dictionary<string, object>();

        if (!root.ContainsKey("product_templates"))
        {
            return await MapToResponseAsync(config, cancellationToken);
        }

        var templates = GetCustomizationNode(root, "product_templates");
        if (!templates.Remove(templateId))
        {
            return await MapToResponseAsync(config, cancellationToken);
        }

        root["product_templates"] = templates;
        config.CustomizationJson = System.Text.Json.JsonSerializer.Serialize(root);
        config.UpdatedAtUtc = DateTime.UtcNow;

        await PersistStoreConfigChangeAsync(
            config,
            StoreConfigPublishAction.Updated,
            traceId,
            cancellationToken);

        return await MapToResponseAsync(config, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<ThemePresetDto>> GetThemePresetsAsync(
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetThemePresets | TraceId: {TraceId}", traceId);

        var presets = await _dbContext.ThemePresets
            .AsNoTracking()
            .OrderBy(p => p.Key == "default" ? 0 : 1)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return presets.Select(p => new ThemePresetDto
        {
            Id = p.Id,
            Key = p.Key,
            Name = p.Name,
            IsRtl = p.IsRtl,
            PreviewImageUrl = p.PreviewImageUrl ?? string.Empty,
            BaseTheme = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(p.BaseThemeJson)
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<AdminStoreConfigResponse> SelectThemePresetAsync(
        Guid tenantId,
        string presetKey,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null)
    {
        _logger.LogInformation("SelectThemePreset (Draft) | TenantId: {TenantId} | Preset: {Preset} | TraceId: {TraceId}",
            tenantId, presetKey, traceId);

        // Validate preset exists
        var presetExists = await _dbContext.ThemePresets.AnyAsync(p => p.Key == presetKey, cancellationToken);
        if (!presetExists) throw new InvalidOperationException($"Theme preset '{presetKey}' not found");

        var config = await GetRequiredStoreConfigForMutationAsync(
            tenantId,
            expectedRowVersion,
            cancellationToken);

        // Update Preset Key & Clear Draft Overrides (Fresh start for new preset)
        config.ThemePresetKey = presetKey;
        // Optimization: When switching presets, we usually want to reset overrides
        // OR we can keep them if user wants to keep their 'logo size' customization across themes.
        // Rule: Clear draft overrides to avoid conflicts with new preset structure.
        config.ThemeDraftOverridesJson = "{}";

        config.UpdatedAtUtc = DateTime.UtcNow;

        await SaveDraftStoreConfigAsync(config, traceId, cancellationToken);

        // NO EVENT - Draft only
        return await MapToResponseAsync(config, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AdminStoreConfigResponse> UpdateThemeOverridesAsync(
        Guid tenantId,
        string overridesJson,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null)
    {
        _logger.LogInformation("UpdateThemeOverrides (Draft) | TenantId: {TenantId} | TraceId: {TraceId}", tenantId, traceId);

        var config = await GetRequiredStoreConfigForMutationAsync(
            tenantId,
            expectedRowVersion,
            cancellationToken);

        var (normalizedOverridesJson, selectedPresetKey) = await NormalizeThemeOverridesAsync(overridesJson, cancellationToken);
        if (!string.IsNullOrWhiteSpace(selectedPresetKey))
        {
            config.ThemePresetKey = selectedPresetKey;
        }

        config.ThemeDraftOverridesJson = normalizedOverridesJson;
        config.UpdatedAtUtc = DateTime.UtcNow;

        await SaveDraftStoreConfigAsync(config, traceId, cancellationToken);

        // NO EVENT - Draft only
        return await MapToResponseAsync(config, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AdminStoreConfigResponse> PublishThemeAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null)
    {
        _logger.LogInformation("PublishTheme (Live) | TenantId: {TenantId} | TraceId: {TraceId}", tenantId, traceId);

        // ============================================================
        // PHASE P3: DISTRIBUTED PUBLISH LOCK
        // ============================================================
        var lockResult = await _publishLockService.TryAcquireAsync(tenantId, "theme");
        if (!lockResult.Acquired)
        {
            _logger.LogWarning("PublishTheme rejected - lock not acquired | TenantId: {TenantId}", tenantId);
            throw new InvalidOperationException(lockResult.Message ?? "Publishing already in progress.");
        }

        try
        {
            var config = await GetRequiredStoreConfigForMutationAsync(
                tenantId,
                expectedRowVersion,
                cancellationToken);

            // Capture before state for audit
            var beforeSnapshot = config.ThemeLiveOverridesJson;

            // Promote Draft -> Live
            // 1. ThemePresetKey is already saved in SelectThemePresetAsync, but checking consistency
            // 2. Promote overrides
            config.ThemeLivePresetKey = config.ThemePresetKey;
            config.ThemeLiveOverridesJson = config.ThemeDraftOverridesJson;

            // If we wanted to version controls, we'd do it here.

            config.UpdatedAtUtc = DateTime.UtcNow;

            var actor = ResolveCurrentActor();

            // Create version snapshot BEFORE saving changes to DB
            CreateAndAppendVersionSnapshot(config, "Theme Published", actor);

            await PersistStoreConfigChangeAsync(
                config,
                StoreConfigPublishAction.Published,
                traceId,
                cancellationToken);

            // ============================================================
            // PHASE P3: AUDIT TRAIL
            // ============================================================
            // Audit the theme publish - failures are logged but do not propagate to caller
            // since the DB commit already succeeded
            var metadata = GetCurrentRequestMetadata();
            try
            {
                await _auditService.RecordAuditAsync(
                    tenantId,
                    AuditActionTypes.ThemePublished,
                    "theme",
                    beforeSnapshot,
                    config.ThemeLiveOverridesJson,
                    config.LiveVersion,
                    actor.UserId,
                    actor.DisplayName,
                    traceId,
                    metadata.IpAddress,
                    metadata.UserAgent,
                    cancellationToken);
            }
            catch (Exception auditEx)
            {
                _logger.LogError(
                    auditEx,
                    "ThemePublishAuditFailed | TenantId: {TenantId} | LiveVersion: {LiveVersion} | TraceId: {TraceId} | Publish succeeded but audit recording failed",
                    tenantId, config.LiveVersion, traceId);
            }

            // EMIT EVENT - Updates Storefront
            return await MapToResponseAsync(config, cancellationToken);
        }
        finally
        {
            // Always release the lock
            if (lockResult.LockToken != null)
            {
                await _publishLockService.ReleaseAsync(tenantId, lockResult.LockToken);
            }
        }
    }

    // ===================================
    // PHASE 9.6: Versioning & Rollback
    // ===================================

    /// <inheritdoc />
    public async Task<AdminStoreConfigResponse> RollbackStoreConfigAsync(
        Guid tenantId,
        int versionNumber,
        bool confirm,
        string? reason,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null)
    {
        // Rollback Confirmation Guard
        if (!confirm)
        {
            _publishTelemetry.RecordInvalidRollback(tenantId, "confirmation_missing");
            throw new InvalidOperationException("Rollback requires explicit confirmation. Set 'confirm' to true.");
        }

        _logger.LogWarning(
            "RollbackStoreConfig | TenantId: {TenantId} | Version: {Version} | Reason: {Reason} | TraceId: {TraceId}",
            tenantId, versionNumber, reason ?? "No reason provided", traceId);

        var lockResult = await _publishLockService.TryAcquireAsync(tenantId, "rollback");
        if (!lockResult.Acquired)
        {
            _logger.LogWarning("RollbackStoreConfig rejected - lock not acquired | TenantId: {TenantId}", tenantId);
            throw new InvalidOperationException(lockResult.Message ?? "Publishing already in progress.");
        }

        try
        {
            var config = await GetRequiredStoreConfigForMutationAsync(
                tenantId,
                expectedRowVersion,
                cancellationToken);

            var actor = ResolveCurrentActor();
            var requestMetadata = GetCurrentRequestMetadata();
            var beforeSnapshot = config.CustomizationJson;

            var versions = System.Text.Json.JsonSerializer.Deserialize<List<VersionSnapshot>>(config.VersionsJson ?? "[]")
                           ?? new List<VersionSnapshot>();

            var targetVersion = versions.FirstOrDefault(v => v.Version == versionNumber);
            if (targetVersion == null)
            {
                _publishTelemetry.RecordFakeVersionAttempt(tenantId);
                _publishTelemetry.RecordInvalidRollback(tenantId, "version_not_found");
                throw new InvalidOperationException($"Version {versionNumber} not found");
            }

        // Validate snapshot before proceeding with rollback
            var snapshotCustomization = ParseCustomizationRootStrict(targetVersion.CustomizationJson);
            if (snapshotCustomization == null)
            {
                _publishTelemetry.RecordInvalidRollback(tenantId, "malformed_snapshot");
                _logger.LogError(
                    "RollbackAborted | TenantId: {TenantId} | Version: {Version} | Reason: Malformed snapshot CustomizationJson | TraceId: {TraceId}",
                    tenantId, versionNumber, traceId);
                throw new InvalidOperationException($"Cannot rollback to version {versionNumber}: snapshot contains malformed CustomizationJson");
            }

            await ValidateRollbackThemeAuthorityAsync(targetVersion, cancellationToken);
            var historicalLiveCustomization = ExtractHistoricalLiveState(snapshotCustomization);

        // Restore LIVE state from snapshot while preserving only current drafts.
            config.ThemeLivePresetKey = targetVersion.ThemePresetKey;
            config.ThemeLiveOverridesJson = targetVersion.ThemeLiveOverridesJson;
            config.CustomizationJson = MergeSnapshotLiveWithCurrentDrafts(
                historicalLiveCustomization,
                config.CustomizationJson);

            config.UpdatedAtUtc = DateTime.UtcNow;

            // A rollback is a new publication derived from historical evidence. Never rewind
            // publication identity, because the next publish could otherwise reuse a version.
            CreateAndAppendVersionSnapshot(
                config,
                "Rollback",
                actor,
                restoredFromVersion: versionNumber);

            await PersistStoreConfigChangeAsync(
                config,
                StoreConfigPublishAction.RolledBack,
                traceId,
                cancellationToken,
                publishedVersion: config.LiveVersion);

            // Audit the rollback - failures are logged but do not propagate to caller
            // since the DB commit already succeeded
            try
            {
                await _auditService.RecordAuditAsync(
                    tenantId,
                    AuditActionTypes.Rollback,
                    "store-config",
                    beforeSnapshot,
                    config.CustomizationJson,
                    config.LiveVersion,
                    actor.UserId,
                    actor.DisplayName,
                    traceId,
                    requestMetadata.IpAddress,
                    requestMetadata.UserAgent,
                    cancellationToken);
            }
            catch (Exception auditEx)
            {
                _logger.LogError(
                    auditEx,
                    "RollbackAuditFailed | TenantId: {TenantId} | RestoredFromVersion: {RestoredFromVersion} | PublishedVersion: {PublishedVersion} | TraceId: {TraceId} | Rollback succeeded but audit recording failed",
                    tenantId, versionNumber, config.LiveVersion, traceId);
            }

            return await MapToResponseAsync(config, cancellationToken);
        }
        finally
        {
            if (lockResult.LockToken != null)
            {
                await _publishLockService.ReleaseAsync(tenantId, lockResult.LockToken);
            }
        }
    }

    /// <inheritdoc />
    public async Task<object> GetStoreConfigPreviewAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetStoreConfigPreview | TenantId: {TenantId} | TraceId: {TraceId}", tenantId, traceId);

        var previewResult = await _previewService.GetConfigPreviewAsync(tenantId, cancellationToken);
        if (!previewResult.Found || previewResult.Config == null)
        {
            throw new InvalidOperationException(previewResult.ErrorMessage ?? "Store config not found");
        }

        return previewResult.Config;
    }

    #region Private Helpers

    private static StoreConfig BuildDefaultConfig(Guid tenantId)
    {
        var now = DateTime.UtcNow;
        return new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "My Store",
            StoreDescription = null,
            ThemeId = "default",
            Theme = new StoreThemeConfig(),
            Behavior = new StoreBehaviorConfig(),
            StorefrontStatus = "live",
            MaintenanceMessage = null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CustomizationJson = "{\"storefrontTheme\":\"modern\"}",
            CommerceAiEnabled = false,
            CommerceAiWidgetStyle = "minimal",
            CommerceAiWidgetPosition = "bottom-right",
            CommerceAiPersonality = "friendly"
        };
    }

    private void CreateAndAppendVersionSnapshot(
        StoreConfig config,
        string changeType,
        StoreConfigActor actor,
        int? restoredFromVersion = null)
    {
        var versions = System.Text.Json.JsonSerializer.Deserialize<List<VersionSnapshot>>(config.VersionsJson ?? "[]") 
                       ?? new List<VersionSnapshot>();

        // Retention Policy: Keep last 50 versions
        const int MaxVersions = 50;
        if (versions.Count >= MaxVersions)
        {
            // remove oldest
            versions = versions.OrderByDescending(v => v.Version).Take(MaxVersions - 1).OrderBy(v => v.Version).ToList();
        }

        var newVersionNumber = (versions.Max(v => (int?)v.Version) ?? 0) + 1;

        var snapshot = new VersionSnapshot
        {
            Version = newVersionNumber,
            PublishedAt = DateTime.UtcNow,
            PublishedBy = actor.DisplayName,
            PublishedByUserId = actor.UserId,
            ChangeType = changeType,
            RestoredFromVersion = restoredFromVersion,
            ThemePresetKey = config.ThemeLivePresetKey,
            ThemeLiveOverridesJson = config.ThemeLiveOverridesJson,
            CustomizationJson = config.CustomizationJson
        };
        
        versions.Add(snapshot);
        config.VersionsJson = System.Text.Json.JsonSerializer.Serialize(versions);
        config.LiveVersion = newVersionNumber;
    }

    private async Task PersistStoreConfigChangeAsync(
        StoreConfig config,
        StoreConfigPublishAction action,
        string traceId,
        CancellationToken cancellationToken,
        int publishedVersion = 0)
    {
        try
        {
            await StageStoreConfigEventsAsync(config, action, traceId, cancellationToken, publishedVersion);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _publishTelemetry.RecordPublishSuccess(GetPublishActionName(action), "outbox_commit");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _publishTelemetry.RecordPublishFailure(GetPublishActionName(action), "outbox_commit", nameof(DbUpdateConcurrencyException));
            _logger.LogWarning(
                ex,
                "Store config concurrency conflict | Action: {Action} | TenantId: {TenantId} | ConfigId: {ConfigId} | TraceId: {TraceId}",
                action,
                config.TenantId,
                config.Id,
                traceId);
            throw new ConcurrencyConflictException("StoreConfig", config.Id, ex);
        }
        catch (Exception ex)
        {
            _publishTelemetry.RecordPublishFailure(GetPublishActionName(action), "outbox_commit", ex.GetType().Name);
            _logger.LogError(ex,
                "Store config publish staging failed | Action: {Action} | TenantId: {TenantId} | ConfigId: {ConfigId} | TraceId: {TraceId}",
                action, config.TenantId, config.Id, traceId);
            throw;
        }
    }

    private async Task StageStoreConfigEventsAsync(
        StoreConfig config,
        StoreConfigPublishAction action,
        string traceId,
        CancellationToken cancellationToken,
        int publishedVersion)
    {
        switch (action)
        {
            case StoreConfigPublishAction.Created:
                await PublishEventAsync(new StoreConfigCreatedEvent
                {
                    TenantId = config.TenantId,
                    StoreConfigId = config.Id,
                    ThemeId = config.ThemeId,
                    StoreName = config.StoreName,
                    CreatedAtUtc = config.CreatedAtUtc
                }, traceId, cancellationToken);
                _logger.LogInformation(
                    "StoreConfigCreatedEvent staged for outbox | TenantId: {TenantId} | ConfigId: {ConfigId} | TraceId: {TraceId}",
                    config.TenantId, config.Id, traceId);
                break;

            case StoreConfigPublishAction.Published:
                await PublishEventAsync(new StoreConfigPublishedEvent
                {
                    TenantId = config.TenantId,
                    StoreConfigId = config.Id,
                    ThemeId = config.ThemeId,
                    StoreName = config.StoreName,
                    UpdatedAtUtc = config.UpdatedAtUtc,
                    ConfigVersion = config.LiveVersion
                }, traceId, cancellationToken);
                _logger.LogInformation(
                    "StoreConfigPublishedEvent staged for outbox (v{Version}) | TenantId: {TenantId} | ConfigId: {ConfigId} | TraceId: {TraceId}",
                    config.LiveVersion, config.TenantId, config.Id, traceId);
                break;

            case StoreConfigPublishAction.RolledBack:
                await PublishEventAsync(new StoreConfigRolledBackEvent
                {
                    TenantId = config.TenantId,
                    StoreConfigId = config.Id,
                    ThemeId = config.ThemeId,
                    StoreName = config.StoreName,
                    UpdatedAtUtc = config.UpdatedAtUtc,
                    ConfigVersion = publishedVersion
                }, traceId, cancellationToken);
                _logger.LogInformation(
                    "StoreConfigRolledBackEvent staged for outbox (v{Version}) | TenantId: {TenantId} | ConfigId: {ConfigId} | TraceId: {TraceId}",
                    publishedVersion, config.TenantId, config.Id, traceId);
                break;

            default:
                await PublishEventAsync(new StoreConfigUpdatedEvent
                {
                    TenantId = config.TenantId,
                    StoreConfigId = config.Id,
                    ThemeId = config.ThemeId,
                    StoreName = config.StoreName,
                    UpdatedAtUtc = config.UpdatedAtUtc
                }, traceId, cancellationToken);
                _logger.LogInformation(
                    "StoreConfigUpdatedEvent staged for outbox | TenantId: {TenantId} | ConfigId: {ConfigId} | TraceId: {TraceId}",
                    config.TenantId, config.Id, traceId);
                break;
        }

        await PublishEventAsync(new TenantUpdatedEvent
        {
            TenantId = config.TenantId,
            StoreName = config.StoreName
        }, traceId, cancellationToken);
    }

    private async Task PublishEventAsync<T>(
        T @event,
        string traceId,
        CancellationToken cancellationToken)
        where T : IntegrationEvent
    {
        @event.CorrelationId = string.IsNullOrWhiteSpace(@event.CorrelationId) ? traceId : @event.CorrelationId;
        @event.IdempotencyKey = string.IsNullOrWhiteSpace(@event.IdempotencyKey)
            ? @event.EventId.ToString("N")
            : @event.IdempotencyKey;

        await _eventPublisher.PublishAsync(@event, cancellationToken);
    }

    private static string GetPublishActionName(StoreConfigPublishAction action)
    {
        return action switch
        {
            StoreConfigPublishAction.Created => "store_config_created",
            StoreConfigPublishAction.Published => "store_config_published",
            StoreConfigPublishAction.RolledBack => "store_config_rollback",
            _ => "store_config_updated"
        };
    }

    private static StoreThemeConfig MapThemeFromDto(AdminThemeConfigDto dto)
    {
        return new StoreThemeConfig
        {
            Colors = new ColorsConfig
            {
                Primary = dto.Colors.Primary,
                Secondary = dto.Colors.Secondary,
                Background = dto.Colors.Background,
                Text = dto.Colors.Text,
                Muted = dto.Colors.Muted
            },
            Font = new FontConfig
            {
                Family = dto.Font.Family,
                BaseSize = dto.Font.BaseSize
            },
            Ui = new UiConfig
            {
                BorderRadius = dto.Ui.BorderRadius,
                ButtonStyle = dto.Ui.ButtonStyle
            },
            Layout = new LayoutConfig
            {
                ProductCardVariant = dto.Layout.ProductCardVariant,
                HeaderVariant = dto.Layout.HeaderVariant
            }
        };
    }

    private static StoreBehaviorConfig MapBehaviorFromDto(AdminBehaviorConfigDto dto)
    {
        return new StoreBehaviorConfig
        {
            EnableGuestCheckout = dto.EnableGuestCheckout,
            ShowOutOfStockProducts = dto.ShowOutOfStockProducts,
            EnableSearch = dto.EnableSearch,
            // Hardening: promotion failures must never fail open.
            FailCheckoutOnPromotionError = true
        };
    }

    private async Task<AdminStoreConfigResponse> MapToResponseAsync(
        StoreConfig config,
        CancellationToken cancellationToken = default)
    {
        // Parse CustomizationJson to extract Pages for builder
        Dictionary<string, object>? pages = null;
        Dictionary<string, object>? productTemplates = null;
        var storefrontTheme = ExtractStorefrontTheme(config.CustomizationJson, config.ThemeId);
        try
        {
            var json = config.CustomizationJson ?? "{}";
            var root = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            if (root != null && root.TryGetValue("pages", out var pagesObj))
            {
                // Deserialize pages object
                var pagesJson = System.Text.Json.JsonSerializer.Serialize(pagesObj);
                var pagesDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(pagesJson);
                
                // Map each page to return DRAFT content (for builder) 
                if (pagesDict != null)
                {
                    pages = new Dictionary<string, object>();
                    foreach (var (pageKey, pageContent) in pagesDict)
                    {
                        // Return draft content for builder, or live if no draft
                        if (pageContent.TryGetValue("draft", out var draftContent))
                        {
                            pages[pageKey] = draftContent;
                        }
                        else if (pageContent.TryGetValue("live", out var liveContent))
                        {
                            pages[pageKey] = liveContent;
                        }
                        else
                        {
                            pages[pageKey] = pageContent;
                        }
                    }
                }
            }

            if (root != null && root.TryGetValue("product_templates", out var productTemplatesObj))
            {
                var templatesJson = System.Text.Json.JsonSerializer.Serialize(productTemplatesObj);
                var templatesDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(templatesJson);

                if (templatesDict != null)
                {
                    productTemplates = new Dictionary<string, object>();
                    foreach (var (templateKey, templateContent) in templatesDict)
                    {
                        if (templateContent.TryGetValue("draft", out var draftContent))
                        {
                            productTemplates[templateKey] = draftContent;
                        }
                        else if (templateContent.TryGetValue("live", out var liveContent))
                        {
                            productTemplates[templateKey] = liveContent;
                        }
                        else
                        {
                            productTemplates[templateKey] = templateContent;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore parse errors, pages will be null
        }

        var presetKey = string.IsNullOrWhiteSpace(config.ThemePresetKey) ? "default" : config.ThemePresetKey;
        var draftThemePreset = await _dbContext.ThemePresets
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == presetKey, cancellationToken);

        if (draftThemePreset == null && !string.Equals(presetKey, "default", StringComparison.OrdinalIgnoreCase))
        {
            draftThemePreset = await _dbContext.ThemePresets
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Key == "default", cancellationToken);
        }

        var livePresetKey = string.IsNullOrWhiteSpace(config.ThemeLivePresetKey) ? "default" : config.ThemeLivePresetKey;
        var liveThemePreset = string.Equals(livePresetKey, presetKey, StringComparison.OrdinalIgnoreCase)
            ? draftThemePreset
            : await _dbContext.ThemePresets
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Key == livePresetKey, cancellationToken);

        if (liveThemePreset == null && !string.Equals(livePresetKey, "default", StringComparison.OrdinalIgnoreCase))
        {
            liveThemePreset = await _dbContext.ThemePresets
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Key == "default", cancellationToken);
        }

        var draftTheme = StorefrontDraftConfigMapper.MapThemeConfig(config, draftThemePreset, useDraftOverrides: true);
        var liveTheme = StorefrontDraftConfigMapper.MapThemeConfig(config, liveThemePreset, useDraftOverrides: false);

        return new AdminStoreConfigResponse
        {
            Id = config.Id,
            TenantId = config.TenantId,
            RowVersion = config.RowVersion.ToString(CultureInfo.InvariantCulture),
            StoreName = config.StoreName,
            StoreDescription = config.StoreDescription,
            StorefrontTheme = storefrontTheme,
            ThemeId = config.ThemeId,
            Theme = new AdminThemeConfigDto
            {
                Colors = new AdminColorsConfigDto
                {
                    Primary = config.Theme.Colors.Primary,
                    Secondary = config.Theme.Colors.Secondary,
                    Background = config.Theme.Colors.Background,
                    Text = config.Theme.Colors.Text,
                    Muted = config.Theme.Colors.Muted
                },
                Font = new AdminFontConfigDto
                {
                    Family = config.Theme.Font.Family,
                    BaseSize = config.Theme.Font.BaseSize
                },
                Ui = new AdminUiConfigDto
                {
                    BorderRadius = config.Theme.Ui.BorderRadius,
                    ButtonStyle = config.Theme.Ui.ButtonStyle
                },
                Layout = new AdminLayoutConfigDto
                {
                    ProductCardVariant = config.Theme.Layout.ProductCardVariant,
                    HeaderVariant = config.Theme.Layout.HeaderVariant
                }
            },
            Behavior = new AdminBehaviorConfigDto
            {
                EnableGuestCheckout = config.Behavior.EnableGuestCheckout,
                ShowOutOfStockProducts = config.Behavior.ShowOutOfStockProducts,
                EnableSearch = config.Behavior.EnableSearch,
                FailCheckoutOnPromotionError = true
            },
            CommerceAi = new AdminCommerceAiConfigDto
            {
                Enabled = config.CommerceAiEnabled,
                WidgetStyle = config.CommerceAiWidgetStyle,
                WidgetPosition = config.CommerceAiWidgetPosition,
                AutoGreetingEnabled = config.CommerceAiAutoGreetingEnabled,
                AutoGreetingMessage = config.CommerceAiAutoGreetingMessage,
                Personality = config.CommerceAiPersonality
            },
            StorefrontStatus = config.StorefrontStatus ?? "live",
            MaintenanceMessage = config.MaintenanceMessage,
            UpdatedAtUtc = config.UpdatedAtUtc,
            CreatedAtUtc = config.CreatedAtUtc,
            ThemePresetKey = config.ThemePresetKey,
            ThemeLivePresetKey = livePresetKey,
            ThemeDraftOverridesJson = config.ThemeDraftOverridesJson ?? "{}",
            ThemeLiveOverridesJson = config.ThemeLiveOverridesJson ?? "{}",
            DraftTheme = BuildAdminThemeView(presetKey, draftTheme),
            LiveTheme = BuildAdminThemeView(livePresetKey, liveTheme),
            Pages = pages,
            ProductTemplates = productTemplates
        };
    }

    private async Task<StoreConfig> GetRequiredStoreConfigForMutationAsync(
        Guid tenantId,
        uint? expectedRowVersion,
        CancellationToken cancellationToken)
    {
        var config = await _dbContext.StoreConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (config == null)
        {
            throw new InvalidOperationException("Store config not found");
        }

        ApplyExpectedRowVersion(config, expectedRowVersion);
        return config;
    }

    private void ApplyExpectedRowVersion(StoreConfig config, uint? expectedRowVersion)
    {
        if (!expectedRowVersion.HasValue)
        {
            return;
        }

        _dbContext.Entry(config)
            .Property(x => x.RowVersion)
            .OriginalValue = expectedRowVersion.Value;
    }

    private async Task SaveDraftStoreConfigAsync(
        StoreConfig config,
        string traceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(
                ex,
                "Store config draft concurrency conflict | TenantId: {TenantId} | ConfigId: {ConfigId} | TraceId: {TraceId}",
                config.TenantId,
                config.Id,
                traceId);
            throw new ConcurrencyConflictException("StoreConfig", config.Id, ex);
        }
    }

    private async Task<(string OverridesJson, string? SelectedPresetKey)> NormalizeThemeOverridesAsync(
        string overridesJson,
        CancellationToken cancellationToken)
    {
        JsonNode? parsedNode;
        try
        {
            parsedNode = JsonNode.Parse(overridesJson);
        }
        catch
        {
            throw new ArgumentException("Invalid JSON");
        }

        if (parsedNode is not JsonObject root)
        {
            throw new ArgumentException("Invalid JSON");
        }

        string? selectedPresetKey = null;
        if (root.TryGetPropertyValue("presetKey", out var presetKeyNode))
        {
            if (presetKeyNode is not JsonValue presetValue ||
                !presetValue.TryGetValue<string>(out var rawPresetKey))
            {
                throw new ArgumentException("Theme preset key must be a string.");
            }

            selectedPresetKey = rawPresetKey?.Trim();
            if (string.IsNullOrWhiteSpace(selectedPresetKey))
            {
                throw new ArgumentException("Theme preset key is required when provided.");
            }

            var presetExists = await _dbContext.ThemePresets
                .AsNoTracking()
                .AnyAsync(p => p.Key == selectedPresetKey, cancellationToken);

            if (!presetExists)
            {
                throw new InvalidOperationException($"Theme preset '{selectedPresetKey}' not found");
            }

            root.Remove("presetKey");
        }

        ValidateThemeOverrideShape(root);
        return (root.ToJsonString(), selectedPresetKey);
    }

    private static void ValidateThemeOverrideShape(JsonObject root)
    {
        var allowedRootKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "colors",
            "typography",
            "font",
            "radius",
            "borderRadius"
        };

        foreach (var property in root)
        {
            if (!allowedRootKeys.Contains(property.Key))
            {
                throw new ArgumentException($"Theme override '{property.Key}' is not supported.");
            }
        }

        if (root["colors"] is JsonNode colorsNode)
        {
            if (colorsNode is not JsonObject colors)
            {
                throw new ArgumentException("Theme colors must be an object.");
            }

            var allowedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "primary", "secondary", "background", "text", "muted"
            };

            foreach (var color in colors)
            {
                if (!allowedColors.Contains(color.Key) ||
                    color.Value is not JsonValue colorValue ||
                    !colorValue.TryGetValue<string>(out var rawColor) ||
                    !Regex.IsMatch(rawColor ?? string.Empty, "^#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$"))
                {
                    throw new ArgumentException($"Theme color '{color.Key}' must be a supported hex color.");
                }
            }
        }

        if (root["font"] is JsonNode fontNode)
        {
            if (fontNode is not JsonValue fontValue ||
                !fontValue.TryGetValue<string>(out var font) ||
                !AdminFontConfigDto.AllowedFontFamilies.Contains(font))
            {
                throw new ArgumentException("Theme font is not supported.");
            }
        }

        if (root["typography"] is JsonNode typographyNode)
        {
            if (typographyNode is not JsonObject typography)
            {
                throw new ArgumentException("Theme typography must be an object.");
            }

            foreach (var property in typography)
            {
                if (property.Key.Equals("fontFamily", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value is not JsonValue value ||
                        !value.TryGetValue<string>(out var font) ||
                        !AdminFontConfigDto.AllowedFontFamilies.Contains(font))
                    {
                        throw new ArgumentException("Theme font family is not supported.");
                    }
                }
                else if (property.Key.Equals("baseSize", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value is not JsonValue value ||
                        !value.TryGetValue<string>(out var baseSize) ||
                        !Regex.IsMatch(baseSize ?? string.Empty, "^(?:1[2-9]|2[0-4])px$"))
                    {
                        throw new ArgumentException("Theme base size must be between 12px and 24px.");
                    }
                }
                else
                {
                    throw new ArgumentException($"Theme typography override '{property.Key}' is not supported.");
                }
            }
        }

        if (root["radius"] is JsonNode radiusNode)
        {
            if (radiusNode is not JsonObject radius ||
                radius.Count != 1 ||
                radius["base"] is not JsonValue radiusValue ||
                !radiusValue.TryGetValue<string>(out var baseRadius) ||
                !TryParseBoundedPixels(baseRadius, out _))
            {
                throw new ArgumentException("Theme radius must contain a base value between 0px and 24px.");
            }
        }

        if (root["borderRadius"] is JsonNode borderRadiusNode)
        {
            var valid = borderRadiusNode is JsonValue borderRadiusValue &&
                ((borderRadiusValue.TryGetValue<int>(out var numericRadius) &&
                  numericRadius is >= 0 and <= 24) ||
                 (borderRadiusValue.TryGetValue<string>(out var stringRadius) &&
                  TryParseBoundedPixels(stringRadius, out _)));

            if (!valid)
            {
                throw new ArgumentException("Theme border radius must be between 0 and 24 pixels.");
            }
        }
    }

    private static bool TryParseBoundedPixels(string? value, out int pixels)
    {
        pixels = 0;
        if (string.IsNullOrWhiteSpace(value) ||
            !value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(value[..^2], NumberStyles.None, CultureInfo.InvariantCulture, out pixels))
        {
            return false;
        }

        return pixels is >= 0 and <= 24;
    }

    private static AdminThemeViewDto BuildAdminThemeView(string presetKey, StorefrontThemeConfigDto theme)
    {
        return new AdminThemeViewDto
        {
            PresetKey = string.IsNullOrWhiteSpace(presetKey) ? "default" : presetKey,
            Colors = new AdminThemeViewColorsDto
            {
                Primary = theme.Colors.Primary,
                Secondary = theme.Colors.Secondary,
                Background = theme.Colors.Background,
                Text = theme.Colors.Text,
                Muted = theme.Colors.Muted
            },
            Font = theme.Font.Family,
            BorderRadius = ParseBorderRadius(theme.Ui.BorderRadius)
        };
    }

    private static int ParseBorderRadius(string? borderRadius)
    {
        if (string.IsNullOrWhiteSpace(borderRadius))
        {
            return 8;
        }

        var numericChars = new string(borderRadius
            .Where(character => char.IsDigit(character) || character == '-')
            .ToArray());

        if (!int.TryParse(numericChars, out var parsedBorderRadius))
        {
            return 8;
        }

        // Clamp negative values to 0 (negative border radius is invalid)
        return Math.Max(parsedBorderRadius, 0);
    }

    private static string ExtractStorefrontTheme(string? customizationJson, string? fallbackThemeId = null)
    {
        if (!string.IsNullOrWhiteSpace(customizationJson))
        {
            try
            {
                var root = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(customizationJson);
                if (root != null &&
                    root.TryGetValue("storefrontTheme", out var storefrontThemeObj))
                {
                    var storefrontTheme = storefrontThemeObj switch
                    {
                        string value => value,
                        System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element => element.GetString(),
                        _ => null
                    };

                    if (!string.IsNullOrWhiteSpace(storefrontTheme))
                    {
                        return StorefrontThemes.NormalizeOrDefault(storefrontTheme);
                    }
                }
            }
            catch
            {
                // Ignore malformed customization JSON and use the fallback/default.
            }
        }

        return StorefrontThemes.NormalizeOrDefault(fallbackThemeId);
    }

    private static string SetStorefrontThemeInCustomizationJson(
        string? customizationJson,
        string? requestedStorefrontTheme,
        string? fallbackThemeId = null)
    {
        var resolvedStorefrontTheme = !string.IsNullOrWhiteSpace(requestedStorefrontTheme)
            ? StorefrontThemes.NormalizeOrDefault(requestedStorefrontTheme)
            : ExtractStorefrontTheme(customizationJson, fallbackThemeId);

        Dictionary<string, object> root;
        try
        {
            root = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(customizationJson ?? "{}")
                ?? new Dictionary<string, object>();
        }
        catch
        {
            root = new Dictionary<string, object>();
        }

        root["storefrontTheme"] = resolvedStorefrontTheme;
        return System.Text.Json.JsonSerializer.Serialize(root);
    }

    private static string MergeSnapshotLiveWithCurrentDrafts(
        JsonObject snapshotCustomization,
        string? currentCustomizationJson)
    {
        var mergedRoot = snapshotCustomization.DeepClone() as JsonObject ?? new JsonObject();
        var currentRoot = ParseCustomizationRoot(currentCustomizationJson);

        PreserveDraftBranches(currentRoot, mergedRoot);
        return mergedRoot.ToJsonString();
    }

    private async Task ValidateRollbackThemeAuthorityAsync(
        VersionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshot.ThemePresetKey))
        {
            throw new InvalidOperationException(
                $"Cannot rollback to version {snapshot.Version}: snapshot theme preset is missing.");
        }

        var presetExists = await _dbContext.ThemePresets
            .AsNoTracking()
            .AnyAsync(p => p.Key == snapshot.ThemePresetKey, cancellationToken);

        if (!presetExists)
        {
            throw new InvalidOperationException(
                $"Cannot rollback to version {snapshot.Version}: theme preset '{snapshot.ThemePresetKey}' is no longer available.");
        }

        JsonNode? overrides;
        try
        {
            overrides = JsonNode.Parse(snapshot.ThemeLiveOverridesJson);
        }
        catch
        {
            throw new InvalidOperationException(
                $"Cannot rollback to version {snapshot.Version}: theme overrides are malformed.");
        }

        if (overrides is not JsonObject overridesObject)
        {
            throw new InvalidOperationException(
                $"Cannot rollback to version {snapshot.Version}: theme overrides must be an object.");
        }

        try
        {
            ValidateThemeOverrideShape(overridesObject);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Cannot rollback to version {snapshot.Version}: theme overrides no longer satisfy current theme authority.",
                ex);
        }
    }

    private static JsonObject ExtractHistoricalLiveState(JsonObject snapshot)
    {
        var liveState = snapshot.DeepClone() as JsonObject ?? new JsonObject();

        CanonicalizeVersionedContainer(liveState, "pages");
        CanonicalizeVersionedContainer(liveState, "product_templates");
        CanonicalizeNavigation(liveState);
        RemoveDraftBranches(liveState);

        return liveState;
    }

    private static void CanonicalizeVersionedContainer(JsonObject root, string containerName)
    {
        if (root[containerName] is not JsonObject container)
        {
            return;
        }

        foreach (var entry in container.ToList())
        {
            if (entry.Value is not JsonObject versionedEntry ||
                versionedEntry["live"] is not JsonObject live)
            {
                container.Remove(entry.Key);
                continue;
            }

            container[entry.Key] = new JsonObject
            {
                ["live"] = live.DeepClone()
            };
        }
    }

    private static void CanonicalizeNavigation(JsonObject root)
    {
        if (root["navigation"] is not JsonObject navigation)
        {
            return;
        }

        if (navigation["live"] is not JsonObject live)
        {
            root.Remove("navigation");
            return;
        }

        root["navigation"] = new JsonObject
        {
            ["live"] = live.DeepClone()
        };
    }

    private static void RemoveDraftBranches(JsonObject node)
    {
        node.Remove("draft");

        foreach (var (_, value) in node.ToList())
        {
            if (value is JsonObject childObject)
            {
                RemoveDraftBranches(childObject);
            }
            else if (value is JsonArray childArray)
            {
                foreach (var child in childArray.OfType<JsonObject>())
                {
                    RemoveDraftBranches(child);
                }
            }
        }
    }

    /// <summary>
    /// Parses customization JSON strictly, returning null if the JSON is malformed or invalid.
    /// Used for validating snapshot data before rollback to prevent data loss.
    /// </summary>
    private static JsonObject? ParseCustomizationRootStrict(string? customizationJson)
    {
        if (string.IsNullOrWhiteSpace(customizationJson))
        {
            // Empty/null is considered invalid for a snapshot that should contain data
            return null;
        }

        try
        {
            var parsed = JsonNode.Parse(customizationJson) as JsonObject;
            
            // Only treat parse failure as invalid; valid empty JSON objects are acceptable
            if (parsed == null)
            {
                return null;
            }
            
            return parsed;
        }
        catch
        {
            // Parse failure - malformed JSON
            return null;
        }
    }

    /// <summary>
    /// Parses customization JSON leniently, returning an empty JsonObject on failure.
    /// Used for current/draft data where an empty state is acceptable.
    /// </summary>
    private static JsonObject ParseCustomizationRoot(string? customizationJson)
    {
        if (string.IsNullOrWhiteSpace(customizationJson))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(customizationJson) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static void PreserveDraftBranches(JsonObject currentNode, JsonObject mergedNode)
    {
        foreach (var (propertyName, currentValue) in currentNode)
        {
            if (currentValue is not JsonObject currentObject)
            {
                continue;
            }

            if (currentObject.TryGetPropertyValue("draft", out var currentDraft))
            {
                var mergedObject = mergedNode[propertyName] as JsonObject ?? new JsonObject();
                mergedObject["draft"] = currentDraft?.DeepClone();

                if (!mergedObject.ContainsKey("live"))
                {
                    // Preserve the current draft while making the missing live branch explicit.
                    mergedObject["live"] = null;
                }

                mergedNode[propertyName] = mergedObject;
            }

            if (!ContainsDraftBranch(currentObject))
            {
                continue;
            }

            var nextMergedObject = mergedNode[propertyName] as JsonObject ?? new JsonObject();
            mergedNode[propertyName] = nextMergedObject;
            PreserveDraftBranches(currentObject, nextMergedObject);
        }
    }

    private static bool ContainsDraftBranch(JsonObject node)
    {
        if (node.ContainsKey("draft"))
        {
            return true;
        }

        foreach (var (_, value) in node)
        {
            if (value is JsonObject childObject && ContainsDraftBranch(childObject))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetProductTemplateKey(string pageId, out string templateId)
    {
        const string prefix = "product-template:";
        if (pageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            templateId = pageId[prefix.Length..];
            return !string.IsNullOrWhiteSpace(templateId);
        }

        templateId = string.Empty;
        return false;
    }

    private static Dictionary<string, Dictionary<string, object>> GetCustomizationNode(
        Dictionary<string, object> root,
        string key)
    {
        if (!root.TryGetValue(key, out var node))
        {
            return new Dictionary<string, Dictionary<string, object>>();
        }

        var nodeJson = System.Text.Json.JsonSerializer.Serialize(node);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(nodeJson)
               ?? new Dictionary<string, Dictionary<string, object>>();
    }

    private static Dictionary<string, object> NormalizeBuilderPageComposition(
        Dictionary<string, object> pageConfig,
        Dictionary<string, object>? currentContent,
        string? authoritativeTemplateId = null)
    {
        var normalized = new Dictionary<string, object>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(authoritativeTemplateId))
        {
            normalized["templateId"] = authoritativeTemplateId;
        }
        else if (TryReadString(pageConfig, "templateId", out var templateId))
        {
            normalized["templateId"] = templateId;
        }

        if (TryReadString(pageConfig, "name", out var name))
        {
            normalized["name"] = name.Length > 100 ? name[..100] : name;
        }

        var sectionsNode = TryGetJsonNode(pageConfig, "sections")
            ?? throw new ArgumentException("Page layout sections are required.");

        if (sectionsNode is not JsonArray sectionsArray)
        {
            throw new ArgumentException("Page layout sections must be an array.");
        }

        var knownSectionIds = ExtractAcceptedSectionIds(currentContent);
        var usedSectionIds = new HashSet<string>(StringComparer.Ordinal);
        var normalizedSections = new List<Dictionary<string, object?>>();

        foreach (var sectionNode in sectionsArray)
        {
            if (sectionNode is not JsonObject sectionObject)
            {
                throw new ArgumentException("Each page layout section must be an object.");
            }

            var sectionType = ReadRequiredSectionType(sectionObject);
            var candidateId = TryReadString(sectionObject, "id", out var submittedId)
                ? submittedId
                : null;
            var acceptedId = IsReusableAcceptedSectionId(candidateId, knownSectionIds, usedSectionIds)
                ? candidateId!
                : GenerateBackendSectionId(usedSectionIds);

            usedSectionIds.Add(acceptedId);

            normalizedSections.Add(new Dictionary<string, object?>
            {
                ["id"] = acceptedId,
                ["type"] = sectionType,
                ["order"] = normalizedSections.Count,
                ["enabled"] = ReadOptionalBoolean(sectionObject, "enabled") ?? true,
                ["settings"] = NormalizeSectionSettings(sectionObject, sectionType)
            });
        }

        normalized["sections"] = normalizedSections;
        return normalized;
    }

    private static string ReadRequiredSectionType(JsonObject sectionObject)
    {
        if (!TryReadString(sectionObject, "type", out var sectionType))
        {
            throw new ArgumentException("Section type is required.");
        }

        if (!AllowedBuilderSectionTypes.Contains(sectionType))
        {
            throw new ArgumentException($"Section type '{sectionType}' is not supported.");
        }

        return sectionType;
    }

    private static object NormalizeSectionSettings(JsonObject sectionObject, string sectionType)
    {
        if (sectionObject["settings"] is not JsonObject settingsObject)
        {
            return new Dictionary<string, object>();
        }

        if (!AllowedBuilderSectionSettingKeys.TryGetValue(sectionType, out var allowedKeys))
        {
            return new Dictionary<string, object>();
        }

        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var setting in settingsObject)
        {
            if (!allowedKeys.Contains(setting.Key) || ReservedBuilderLayoutAuthorityKeys.Contains(setting.Key))
            {
                continue;
            }

            var normalizedValue = NormalizeSectionSettingValue(sectionType, setting.Key, setting.Value);
            if (normalizedValue != null)
            {
                normalized[setting.Key] = normalizedValue;
            }
        }

        return normalized;
    }

    private static object? NormalizeSectionSettingValue(string sectionType, string key, JsonNode? value)
    {
        if (value == null)
        {
            return null;
        }

        if (IsLayoutIntegerSetting(key))
        {
            var fallback = key.Equals("columns", StringComparison.OrdinalIgnoreCase) ? 3 : 1;
            var max = key.Equals("maxImages", StringComparison.OrdinalIgnoreCase) ? 12 : 6;
            return Clamp(ReadInteger(value, fallback), 1, max);
        }

        if (key.Equals("limit", StringComparison.OrdinalIgnoreCase))
        {
            return Clamp(ReadInteger(value, 4), 1, 24);
        }

        if (key.Equals("overlayOpacity", StringComparison.OrdinalIgnoreCase))
        {
            return Clamp(ReadInteger(value, 45), 0, 100);
        }

        if (key.Equals("alignment", StringComparison.OrdinalIgnoreCase))
        {
            return ReadAllowedString(value, "center", "left", "center", "right");
        }

        if (key.Equals("height", StringComparison.OrdinalIgnoreCase))
        {
            return ReadAllowedString(value, "medium", "small", "medium", "large", "full");
        }

        if (key.Equals("maxWidth", StringComparison.OrdinalIgnoreCase))
        {
            return ReadAllowedString(value, "full", "sm", "md", "lg", "xl", "full");
        }

        if (key.Equals("layout", StringComparison.OrdinalIgnoreCase))
        {
            return sectionType.Equals("testimonials", StringComparison.OrdinalIgnoreCase)
                ? ReadAllowedString(value, "carousel", "carousel", "grid")
                : ReadAllowedString(value, "single", "single", "grid", "carousel", "masonry");
        }

        if (key.Equals("cardStyle", StringComparison.OrdinalIgnoreCase))
        {
            return ReadAllowedString(value, "overlay", "overlay", "card");
        }

        if (key.Equals("productSource", StringComparison.OrdinalIgnoreCase))
        {
            return ReadAllowedString(value, "featured", "featured", "latest", "category", "manual");
        }

        if (key.Equals("displayStyle", StringComparison.OrdinalIgnoreCase))
        {
            return ReadAllowedString(value, "carousel", "carousel", "grid");
        }

        if (IsBooleanSetting(key))
        {
            return ReadBoolean(value, fallback: false);
        }

        if (IsColorSetting(key))
        {
            return ReadColor(value);
        }

        if (IsUrlSetting(key))
        {
            return ReadSafeUrl(value);
        }

        if (key.Equals("productIds", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("categoryIds", StringComparison.OrdinalIgnoreCase))
        {
            return ReadStringArray(value, maxItems: 50);
        }

        if (key.Equals("images", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeImageList(value, maxItems: 24);
        }

        if (key.Equals("testimonials", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeTestimonials(value);
        }

        if (key.Equals("items", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeSpecItems(value);
        }

        if (key.Equals("reviews", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeCustomerReviews(value);
        }

        if (IsRichContentSetting(key))
        {
            return ReadSanitizedRichText(value, BuilderLongTextMaxLength);
        }

        if (IsPlainTextSetting(key))
        {
            return ReadPlainText(value, BuilderShortTextMaxLength);
        }

        var sanitized = SanitizeBuilderSettingNode(value);
        return sanitized == null
            ? null
            : System.Text.Json.JsonSerializer.Deserialize<object>(sanitized.ToJsonString());
    }

    private static bool IsPlainTextSetting(string key)
    {
        return key.Equals("title", StringComparison.OrdinalIgnoreCase)
            || key.Equals("subtitle", StringComparison.OrdinalIgnoreCase)
            || key.Equals("heading", StringComparison.OrdinalIgnoreCase)
            || key.Equals("subheading", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ctaText", StringComparison.OrdinalIgnoreCase)
            || key.Equals("buttonText", StringComparison.OrdinalIgnoreCase)
            || key.Equals("badgeText", StringComparison.OrdinalIgnoreCase)
            || key.Equals("name", StringComparison.OrdinalIgnoreCase)
            || key.Equals("label", StringComparison.OrdinalIgnoreCase)
            || key.Equals("value", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRichContentSetting(string key)
    {
        return key.Equals("content", StringComparison.OrdinalIgnoreCase)
            || key.Equals("text", StringComparison.OrdinalIgnoreCase)
            || key.Equals("html", StringComparison.OrdinalIgnoreCase)
            || key.Equals("markdown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBooleanSetting(string key)
    {
        return key.StartsWith("show", StringComparison.OrdinalIgnoreCase)
            || key.Equals("autoplay", StringComparison.OrdinalIgnoreCase)
            || key.Equals("fullWidth", StringComparison.OrdinalIgnoreCase)
            || key.Equals("useDescription", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsColorSetting(string key)
    {
        return key.EndsWith("Color", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUrlSetting(string key)
    {
        return key.Equals("backgroundImage", StringComparison.OrdinalIgnoreCase)
            || key.Equals("videoUrl", StringComparison.OrdinalIgnoreCase)
            || key.Equals("avatar", StringComparison.OrdinalIgnoreCase)
            || key.Equals("url", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("Link", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadPlainText(JsonNode value, int maxLength)
    {
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var raw))
        {
            return null;
        }

        var sanitized = SafeRichTextSanitizer.Sanitize(raw).Trim();
        return Truncate(sanitized, maxLength);
    }

    private static string? ReadSanitizedRichText(JsonNode value, int maxLength)
    {
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var raw))
        {
            return null;
        }

        return Truncate(SafeRichTextSanitizer.Sanitize(raw), maxLength);
    }

    private static string? ReadSafeUrl(JsonNode value)
    {
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var raw))
        {
            return null;
        }

        var candidate = Truncate(raw.Trim(), BuilderUrlMaxLength);
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.StartsWith("//", StringComparison.Ordinal) ||
            candidate.Contains('\r') ||
            candidate.Contains('\n'))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                   absoluteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                ? candidate
                : string.Empty;
        }

        return Uri.TryCreate(candidate, UriKind.Relative, out _) ? candidate : string.Empty;
    }

    private static string ReadColor(JsonNode value)
    {
        if (value is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var raw) &&
            HexColorRegex.IsMatch(raw.Trim()))
        {
            return raw.Trim();
        }

        return "#ffffff";
    }

    private static bool ReadBoolean(JsonNode value, bool fallback)
    {
        return value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var result)
            ? result
            : fallback;
    }

    private static List<string> ReadStringArray(JsonNode value, int maxItems)
    {
        if (value is not JsonArray array)
        {
            return new List<string>();
        }

        return array
            .OfType<JsonValue>()
            .Select(item => item.TryGetValue<string>(out var raw) ? Truncate(raw.Trim(), BuilderIdMaxLength) : string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList();
    }

    private static List<Dictionary<string, object?>> NormalizeImageList(JsonNode value, int maxItems)
    {
        if (value is not JsonArray array)
        {
            return new List<Dictionary<string, object?>>();
        }

        var items = new List<Dictionary<string, object?>>();
        foreach (var item in array.Take(maxItems))
        {
            string? url = null;
            string? alt = null;

            if (item is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var rawUrl))
            {
                url = ReadSafeUrl(JsonValue.Create(rawUrl)!);
            }
            else if (item is JsonObject image)
            {
                url = ReadSafeUrl(image["url"] ?? JsonValue.Create(string.Empty)!);
                alt = ReadPlainText(image["alt"] ?? JsonValue.Create(string.Empty)!, BuilderShortTextMaxLength);
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                items.Add(new Dictionary<string, object?>
                {
                    ["url"] = url,
                    ["alt"] = alt ?? string.Empty
                });
            }
        }

        return items;
    }

    private static List<Dictionary<string, object?>> NormalizeTestimonials(JsonNode value)
    {
        if (value is not JsonArray array)
        {
            return new List<Dictionary<string, object?>>();
        }

        return array.OfType<JsonObject>().Take(20).Select(item => new Dictionary<string, object?>
        {
            ["name"] = ReadPlainText(item["name"] ?? JsonValue.Create(string.Empty)!, BuilderShortTextMaxLength) ?? string.Empty,
            ["role"] = ReadPlainText(item["role"] ?? JsonValue.Create(string.Empty)!, BuilderShortTextMaxLength) ?? string.Empty,
            ["content"] = ReadSanitizedRichText(item["content"] ?? item["text"] ?? JsonValue.Create(string.Empty)!, 1000) ?? string.Empty,
            ["rating"] = Clamp(ReadInteger(item["rating"] ?? JsonValue.Create(5)!, 5), 1, 5),
            ["avatar"] = ReadSafeUrl(item["avatar"] ?? JsonValue.Create(string.Empty)!) ?? string.Empty
        }).ToList();
    }

    private static List<Dictionary<string, object?>> NormalizeSpecItems(JsonNode value)
    {
        if (value is not JsonArray array)
        {
            return new List<Dictionary<string, object?>>();
        }

        return array.OfType<JsonObject>().Take(50).Select(item => new Dictionary<string, object?>
        {
            ["label"] = ReadPlainText(item["label"] ?? JsonValue.Create(string.Empty)!, BuilderShortTextMaxLength) ?? string.Empty,
            ["value"] = ReadPlainText(item["value"] ?? JsonValue.Create(string.Empty)!, BuilderShortTextMaxLength) ?? string.Empty
        }).ToList();
    }

    private static List<Dictionary<string, object?>> NormalizeCustomerReviews(JsonNode value)
    {
        if (value is not JsonArray array)
        {
            return new List<Dictionary<string, object?>>();
        }

        return array.OfType<JsonObject>().Take(20).Select(item => new Dictionary<string, object?>
        {
            ["name"] = ReadPlainText(item["name"] ?? JsonValue.Create(string.Empty)!, BuilderShortTextMaxLength) ?? string.Empty,
            ["rating"] = Clamp(ReadInteger(item["rating"] ?? JsonValue.Create(5)!, 5), 1, 5),
            ["text"] = ReadSanitizedRichText(item["text"] ?? JsonValue.Create(string.Empty)!, 1000) ?? string.Empty,
            ["avatar"] = ReadSafeUrl(item["avatar"] ?? JsonValue.Create(string.Empty)!) ?? string.Empty
        }).ToList();
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static JsonNode? SanitizeBuilderSettingNode(JsonNode value, int depth = 0)
    {
        if (depth > 8)
        {
            return null;
        }

        if (value is JsonValue)
        {
            return value.DeepClone();
        }

        if (value is JsonArray array)
        {
            var sanitizedArray = new JsonArray();
            foreach (var item in array)
            {
                var sanitizedItem = item == null ? null : SanitizeBuilderSettingNode(item, depth + 1);
                sanitizedArray.Add(sanitizedItem);
            }

            return sanitizedArray;
        }

        if (value is JsonObject obj)
        {
            var sanitizedObject = new JsonObject();
            foreach (var property in obj)
            {
                if (ReservedBuilderLayoutAuthorityKeys.Contains(property.Key))
                {
                    continue;
                }

                var sanitizedProperty = property.Value == null
                    ? null
                    : SanitizeBuilderSettingNode(property.Value, depth + 1);
                sanitizedObject[property.Key] = sanitizedProperty;
            }

            return sanitizedObject;
        }

        return null;
    }

    private static bool IsLayoutIntegerSetting(string key)
    {
        return key.Equals("columns", StringComparison.OrdinalIgnoreCase)
            || key.Equals("rows", StringComparison.OrdinalIgnoreCase)
            || key.Equals("maxImages", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadInteger(JsonNode value, int fallback)
    {
        return value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var result)
            ? result
            : fallback;
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(Math.Max(value, min), max);
    }

    private static string ReadAllowedString(JsonNode value, string fallback, params string[] allowedValues)
    {
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var raw))
        {
            return fallback;
        }

        return allowedValues.Contains(raw, StringComparer.OrdinalIgnoreCase)
            ? raw
            : fallback;
    }

    private static HashSet<string> SettingKeys(params string[] keys)
    {
        return new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ExtractAcceptedSectionIds(Dictionary<string, object>? currentContent)
    {
        var sectionIds = new HashSet<string>(StringComparer.Ordinal);

        if (currentContent == null)
        {
            return sectionIds;
        }

        ExtractAcceptedSectionIds(currentContent, "draft", sectionIds);
        ExtractAcceptedSectionIds(currentContent, "live", sectionIds);
        return sectionIds;
    }

    private static void ExtractAcceptedSectionIds(
        Dictionary<string, object> currentContent,
        string branchName,
        HashSet<string> sectionIds)
    {
        if (!currentContent.TryGetValue(branchName, out var branch))
        {
            return;
        }

        var branchNode = System.Text.Json.JsonSerializer.SerializeToNode(branch);
        if (branchNode?["sections"] is not JsonArray sections)
        {
            return;
        }

        foreach (var section in sections.OfType<JsonObject>())
        {
            if (TryReadString(section, "id", out var sectionId) && BuilderSectionIdRegex.IsMatch(sectionId))
            {
                sectionIds.Add(sectionId);
            }
        }
    }

    private static bool IsReusableAcceptedSectionId(
        string? candidateId,
        HashSet<string> knownSectionIds,
        HashSet<string> usedSectionIds)
    {
        return !string.IsNullOrWhiteSpace(candidateId)
            && BuilderSectionIdRegex.IsMatch(candidateId)
            && knownSectionIds.Contains(candidateId)
            && !usedSectionIds.Contains(candidateId);
    }

    private static string GenerateBackendSectionId(HashSet<string> usedSectionIds)
    {
        string sectionId;
        do
        {
            sectionId = $"section_{Guid.NewGuid():N}";
        }
        while (usedSectionIds.Contains(sectionId));

        return sectionId;
    }

    private static JsonNode? TryGetJsonNode(Dictionary<string, object> source, string key)
    {
        return source.TryGetValue(key, out var value)
            ? System.Text.Json.JsonSerializer.SerializeToNode(value)
            : null;
    }

    private static bool TryReadString(Dictionary<string, object> source, string key, out string value)
    {
        value = string.Empty;
        var node = TryGetJsonNode(source, key);
        return TryReadString(node, out value);
    }

    private static bool TryReadString(JsonObject source, string key, out string value)
    {
        value = string.Empty;
        return TryReadString(source[key], out value);
    }

    private static bool TryReadString(JsonNode? node, out string value)
    {
        value = string.Empty;
        if (node == null)
        {
            return false;
        }

        try
        {
            var candidate = node.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            value = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool? ReadOptionalBoolean(JsonObject source, string key)
    {
        var node = source[key];
        if (node == null)
        {
            return null;
        }

        try
        {
            return node.GetValue<bool>();
        }
        catch
        {
            return null;
        }
    }

    private class VersionSnapshot
    {
        public int Version { get; set; }
        public DateTime PublishedAt { get; set; }
        public string PublishedBy { get; set; } = "System";
        public string? PublishedByUserId { get; set; }
        public string ChangeType { get; set; } = "Unknown";
        public int? RestoredFromVersion { get; set; }
        public string ThemePresetKey { get; set; } = string.Empty;
        public string ThemeLiveOverridesJson { get; set; } = "{}";
        public string CustomizationJson { get; set; } = "{}";
    }

    private enum StoreConfigPublishAction
    {
        Created,
        Updated,
        Published,
        RolledBack
    }

    #endregion

    // ===================================
    // PHASE: Storefront Admin Hardening - P1
    // Version History API
    // ===================================

    /// <inheritdoc />
    public async Task<VersionHistoryResponse> GetVersionHistoryAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetVersionHistory | TenantId: {TenantId} | TraceId: {TraceId}", tenantId, traceId);

        var config = await _dbContext.StoreConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (config == null)
        {
            return new VersionHistoryResponse
            {
                CurrentVersion = 0,
                TotalVersions = 0,
                Versions = new List<VersionHistoryEntry>()
            };
        }

        // Parse stored versions from VersionsJson
        var snapshots = System.Text.Json.JsonSerializer.Deserialize<List<VersionSnapshot>>(config.VersionsJson ?? "[]")
                        ?? new List<VersionSnapshot>();

        // Map to response DTOs (newest first)
        var entries = snapshots
            .OrderByDescending(s => s.Version)
            .Select(s => new VersionHistoryEntry
            {
                Version = s.Version,
                PublishedAt = s.PublishedAt,
                PublishedBy = ResolvePublishedByDisplay(s),
                PublishedByUserId = s.PublishedByUserId,
                ChangeType = s.ChangeType,
                RestoredFromVersion = s.RestoredFromVersion,
                ChangeSummary = GetChangeSummary(s),
                IsCurrent = s.Version == config.LiveVersion
            })
            .ToList();

        return new VersionHistoryResponse
        {
            CurrentVersion = config.LiveVersion,
            TotalVersions = entries.Count,
            Versions = entries
        };
    }

    /// <summary>
    /// Generates a human-readable summary of what changed in a version.
    /// </summary>
    private static string GetChangeSummary(VersionSnapshot snapshot)
    {
        var parts = new List<string>();

        if (snapshot.RestoredFromVersion.HasValue)
        {
            parts.Add($"Restored from version {snapshot.RestoredFromVersion.Value}");
        }

        if (!string.IsNullOrEmpty(snapshot.ThemePresetKey))
        {
            parts.Add($"Theme: {snapshot.ThemePresetKey}");
        }

        // Count CMS pages in customization
        try
        {
            var customization = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(snapshot.CustomizationJson ?? "{}");
            if (customization != null)
            {
                if (customization.ContainsKey("pages"))
                {
                    var pagesJson = System.Text.Json.JsonSerializer.Serialize(customization["pages"]);
                    var pagesDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(pagesJson);
                    
                    // Check for CMS pages
                    if (pagesDict?.ContainsKey("cms") == true)
                    {
                        var cmsJson = System.Text.Json.JsonSerializer.Serialize(pagesDict["cms"]);
                        var cmsPages = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(cmsJson);
                        if (cmsPages?.Count > 0)
                        {
                            parts.Add($"CMS Pages: {cmsPages.Count}");
                        }
                    }
                }

                if (customization.ContainsKey("navigation"))
                {
                    parts.Add("Navigation included");
                }
            }
        }
        catch
        {
            // Ignore parse errors
        }

        return parts.Count > 0
            ? string.Join(" | ", parts)
            : snapshot.ChangeType;
    }

    private StoreConfigActor ResolveCurrentActor()
    {
        var principal = _httpContextAccessor?.HttpContext?.User;
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = principal?.FindFirstValue(ClaimTypes.Name);
        var email = principal?.FindFirstValue(ClaimTypes.Email);

        string displayName;
        if (!string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(email))
        {
            displayName = string.Equals(userName, email, StringComparison.OrdinalIgnoreCase)
                ? email.Trim()
                : $"{userName.Trim()} ({email.Trim()})";
        }
        else if (!string.IsNullOrWhiteSpace(email))
        {
            displayName = email.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(userName))
        {
            displayName = userName.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(userId))
        {
            displayName = userId.Trim();
        }
        else
        {
            displayName = "System";
        }

        return new StoreConfigActor(userId, displayName);
    }

    private (string? IpAddress, string? UserAgent) GetCurrentRequestMetadata()
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        return (
            httpContext?.Connection?.RemoteIpAddress?.ToString(),
            httpContext?.Request?.Headers["User-Agent"].ToString());
    }

    private static string ResolvePublishedByDisplay(VersionSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.PublishedBy) &&
            !LooksLikeTraceId(snapshot.PublishedBy))
        {
            return snapshot.PublishedBy;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.PublishedByUserId))
        {
            return snapshot.PublishedByUserId;
        }

        return "Unknown admin";
    }

    private static bool LooksLikeTraceId(string value)
    {
        var normalized = value.Trim();

        if (normalized.Length == 32 &&
            normalized.All(static c => Uri.IsHexDigit(c)))
        {
            return true;
        }

        return normalized.Length == 55 &&
               normalized.Count(static c => c == '-') == 3 &&
               normalized.All(static c => c == '-' || Uri.IsHexDigit(c));
    }

    private sealed record StoreConfigActor(string? UserId, string DisplayName);
}
