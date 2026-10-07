using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Projects Navigation changes from SQL (Write Model) to MongoDB (Read Model).
/// Handles NavigationPublishedEvent.
/// 
/// Phase 9.11: Footer & Navigation Engine
/// 
/// Responsibilities:
/// 1. Validate TenantId
/// 2. Fetch StoreConfig from PostgreSQL
/// 3. Extract navigation.header.live and navigation.footer.live
/// 4. Upsert into MongoDB storefront_navigation
/// </summary>
public class NavigationProjectionConsumer :
    IConsumer<NavigationPublishedEvent>,
    IConsumer<StoreConfigRolledBackEvent>
{
    private const string ConsumerName = nameof(NavigationProjectionConsumer);

    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoCollection<StorefrontNavigationDocument> _collection;
    private readonly ILogger<NavigationProjectionConsumer> _logger;

    public NavigationProjectionConsumer(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        ILogger<NavigationProjectionConsumer> logger)
    {
        _dbContext = dbContext;
        _collection = mongoDatabase.GetCollection<StorefrontNavigationDocument>("storefront_navigation");
        _logger = logger;
    }

    /// <summary>
    /// Handle NavigationPublishedEvent - projects LIVE navigation to MongoDB.
    /// </summary>
    public async Task Consume(ConsumeContext<NavigationPublishedEvent> context)
    {
        var @event = context.Message;
        
        _logger.LogInformation(
            "✅ NavigationProjectionConsumer received NavigationPublishedEvent | Version: {Version} | TenantId: {TenantId}",
            @event.Version, @event.TenantId);

        await ProjectNavigationAsync(@event, context.CancellationToken);
    }

    // StoreConfig rollback restores Navigation's live branch as part of the
    // existing version snapshot. Recompose the derivative projection from SQL.
    public Task Consume(ConsumeContext<StoreConfigRolledBackEvent> context)
    {
        var source = context.Message;
        return ProjectNavigationAsync(new NavigationPublishedEvent
        {
            EventId = source.EventId,
            OccurredAtUtc = source.OccurredAtUtc,
            EventVersion = source.EventVersion,
            TenantId = source.TenantId,
            StoreConfigId = source.StoreConfigId,
            CorrelationId = source.CorrelationId,
            PublishedAtUtc = source.UpdatedAtUtc
        }, context.CancellationToken, nameof(StoreConfigRolledBackEvent));
    }

    /// <summary>
    /// Core projection logic - extracts LIVE navigation from StoreConfig and upserts to MongoDB.
    /// </summary>
    private async Task ProjectNavigationAsync(
        NavigationPublishedEvent @event,
        CancellationToken cancellationToken,
        string eventType = nameof(NavigationPublishedEvent))
    {
        var tenantId = @event.TenantId;
        var storeConfigId = @event.StoreConfigId;

        if (!tenantId.HasValue)
        {
            _logger.LogError("Navigation event missing TenantId. Skipping projection.");
            return;
        }

        var fenceKey = new ProjectionEventFenceKey(
            ConsumerName,
            eventType,
            storeConfigId);

        await using var fence = await ProjectionEventFence.TryAcquireAsync(
            _dbContext,
            @event.EventId,
            fenceKey,
            _logger,
            cancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "Navigation projection duplicate or active replay collapsed before projection side effects | TenantId: {TenantId} | StoreConfigId: {StoreConfigId} | EventId: {EventId}",
                tenantId.Value,
                storeConfigId,
                @event.EventId);
            return;
        }

        try
        {
            var lineage = await TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync(
                _dbContext,
                tenantId.Value,
                storeConfigId,
                cancellationToken);

            if (lineage == null)
            {
                await FailClosedNavigationProjectionAsync(
                    tenantId.Value,
                    storeConfigId,
                    @event.EventId,
                    cancellationToken);
                await fence.CompleteAsync(
                    @event.EventId,
                    tenantId,
                    @event.CorrelationId ?? @event.EventId.ToString("N"),
                    fenceKey,
                    cancellationToken);
                return;
            }

            var storeConfig = lineage.StoreConfig;
            var tenant = lineage.Tenant;

            var navDoc = ExtractLiveNavigation(storeConfig.CustomizationJson, tenantId.Value, tenant.Slug ?? "");

            if (navDoc == null)
            {
                _logger.LogWarning(
                    "Navigation has no live content for Tenant {TenantId}. Creating default.",
                    tenantId.Value);
                
                navDoc = CreateDefaultNavigation(tenantId.Value, tenant.Slug ?? "");
            }

            // Step 5: Upsert to MongoDB - preserve existing _id if document exists
            var filter = Builders<StorefrontNavigationDocument>.Filter.Eq(x => x.TenantId, tenantId.Value);

            // Check if document already exists to preserve its _id
            var existingDoc = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
            if (existingDoc != null)
            {
                // Preserve the existing _id to avoid "immutable field '_id' was altered" error
                navDoc.Id = existingDoc.Id;
            }

            navDoc.LastEventId = @event.EventId;
            navDoc.LastEventOccurredAtUtc = @event.OccurredAtUtc;
            navDoc.LastEventVersion = @event.EventVersion;

            var freshFilter = BuildFreshNavigationEventFilter(
                tenantId.Value,
                @event.EventId,
                @event.OccurredAtUtc,
                @event.EventVersion);

            RequireReviewedNavigationMutation(
                DangerousProjectionPrimitive.MongoFullDocumentReplace,
                tenantId.Value,
                storeConfigId,
                @event.EventId,
                @event.OccurredAtUtc,
                @event.EventVersion,
                "SQL store config navigation recomposition with freshness filter before reviewed Mongo replacement.");
            var replaceResult = await _collection.ReplaceOneAsync(
                freshFilter,
                navDoc,
                new ReplaceOptions { IsUpsert = existingDoc == null },
                cancellationToken);

            if (existingDoc != null &&
                replaceResult.MatchedCount == 0 &&
                replaceResult.ModifiedCount == 0)
            {
                _logger.LogInformation(
                    "Navigation projection ignored as stale or duplicate | TenantId: {TenantId} | EventId: {EventId} | EventVersion: {EventVersion}",
                    tenantId.Value,
                    @event.EventId,
                    @event.EventVersion);
                await fence.CompleteAsync(
                    @event.EventId,
                    tenantId,
                    @event.CorrelationId ?? @event.EventId.ToString("N"),
                    fenceKey,
                    cancellationToken);
                return;
            }

            _logger.LogInformation(
                "Navigation projected to MongoDB for Tenant {TenantSlug} | IsNew: {IsNew}",
                tenant.Slug, existingDoc == null);

            await fence.CompleteAsync(
                @event.EventId,
                tenantId,
                @event.CorrelationId ?? @event.EventId.ToString("N"),
                fenceKey,
                cancellationToken);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to project navigation for Tenant {TenantId}", tenantId);
            throw; // Allow MassTransit retry
        }
    }

    private async Task FailClosedNavigationProjectionAsync(
        Guid tenantId,
        Guid storeConfigId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<StorefrontNavigationDocument>.Filter.Eq(x => x.TenantId, tenantId);
        await _collection.DeleteOneAsync(filter, cancellationToken);

        _logger.LogWarning(
            "Navigation projection failed closed because current SQL tenant/store-config lineage was not proven | TenantId: {TenantId} | StoreConfigId: {StoreConfigId} | EventId: {EventId}",
            tenantId,
            storeConfigId,
            eventId);
    }

    private static ReviewedDerivativeProjectionMutation RequireReviewedNavigationMutation(
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
                nameof(StorefrontNavigationDocument),
                $"{tenantId:N}:{storeConfigId:N}",
                tenantId,
                eventId,
                occurredAtUtc,
                eventVersion,
                SqlAuthorityOverlayKind.StorefrontConfig |
                SqlAuthorityOverlayKind.StoreMetadata |
                SqlAuthorityOverlayKind.StorefrontNavigation,
                HasSqlAuthority: true,
                HasFreshnessGuard: true,
                FailsClosedWhenAuthorityMissing: true,
                reviewReason));
    }

    /// <summary>
    /// Extracts LIVE navigation from CustomizationJson.
    /// </summary>
    private StorefrontNavigationDocument? ExtractLiveNavigation(
        string customizationJson,
        Guid tenantId,
        string tenantSlug)
    {
        if (string.IsNullOrWhiteSpace(customizationJson) || customizationJson == "{}")
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(customizationJson);
            var root = doc.RootElement;

            // Navigate: navigation -> live (or direct navigation structure)
            if (!root.TryGetProperty("navigation", out var navigation))
            {
                return null;
            }

            // Check for draft/live structure or direct structure
            JsonElement liveNav;
            if (navigation.TryGetProperty("live", out var liveNavProp))
            {
                if (liveNavProp.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                liveNav = liveNavProp;
            }
            else if (navigation.TryGetProperty("draft", out _))
            {
                return null;
            }
            else
            {
                // Direct structure (legacy support)
                liveNav = navigation;
            }

            var navDoc = new StorefrontNavigationDocument
            {
                TenantId = tenantId,
                TenantSlug = tenantSlug,
                UpdatedAt = DateTime.UtcNow
            };

            // Header links
            if (liveNav.TryGetProperty("header", out var headerProp))
            {
                navDoc.Header = ExtractNavigationMenu(headerProp);
            }

            // Footer links
            if (liveNav.TryGetProperty("footer", out var footerProp))
            {
                navDoc.Footer = ExtractNavigationMenu(footerProp);
            }

            return navDoc;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse CustomizationJson for navigation");
            return null;
        }
    }

    private static FilterDefinition<StorefrontNavigationDocument> BuildFreshNavigationEventFilter(
        Guid tenantId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        var filter = Builders<StorefrontNavigationDocument>.Filter;

        return filter.And(
            filter.Eq(document => document.TenantId, tenantId),
            filter.Ne(document => document.LastEventId, eventId),
            filter.Or(
                filter.Exists(document => document.LastEventOccurredAtUtc, false),
                filter.Eq(document => document.LastEventOccurredAtUtc, null),
                filter.Lt(document => document.LastEventOccurredAtUtc, occurredAtUtc),
                filter.And(
                    filter.Eq(document => document.LastEventOccurredAtUtc, occurredAtUtc),
                    filter.Or(
                        filter.Exists(document => document.LastEventVersion, false),
                        filter.Lt(document => document.LastEventVersion, eventVersion)))));
    }

    /// <summary>
    /// Extracts navigation menu from JSON element.
    /// </summary>
    private NavigationMenu ExtractNavigationMenu(JsonElement menuElement)
    {
        var menu = new NavigationMenu();
        if (menuElement.ValueKind == JsonValueKind.Object && menuElement.TryGetProperty("announcement", out var announcement))
            menu.Announcement = announcement.Deserialize<PointOfSale.Core.Models.HeaderAnnouncementConfig>(new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Support both { "links": [...] } and direct [...] array
        JsonElement linksElement;
        if (menuElement.TryGetProperty("links", out var linksProp))
        {
            linksElement = linksProp;
        }
        else if (menuElement.ValueKind == JsonValueKind.Array)
        {
            linksElement = menuElement;
        }
        else
        {
            return menu;
        }

        if (linksElement.ValueKind != JsonValueKind.Array)
        {
            return menu;
        }

        var order = 0;
        foreach (var linkElem in linksElement.EnumerateArray())
        {
            var link = new NavigationLink
            {
                Label = GetStringProperty(linkElem, "label", ""),
                LabelAr = GetStringProperty(linkElem, "labelAr", null),
                Type = GetStringProperty(linkElem, "type", "internal"),
                Path = GetStringProperty(linkElem, "path", null),
                Slug = GetStringProperty(linkElem, "slug", null),
                OpenInNewTab = GetBoolProperty(linkElem, "openInNewTab", false),
                Order = GetIntProperty(linkElem, "order", order++),
                Enabled = GetBoolProperty(linkElem, "enabled", true)
            };

            // Skip empty labels
            if (!string.IsNullOrWhiteSpace(link.Label))
            {
                menu.Links.Add(link);
            }
        }

        // Sort by order
        menu.Links = menu.Links.OrderBy(l => l.Order).ToList();

        return menu;
    }

    /// <summary>
    /// Creates default navigation for new tenants.
    /// </summary>
    private StorefrontNavigationDocument CreateDefaultNavigation(Guid tenantId, string tenantSlug)
    {
        return new StorefrontNavigationDocument
        {
            TenantId = tenantId,
            TenantSlug = tenantSlug,
            UpdatedAt = DateTime.UtcNow,
            Header = new NavigationMenu
            {
                Links = new List<NavigationLink>
                {
                    new() { Label = "Home", Type = "internal", Path = "/", Order = 0 },
                    new() { Label = "Shop", Type = "internal", Path = "/products", Order = 1 }
                }
            },
            Footer = new NavigationMenu
            {
                Links = new List<NavigationLink>
                {
                    new() { Label = "About Us", Type = "cms", Slug = "about", Order = 0 },
                    new() { Label = "Contact", Type = "cms", Slug = "contact", Order = 1 },
                    new() { Label = "Privacy Policy", Type = "cms", Slug = "privacy", Order = 2 },
                    new() { Label = "Terms & Conditions", Type = "cms", Slug = "terms", Order = 3 }
                }
            }
        };
    }

    #region Helper Methods

    private static string? GetStringProperty(JsonElement element, string propertyName, string? defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString() ?? defaultValue;
        }
        return defaultValue;
    }

    private static bool GetBoolProperty(JsonElement element, string propertyName, bool defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            return prop.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => defaultValue
            };
        }
        return defaultValue;
    }

    private static int GetIntProperty(JsonElement element, string propertyName, int defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            return prop.GetInt32();
        }
        return defaultValue;
    }

    #endregion
}

/// <summary>
/// MassTransit consumer definition for NavigationProjectionConsumer.
/// </summary>
public class NavigationProjectionConsumerDefinition : ConsumerDefinition<NavigationProjectionConsumer>
{
    public NavigationProjectionConsumerDefinition()
    {
        EndpointName = "navigation-projection-v1";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<NavigationProjectionConsumer> consumerConfigurator)
    {
        endpointConfigurator.UseMessageRetry(r => r.Interval(3, 1000));
    }
}
