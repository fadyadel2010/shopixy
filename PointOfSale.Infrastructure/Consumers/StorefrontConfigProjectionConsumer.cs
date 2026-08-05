using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Projects Store Configuration changes from SQL (Write Model) to MongoDB (Read Model).
/// Handles all StoreConfig events: Created, Updated, Published, RolledBack.
/// 
/// Responsibilities:
/// 1. Fetch latest StoreConfig from SQL.
/// 2. Transform into StorefrontConfigDocument (MongoDB).
/// 3. Upsert to MongoDB.
/// </summary>
public class StorefrontConfigProjectionConsumer :
    IConsumer<StoreConfigUpdatedEvent>,
    IConsumer<StoreConfigPublishedEvent>,
    IConsumer<StoreConfigCreatedEvent>,
    IConsumer<StoreConfigRolledBackEvent>
{
    private const string ConsumerName = nameof(StorefrontConfigProjectionConsumer);

    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoCollection<StorefrontConfigDocument> _collection;
    private readonly StorefrontPublishTelemetry _publishTelemetry;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<StorefrontConfigProjectionConsumer> _logger;

    public StorefrontConfigProjectionConsumer(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        StorefrontPublishTelemetry publishTelemetry,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<StorefrontConfigProjectionConsumer> logger)
    {
        _dbContext = dbContext;
        _collection = mongoDatabase.GetCollection<StorefrontConfigDocument>("storefront_configs");
        _publishTelemetry = publishTelemetry;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
    }

    // Handle StoreConfigPublishedEvent (includes Version)
    public Task Consume(ConsumeContext<StoreConfigPublishedEvent> context)
    {
        _logger.LogInformation(
            "✅ CONSUMER RECEIVED StoreConfigPublishedEvent | Version: {Version} | TenantId: {TenantId} | ConfigId: {ConfigId} | MessageId: {MessageId}",
            context.Message.Version,
            context.Message.TenantId,
            context.Message.StoreConfigId,
            context.MessageId);
        return ProjectStoreConfigAsync(
            context.Message.TenantId,
            context.Message.StoreConfigId,
            context.Message.EventId,
            context.Message.OccurredAtUtc,
            context.Message.EventVersion,
            nameof(StoreConfigPublishedEvent),
            context.CancellationToken);
    }

    // Handle StoreConfigCreatedEvent
    public Task Consume(ConsumeContext<StoreConfigCreatedEvent> context)
    {
        _logger.LogInformation("Received StoreConfigCreatedEvent");
        return ProjectStoreConfigAsync(
            context.Message.TenantId,
            context.Message.StoreConfigId,
            context.Message.EventId,
            context.Message.OccurredAtUtc,
            context.Message.EventVersion,
            nameof(StoreConfigCreatedEvent),
            context.CancellationToken);
    }

    // Handle StoreConfigRolledBackEvent
    public Task Consume(ConsumeContext<StoreConfigRolledBackEvent> context)
    {
        _logger.LogInformation("Received StoreConfigRolledBackEvent (v{Version})", context.Message.Version);
        return ProjectStoreConfigAsync(
            context.Message.TenantId,
            context.Message.StoreConfigId,
            context.Message.EventId,
            context.Message.OccurredAtUtc,
            context.Message.EventVersion,
            nameof(StoreConfigRolledBackEvent),
            context.CancellationToken);
    }

    // Handle generic StoreConfigUpdatedEvent
    public Task Consume(ConsumeContext<StoreConfigUpdatedEvent> context)
    {
        _logger.LogInformation("Received StoreConfigUpdatedEvent");
        return ProjectStoreConfigAsync(
            context.Message.TenantId,
            context.Message.StoreConfigId,
            context.Message.EventId,
            context.Message.OccurredAtUtc,
            context.Message.EventVersion,
            nameof(StoreConfigUpdatedEvent),
            context.CancellationToken);
    }

    private async Task ProjectStoreConfigAsync(
        Guid? tenantId,
        Guid storeConfigId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        string eventType,
        CancellationToken cancellationToken)
    {
        if (!tenantId.HasValue)
        {
            _logger.LogError("StoreConfig event missing TenantId. Skipping projection.");
            _publishTelemetry.RecordPublishFailure(eventType, "projection", "missing_tenant");
            return;
        }

        _logger.LogInformation("Projecting StoreConfig for Tenant {TenantId} | EventType: {EventType}", tenantId, eventType);

        ProjectionEventFence? fence = null;
        var completeFence = false;
        try
        {
            var fenceKey = new ProjectionEventFenceKey(ConsumerName, eventType, storeConfigId);
            fence = await ProjectionEventFence.TryAcquireAsync(
                _dbContext,
                eventId,
                fenceKey,
                _logger,
                cancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "StoreConfig projection duplicate or active replay collapsed before projection side effects | TenantId: {TenantId} | StoreConfigId: {StoreConfigId} | EventType: {EventType} | EventId: {EventId}",
                    tenantId.Value,
                    storeConfigId,
                    eventType,
                    eventId);
                _publishTelemetry.RecordPublishSuccess(eventType, "projection");
                return;
            }

            completeFence = true;

            var lineage = await TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync(
                _dbContext,
                tenantId.Value,
                storeConfigId,
                cancellationToken);

            if (lineage == null)
            {
                await FailClosedStoreConfigProjectionAsync(
                    tenantId.Value,
                    storeConfigId,
                    eventId,
                    eventType,
                    cancellationToken);
                return;
            }

            var storeConfig = lineage.StoreConfig;
            var tenant = lineage.Tenant;

            var themePresetKey = string.IsNullOrWhiteSpace(storeConfig.ThemeLivePresetKey)
                ? "default"
                : storeConfig.ThemeLivePresetKey;
            var themePresetCandidates = await _dbContext.ThemePresets
                .AsNoTracking()
                .Where(p => p.Key == themePresetKey || p.Key == "default")
                .ToListAsync(cancellationToken);
            
            var themePreset = themePresetCandidates.FirstOrDefault(p => p.Key == themePresetKey)
                ?? themePresetCandidates.FirstOrDefault(p => p.Key == "default");

            var tenantCurrency = ResolveTenantCurrency(tenant);
            var smartOffers = await GetSmartOffersAsync(tenantId.Value, cancellationToken);
            var mongoDoc = MapToMongoDocument(storeConfig, tenant, themePreset, tenantCurrency, smartOffers);
            using var existingProjectionCursor = await _collection.FindAsync(
                Builders<StorefrontConfigDocument>.Filter.Eq(x => x.TenantId, tenantId.Value),
                new FindOptions<StorefrontConfigDocument, StorefrontConfigDocument>(),
                cancellationToken);
            var existingProjection = await existingProjectionCursor.FirstOrDefaultAsync(cancellationToken);

            if (existingProjection != null)
            {
                // Preserve the independent currency freshness fence. StoreConfig replacement
                // reads current SQL currency, but clearing this watermark would let an older
                // TenantCurrencyChangedEvent replay pass the field-specific currency filter.
                mongoDoc.LastCurrencyEventId = existingProjection.LastCurrencyEventId;
                mongoDoc.LastCurrencyEventOccurredAtUtc = existingProjection.LastCurrencyEventOccurredAtUtc;
                mongoDoc.LastCurrencyEventVersion = existingProjection.LastCurrencyEventVersion;
            }

            mongoDoc.LastEventId = eventId;
            mongoDoc.LastEventOccurredAtUtc = occurredAtUtc;
            mongoDoc.LastEventVersion = eventVersion;

            var freshFilter = BuildFreshStoreConfigEventFilter(
                tenantId.Value,
                eventId,
                occurredAtUtc,
                eventVersion);
            RequireReviewedStoreConfigMutation(
                DangerousProjectionPrimitive.MongoFullDocumentReplace,
                tenantId.Value,
                storeConfigId,
                eventId,
                occurredAtUtc,
                eventVersion,
                "SQL store config and tenant currency recomposition with freshness filter before reviewed Mongo replacement.");
            var replaceResult = await _collection.ReplaceOneAsync(
                freshFilter,
                mongoDoc,
                new ReplaceOptions { IsUpsert = false },
                cancellationToken);

            // PRODUCTION HARDENING: Validate IsAcknowledged before treating operation as successful
            if (!replaceResult.IsAcknowledged)
            {
                // Unacknowledged write - perform fallback upsert and retry
                _logger.LogWarning(
                    "StoreConfig replace not acknowledged, attempting fallback upsert | TenantId: {TenantId} | StoreConfigId: {StoreConfigId}",
                    tenantId.Value,
                    storeConfigId);
                
                try
                {
                    replaceResult = await _collection.ReplaceOneAsync(
                        freshFilter,
                        mongoDoc,
                        new ReplaceOptions { IsUpsert = true },
                        cancellationToken);

                    if (!replaceResult.IsAcknowledged)
                    {
                        _logger.LogError(
                            "StoreConfig projection fallback upsert not acknowledged | TenantId: {TenantId} | StoreConfigId: {StoreConfigId} | IsAcknowledged: {IsAcknowledged}",
                            tenantId.Value,
                            storeConfigId,
                            replaceResult.IsAcknowledged);
                        throw new InvalidOperationException(
                            $"MongoDB write not acknowledged for StoreConfig projection | TenantId: {tenantId.Value} | StoreConfigId: {storeConfigId}");
                    }
                    
                    if (replaceResult.MatchedCount == 0 && replaceResult.UpsertedId == null)
                    {
                        _logger.LogError(
                            "StoreConfig projection fallback upsert failed | TenantId: {TenantId} | StoreConfigId: {StoreConfigId} | MatchedCount: {MatchedCount} | UpsertedId: {UpsertedId}",
                            tenantId.Value,
                            storeConfigId,
                            replaceResult.MatchedCount,
                            replaceResult.UpsertedId);
                        throw new InvalidOperationException(
                            $"MongoDB upsert failed for StoreConfig projection | TenantId: {tenantId.Value} | StoreConfigId: {storeConfigId}");
                    }
                }
                catch (MongoWriteException ex) when (IsDuplicateKey(ex))
                {
                    // Duplicate key means document exists - perform guarded retry to check if this event is fresher
                    _logger.LogDebug(
                        "StoreConfig projection encountered duplicate key on fallback upsert, retrying with freshness filter | TenantId: {TenantId} | StoreConfigId: {StoreConfigId} | EventId: {EventId}",
                        tenantId.Value,
                        storeConfigId,
                        eventId);
                    
                    // Retry with freshFilter (enforces this event is fresher) and IsUpsert = false
                    var retryResult = await _collection.ReplaceOneAsync(
                        freshFilter,
                        mongoDoc,
                        new ReplaceOptions { IsUpsert = false },
                        cancellationToken);

                    if (!retryResult.IsAcknowledged)
                    {
                        _logger.LogError(
                            "StoreConfig projection guarded retry not acknowledged after duplicate key | TenantId: {TenantId} | StoreConfigId: {StoreConfigId} | IsAcknowledged: {IsAcknowledged}",
                            tenantId.Value,
                            storeConfigId,
                            retryResult.IsAcknowledged);
                        throw new InvalidOperationException(
                            $"MongoDB guarded retry not acknowledged for StoreConfig projection | TenantId: {tenantId.Value} | StoreConfigId: {storeConfigId}");
                    }

                    if (retryResult.MatchedCount == 0)
                    {
                        // Truly stale - document exists but this event is not fresher
                        _logger.LogDebug(
                            "StoreConfig projection ignored stale event (duplicate key, guarded retry matched 0) | TenantId: {TenantId} | StoreConfigId: {StoreConfigId} | EventId: {EventId}",
                            tenantId.Value,
                            storeConfigId,
                            eventId);
                        _publishTelemetry.RecordPublishSuccess(eventType, "projection");
                        return;
                    }

                    // Guarded retry succeeded - continue to fence completion.
                    _logger.LogDebug(
                        "StoreConfig projection succeeded via guarded retry after duplicate key | TenantId: {TenantId} | StoreConfigId: {StoreConfigId} | EventId: {EventId}",
                        tenantId.Value,
                        storeConfigId,
                        eventId);
                }
            }
            if (replaceResult.IsAcknowledged && replaceResult.MatchedCount == 0)
            {
                // Acknowledged but no match: insert only when the document does not exist yet.
                // If another event inserted first, retry the guarded replace so stale events cannot upsert over it.
                try
                {
                    await _collection.InsertOneAsync(mongoDoc, cancellationToken: cancellationToken);
                }
                catch (MongoWriteException ex) when (IsDuplicateKey(ex))
                {
                    replaceResult = await _collection.ReplaceOneAsync(
                        freshFilter,
                        mongoDoc,
                        new ReplaceOptions { IsUpsert = false },
                        cancellationToken);

                    if (!replaceResult.IsAcknowledged)
                    {
                        _logger.LogError(
                            "StoreConfig projection retry after duplicate key not acknowledged | TenantId: {TenantId} | StoreConfigId: {StoreConfigId} | IsAcknowledged: {IsAcknowledged}",
                            tenantId.Value,
                            storeConfigId,
                            replaceResult.IsAcknowledged);
                        throw new InvalidOperationException(
                            $"MongoDB retry not acknowledged for StoreConfig projection | TenantId: {tenantId.Value} | StoreConfigId: {storeConfigId}");
                    }

                    if (replaceResult.MatchedCount == 0)
                    {
                        _logger.LogDebug(
                            "StoreConfig projection ignored stale concurrent event | TenantId: {TenantId} | StoreConfigId: {StoreConfigId} | EventType: {EventType} | EventId: {EventId} | OccurredAtUtc: {OccurredAtUtc} | EventVersion: {EventVersion}",
                            tenantId.Value,
                            storeConfigId,
                            eventType,
                            eventId,
                            occurredAtUtc,
                            eventVersion);
                        _publishTelemetry.RecordPublishSuccess(eventType, "projection");
                        return;
                    }
                }
            }

            _logger.LogInformation("StorefrontConfig projected for Tenant {Slug} with Theme {Theme}", tenant.Slug, themePresetKey);

            _publishTelemetry.RecordProjectionLag((DateTime.UtcNow - occurredAtUtc).TotalSeconds, eventType);
            _publishTelemetry.RecordPublishSuccess(eventType, "projection");
        }
        catch (Exception ex)
        {
            completeFence = false;
            _publishTelemetry.RecordPublishFailure(eventType, "projection", ex.GetType().Name);
            _logger.LogError(ex, "Failed to project StoreConfig for Tenant {TenantId}", tenantId);
            throw;
        }
        finally
        {
            if (fence != null)
            {
                if (completeFence)
                {
                    await fence.CompleteAsync(
                        eventId,
                        tenantId,
                        eventId.ToString("N"),
                        new ProjectionEventFenceKey(ConsumerName, eventType, storeConfigId),
                        cancellationToken);
                }

                await fence.DisposeAsync();
            }
        }
    }

    private async Task FailClosedStoreConfigProjectionAsync(
        Guid tenantId,
        Guid storeConfigId,
        Guid eventId,
        string eventType,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "StoreConfig projection failed closed because current SQL tenant lineage was not proven | TenantId: {TenantId} | StoreConfigId: {StoreConfigId} | EventId: {EventId} | EventType: {EventType}",
            tenantId,
            storeConfigId,
            eventId,
            eventType);

        await _collection.DeleteOneAsync(
            Builders<StorefrontConfigDocument>.Filter.Eq(document => document.TenantId, tenantId),
            cancellationToken);

        _publishTelemetry.RecordPublishSuccess(eventType, "projection_fail_closed");
    }

    private string ResolveTenantCurrency(Tenant tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant.Currency))
        {
            _reliabilityTelemetry.RecordCurrencyProjectionMismatch("storefront_config", "tenant_currency_missing");
            throw new InvalidOperationException($"Tenant currency is not configured for tenant '{tenant.Id}'.");
        }

        return tenant.Currency.Trim().ToUpperInvariant();
    }

    private static ReviewedDerivativeProjectionMutation RequireReviewedStoreConfigMutation(
        DangerousProjectionPrimitive primitive,
        Guid tenantId,
        Guid storeConfigId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        string reviewReason)
    {
        return DerivativeProjectionMutationGuard.RequireReviewedMutation(
            new ReviewedDerivativeProjectionMutation(
                primitive,
                ConsumerName,
                nameof(StorefrontConfigDocument),
                $"{tenantId:N}:{storeConfigId:N}",
                tenantId,
                eventId,
                occurredAtUtc,
                eventVersion,
                SqlAuthorityOverlayKind.StorefrontConfig |
                SqlAuthorityOverlayKind.Checkout |
                SqlAuthorityOverlayKind.StoreMetadata,
                HasSqlAuthority: true,
                HasFreshnessGuard: true,
                FailsClosedWhenAuthorityMissing: true,
                reviewReason));
    }

    private static FilterDefinition<StorefrontConfigDocument> BuildFreshStoreConfigEventFilter(
        Guid tenantId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        var filter = Builders<StorefrontConfigDocument>.Filter;
        return filter.And(
            filter.Eq(x => x.TenantId, tenantId),
            filter.Ne(x => x.LastEventId, eventId),
            filter.Or(
                filter.Exists(x => x.LastEventVersion, false),
                filter.Lt(x => x.LastEventVersion, eventVersion),
                filter.And(
                    filter.Eq(x => x.LastEventVersion, eventVersion),
                    filter.Or(
                        filter.Exists(x => x.LastEventOccurredAtUtc, false),
                        filter.Eq(x => x.LastEventOccurredAtUtc, null),
                        filter.Lt(x => x.LastEventOccurredAtUtc, occurredAtUtc)))));
    }

    private static bool IsDuplicateKey(MongoWriteException ex)
    {
        return ex.WriteError?.Category == ServerErrorCategory.DuplicateKey ||
               ex.WriteError?.Code == 11000;
    }

    private async Task<List<StorefrontSmartOfferDocument>> GetSmartOffersAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return await _dbContext.SmartOffers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .Select(x => new StorefrontSmartOfferDocument
            {
                Id = x.Id,
                Name = x.Name,
                Status = x.Status,
                Priority = x.Priority,
                TriggerType = x.TriggerType,
                PagesScope = x.PagesScope,
                DelaySeconds = x.DelaySeconds,
                ScrollPercent = x.ScrollPercent,
                ShowOncePerSession = x.ShowOncePerSession,
                MaxDisplaysPerUser = x.MaxDisplaysPerUser,
                PopupTitle = x.PopupTitle,
                PopupDescription = x.PopupDescription,
                ButtonText = x.ButtonText,
                CouponCode = x.CouponCode,
                ImageUrl = x.ImageUrl,
                BackgroundColor = x.BackgroundColor
            })
            .ToListAsync(cancellationToken);
    }

    private StorefrontConfigDocument MapToMongoDocument(
        StoreConfig sqlConfig,
        Tenant tenant,
        ThemePreset? themePreset,
        string currency,
        List<StorefrontSmartOfferDocument> smartOffers)
    {
        var doc = new StorefrontConfigDocument
        {
            Id = sqlConfig.Id.ToString(), 
            TenantId = sqlConfig.TenantId,
            TenantSlug = tenant.Slug ?? string.Empty,
            UpdatedAt = DateTime.UtcNow,
            SchemaVersion = 1,
            
            StoreName = sqlConfig.StoreName,
            StoreDescription = sqlConfig.StoreDescription,
            StorefrontTheme = ExtractStorefrontTheme(sqlConfig),
            StorefrontStatus = sqlConfig.StorefrontStatus,
            MaintenanceMessage = sqlConfig.MaintenanceMessage,
            
            Behavior = new BehaviorConfig 
            {
                EnableGuestCheckout = sqlConfig.Behavior.EnableGuestCheckout,
                ShowOutOfStockProducts = sqlConfig.Behavior.ShowOutOfStockProducts,
                EnableSearch = sqlConfig.Behavior.EnableSearch,
                FailCheckoutOnPromotionError = true
            },

            Theme = MapTheme(sqlConfig, themePreset),
            Pages = MapPages(sqlConfig),
            ProductTemplates = MapProductTemplates(sqlConfig),
            SmartOffers = smartOffers,
            PromotionRanking = sqlConfig.PromotionRanking ?? new PromotionRankingConfig(),
            Currency = currency,

            // Phase 6: Commerce AI feature flag
            CommerceAiEnabled = sqlConfig.CommerceAiEnabled,
            // Phase 7: Commerce AI Experience Config
            CommerceAiWidgetStyle = sqlConfig.CommerceAiWidgetStyle,
            CommerceAiWidgetPosition = sqlConfig.CommerceAiWidgetPosition,
            CommerceAiAutoGreetingEnabled = sqlConfig.CommerceAiAutoGreetingEnabled,
            CommerceAiAutoGreetingMessage = sqlConfig.CommerceAiAutoGreetingMessage,
            CommerceAiPersonality = sqlConfig.CommerceAiPersonality
        };

        return doc;
    }

    private static string ExtractStorefrontTheme(StoreConfig sqlConfig)
    {
        if (string.IsNullOrWhiteSpace(sqlConfig.CustomizationJson) || sqlConfig.CustomizationJson == "{}")
        {
            return "modern";
        }

        try
        {
            using var doc = JsonDocument.Parse(sqlConfig.CustomizationJson);
            if (doc.RootElement.TryGetProperty("storefrontTheme", out var storefrontThemeProp) &&
                storefrontThemeProp.ValueKind == JsonValueKind.String)
            {
                var storefrontTheme = storefrontThemeProp.GetString();
                if (!string.IsNullOrWhiteSpace(storefrontTheme))
                {
                    return StorefrontThemes.NormalizeOrDefault(storefrontTheme);
                }
            }
        }
        catch
        {
            // Ignore malformed customization JSON and use the default storefront theme.
        }

        return StorefrontThemes.Default;
    }

    private StorefrontTheme MapTheme(StoreConfig sqlConfig, ThemePreset? themePreset)
    {
        // 1. Start with PRESET (if exists), otherwise fallback to legacy strict mapping
        StorefrontTheme theme;

        if (themePreset != null)
        {
            // Parse BaseThemeJson from Preset
            var baseTheme = JsonSerializer.Deserialize<Dictionary<string, object>>(themePreset.BaseThemeJson);
            
            // Build initial theme object structure from generic JSON
            // We need to map generic JSON to the typed StorefrontTheme structure.
            // For now, let's assume standard structure: { colors: {}, typography: {}, radius: {} }
            // But StorefrontTheme expects specific properties.
            
            // Helper to get nested value safely
            string GetVal(Dictionary<string, object> dict, string section, string key, string fallback)
            {
                if (dict != null && dict.TryGetValue(section, out var secObj) && secObj is JsonElement secElem)
                {
                   if (secElem.TryGetProperty(key, out var val)) return val.ToString();
                }
                // Handle dict<string,object> if deserialized differently
                return fallback;
            }

            // RE-Deserialize to typed object for easier mapping if structure matches
            // Or just map manually.
            
            // Simplification: We will initialize strictly from what we know, 
            // then overlay Live Overrides.
            
            theme = new StorefrontTheme
            {
                Id = themePreset.Key,
                Tokens = new ThemeTokens()
            };

            // Parse preset JSON properly
            try {
                var presetDoc = JsonDocument.Parse(themePreset.BaseThemeJson);
                var root = presetDoc.RootElement;
                
                if (root.TryGetProperty("colors", out var colors))
                {
                    foreach (var prop in colors.EnumerateObject())
                    {
                        theme.Tokens.Colors[prop.Name] = prop.Value.ToString();
                    }
                }
                if (root.TryGetProperty("typography", out var typo))
                {
                    foreach (var prop in typo.EnumerateObject())
                    {
                        theme.Tokens.Typography[prop.Name] = prop.Value.ToString();
                    }
                }
                if (root.TryGetProperty("radius", out var radius))
                {
                     foreach (var prop in radius.EnumerateObject())
                    {
                        theme.Tokens.Radius[prop.Name] = prop.Value.ToString();
                    }
                }
            } catch { /* log? */ }
        }
        else
        {
            // Legacy Fallback (Phase 7.1 behavior)
             theme = new StorefrontTheme
            {
                Id = sqlConfig.ThemeId, // "default"
                Tokens = new ThemeTokens
                {
                    Colors = new Dictionary<string, string>
                    {
                        { "primary", sqlConfig.Theme.Colors.Primary },
                        { "secondary", sqlConfig.Theme.Colors.Secondary },
                        { "background", sqlConfig.Theme.Colors.Background },
                        { "text", sqlConfig.Theme.Colors.Text },
                        { "muted", sqlConfig.Theme.Colors.Muted }
                    },
                    Typography = new Dictionary<string, string>
                    {
                        { "fontFamily", sqlConfig.Theme.Font.Family },
                        { "baseSize", sqlConfig.Theme.Font.BaseSize }
                    },
                    Radius = new Dictionary<string, string>
                    {
                        { "base", sqlConfig.Theme.Ui.BorderRadius }
                    }
                }
            };
        }

        // 2. Apply LIVE OVERRIDES (Phase 9.3)
        // Supports both nested format (colors/typography/radius) and flat format from ThemeEditorPage
        if (!string.IsNullOrWhiteSpace(sqlConfig.ThemeLiveOverridesJson) && sqlConfig.ThemeLiveOverridesJson != "{}")
        {
            try
            {
                var overrideDoc = JsonDocument.Parse(sqlConfig.ThemeLiveOverridesJson);
                var root = overrideDoc.RootElement;
                
                // Handle nested "colors" object (expected format)
                if (root.TryGetProperty("colors", out var colors) && colors.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in colors.EnumerateObject())
                    {
                        // Map ThemeEditorPage colors to storefront tokens
                        // ThemeEditorPage has: primary, background, text, accent
                        // Storefront expects: primary, secondary, background, text, muted
                        var colorValue = prop.Value.ToString();
                        theme.Tokens.Colors[prop.Name] = colorValue;
                        
                        // Also derive secondary from text if not explicitly set
                        if (prop.Name == "text" && !theme.Tokens.Colors.ContainsKey("secondary"))
                        {
                            theme.Tokens.Colors["secondary"] = colorValue;
                        }
                        // Derive muted from a lighter version logic or use text with opacity
                        if (prop.Name == "text" && !theme.Tokens.Colors.ContainsKey("muted"))
                        {
                            theme.Tokens.Colors["muted"] = "#64748b"; // Default muted gray
                        }
                    }
                }
                
                // Handle nested "typography" object (expected format)
                if (root.TryGetProperty("typography", out var typo) && typo.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in typo.EnumerateObject())
                        theme.Tokens.Typography[prop.Name] = prop.Value.ToString();
                }
                
                // Handle flat "font" property (ThemeEditorPage format)
                if (root.TryGetProperty("font", out var font) && font.ValueKind == JsonValueKind.String)
                {
                    theme.Tokens.Typography["fontFamily"] = font.GetString() ?? "Inter";
                }
                
                // Handle nested "radius" object (expected format)
                if (root.TryGetProperty("radius", out var radius) && radius.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in radius.EnumerateObject())
                        theme.Tokens.Radius[prop.Name] = prop.Value.ToString();
                }
                
                // Handle flat "borderRadius" property (ThemeEditorPage format - number)
                if (root.TryGetProperty("borderRadius", out var borderRadius))
                {
                    var borderRadiusValue = borderRadius.ValueKind == JsonValueKind.Number 
                        ? $"{borderRadius.GetInt32()}px" 
                        : borderRadius.GetString() ?? "8px";
                    theme.Tokens.Radius["base"] = borderRadiusValue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to apply theme overrides: {Message}", ex.Message);
            }
        }

        return theme;
    }

    private StorefrontPages MapPages(StoreConfig sqlConfig)
    {
        var pages = new StorefrontPages();

        if (string.IsNullOrWhiteSpace(sqlConfig.CustomizationJson) || sqlConfig.CustomizationJson == "{}")
        {
            return pages;
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using var doc = JsonDocument.Parse(sqlConfig.CustomizationJson);
            
            if (doc.RootElement.TryGetProperty("pages", out var pagesProp))
            {
                // Map Home Page
                if (pagesProp.TryGetProperty("home", out var homeProp))
                {
                    if (homeProp.TryGetProperty("live", out var liveProp))
                    {
                        var liveConfig = JsonSerializer.Deserialize<StorefrontPageConfig>(liveProp.GetRawText(), options);
                        if (liveConfig != null)
                        {
                            ConvertSectionSettings(liveConfig);
                            pages.Home = liveConfig;
                        }
                    }
                    else if (!homeProp.TryGetProperty("draft", out _) &&
                             homeProp.TryGetProperty("sections", out _)) 
                    {
                        var flatConfig = JsonSerializer.Deserialize<StorefrontPageConfig>(homeProp.GetRawText(), options);
                        if (flatConfig != null)
                        {
                            ConvertSectionSettings(flatConfig);
                            pages.Home = flatConfig;
                        }
                    }
                }

                // Map Product Page (Phase 9.5)
                if (pagesProp.TryGetProperty("product", out var productProp))
                {
                    if (productProp.TryGetProperty("live", out var liveProp))
                    {
                        var liveConfig = JsonSerializer.Deserialize<StorefrontPageConfig>(liveProp.GetRawText(), options);
                        if (liveConfig != null)
                        {
                            ConvertSectionSettings(liveConfig);
                            pages.Product = liveConfig;
                        }
                    }
                }

                // Map Category Page (Phase 9.5)
                if (pagesProp.TryGetProperty("category", out var categoryProp))
                {
                    if (categoryProp.TryGetProperty("live", out var liveProp))
                    {
                        var liveConfig = JsonSerializer.Deserialize<StorefrontPageConfig>(liveProp.GetRawText(), options);
                        if (liveConfig != null)
                        {
                            ConvertSectionSettings(liveConfig);
                            pages.Category = liveConfig;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to parse Pages JSON during projection: {Message}", ex.Message);
        }
        
        return pages;
    }

    private Dictionary<string, StorefrontProductTemplate> MapProductTemplates(StoreConfig sqlConfig)
    {
        var templates = new Dictionary<string, StorefrontProductTemplate>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(sqlConfig.CustomizationJson) || sqlConfig.CustomizationJson == "{}")
        {
            return templates;
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using var doc = JsonDocument.Parse(sqlConfig.CustomizationJson);

            if (!doc.RootElement.TryGetProperty("product_templates", out var templatesProp))
            {
                return templates;
            }

            foreach (var templateNode in templatesProp.EnumerateObject())
            {
                JsonElement contentProp;
                if (templateNode.Value.TryGetProperty("live", out var liveProp))
                {
                    if (liveProp.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    {
                        continue;
                    }

                    contentProp = liveProp;
                }
                else if (templateNode.Value.TryGetProperty("draft", out _))
                {
                    continue;
                }
                else
                {
                    contentProp = templateNode.Value;
                }

                var templateConfig = JsonSerializer.Deserialize<StorefrontProductTemplate>(contentProp.GetRawText(), options);
                if (templateConfig == null)
                {
                    continue;
                }

                templateConfig.TemplateId = string.IsNullOrWhiteSpace(templateConfig.TemplateId)
                    ? templateNode.Name
                    : templateConfig.TemplateId;

                if (templateConfig.Sections.Count > 0)
                {
                    ConvertSectionSettings(new StorefrontPageConfig { Sections = templateConfig.Sections });
                }

                templates[templateNode.Name] = templateConfig;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to parse Product Templates JSON during projection: {Message}", ex.Message);
        }

        return templates;
    }

    /// <summary>
    /// Converts JsonElement values in section Settings to primitive .NET types that MongoDB can serialize.
    /// </summary>
    private void ConvertSectionSettings(StorefrontPageConfig config)
    {
        if (config?.Sections == null) return;
        
        foreach (var section in config.Sections)
        {
            if (section.Settings == null) continue;
            
            var convertedSettings = new Dictionary<string, object>();
            foreach (var kvp in section.Settings)
            {
                convertedSettings[kvp.Key] = ConvertJsonElement(kvp.Value);
            }
            section.Settings = convertedSettings;
        }
    }

    /// <summary>
    /// Recursively converts JsonElement to proper .NET types.
    /// </summary>
    private object? ConvertJsonElement(object? value)
    {
        if (value == null) return null;
        
        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                JsonValueKind.String => jsonElement.GetString(),
                JsonValueKind.Number => jsonElement.TryGetInt64(out var l) ? l : jsonElement.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Array => ConvertJsonArray(jsonElement),
                JsonValueKind.Object => ConvertJsonObject(jsonElement),
                _ => jsonElement.ToString()
            };
        }
        
        return value;
    }

    private List<object?> ConvertJsonArray(JsonElement jsonElement)
    {
        var list = new List<object?>();
        foreach (var item in jsonElement.EnumerateArray())
        {
            list.Add(ConvertJsonElement(item));
        }
        return list;
    }

    private Dictionary<string, object?> ConvertJsonObject(JsonElement jsonElement)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in jsonElement.EnumerateObject())
        {
            dict[prop.Name] = ConvertJsonElement(prop.Value);
        }
        return dict;
    }
}

public class StorefrontConfigProjectionConsumerDefinition : ConsumerDefinition<StorefrontConfigProjectionConsumer>
{
    public StorefrontConfigProjectionConsumerDefinition()
    {
        // Explicitly name the queue to allow binding refresh/versioning
        EndpointName = "storefront-config-projection-v2";
    }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator, IConsumerConfigurator<StorefrontConfigProjectionConsumer> consumerConfigurator)
    {
        endpointConfigurator.UseMessageRetry(r => r.Exponential(
            retryLimit: 5,
            minInterval: TimeSpan.FromSeconds(1),
            maxInterval: TimeSpan.FromSeconds(30),
            intervalDelta: TimeSpan.FromSeconds(5)));
    }
}
