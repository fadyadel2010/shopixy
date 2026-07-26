using System.Security.Cryptography;
using System.Text;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Projects published storefront CMS pages from SQL to MongoDB.
/// Preview and draft content remain on the write side only.
/// </summary>
public class CmsPageProjectionConsumer :
    IConsumer<StorefrontCmsPagePublishedEvent>,
    IConsumer<StorefrontCmsPageDeletedEvent>
{
    private const string ConsumerName = nameof(CmsPageProjectionConsumer);

    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoCollection<StorefrontPageDocument> _pageCollection;
    private readonly ILogger<CmsPageProjectionConsumer> _logger;

    public CmsPageProjectionConsumer(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        ILogger<CmsPageProjectionConsumer> logger)
    {
        _dbContext = dbContext;
        _pageCollection = mongoDatabase.GetCollection<StorefrontPageDocument>("storefront_pages");
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StorefrontCmsPagePublishedEvent> context)
    {
        var evt = context.Message;
        if (!evt.TenantId.HasValue)
        {
            _logger.LogError(
                "Storefront CMS publish event missing TenantId | PageGroupId: {PageGroupId} | Slug: {Slug}",
                evt.PageGroupId,
                evt.Slug);
            return;
        }

        var tenantId = evt.TenantId.Value;
        var cancellationToken = context.CancellationToken;
        var entityId = ResolveCmsProjectionEntityId(evt.PageGroupId, evt.Slug, evt.EventId);
        var fenceKey = new ProjectionEventFenceKey(
            ConsumerName,
            nameof(StorefrontCmsPagePublishedEvent),
            entityId);

        await using var fence = await ProjectionEventFence.TryAcquireAsync(
            _dbContext,
            evt.EventId,
            fenceKey,
            _logger,
            cancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "Storefront CMS page publish duplicate or active replay collapsed before MongoDB side effects | TenantId: {TenantId} | PageGroupId: {PageGroupId} | Slug: {Slug} | EventId: {EventId}",
                tenantId,
                evt.PageGroupId,
                evt.Slug,
                evt.EventId);
            return;
        }

        var pageQuery = _dbContext.CmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId);

        // Use Slug for legacy pages (PageGroupId == Guid.Empty), otherwise use PageGroupId
        pageQuery = evt.PageGroupId == Guid.Empty
            ? pageQuery.Where(item => item.Slug == evt.Slug)
            : pageQuery.Where(item => item.PageGroupId == evt.PageGroupId);

        var page = await pageQuery
            .Where(item => !item.IsDeleted)
            .Where(item => item.IsPublished && !item.IsDraft)
            .Where(item => item.PublishedAt != null)
            .OrderByDescending(item => item.Version)
            .ThenByDescending(item => item.PublishedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (page == null)
        {
            await FailClosedCmsPageProjectionAsync(
                tenantId,
                evt.PageGroupId,
                evt.Slug,
                evt.EventId,
                evt.PublishedAtUtc,
                evt.Version,
                cancellationToken);

            await fence.CompleteAsync(
                evt.EventId,
                tenantId,
                evt.CorrelationId ?? evt.EventId.ToString("N"),
                fenceKey,
                cancellationToken);

            _logger.LogWarning(
                "Storefront CMS page publish failed closed because current SQL page authority was not proven | TenantId: {TenantId} | PageGroupId: {PageGroupId} | Slug: {Slug}",
                tenantId,
                evt.PageGroupId,
                evt.Slug);
            return;
        }

        if (!await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, tenantId, cancellationToken))
        {
            await FailClosedCmsPageProjectionAsync(
                tenantId,
                evt.PageGroupId,
                evt.Slug,
                evt.EventId,
                evt.PublishedAtUtc,
                evt.Version,
                cancellationToken);

            await fence.CompleteAsync(
                evt.EventId,
                tenantId,
                evt.CorrelationId ?? evt.EventId.ToString("N"),
                fenceKey,
                cancellationToken);

            _logger.LogWarning(
                "Storefront CMS page publish failed closed because tenant lineage is inactive | TenantId: {TenantId} | PageGroupId: {PageGroupId} | Slug: {Slug}",
                tenantId,
                evt.PageGroupId,
                evt.Slug);
            return;
        }

        // Build filter based on whether this is a legacy page or modern page
        // Legacy pages (PageGroupId == Guid.Empty) must be matched by Slug to avoid collapsing all legacy pages
        // Modern pages use PageGroupId as the immutable identifier
        FilterDefinition<StorefrontPageDocument> filter;
        if (evt.PageGroupId == Guid.Empty)
        {
            filter = Builders<StorefrontPageDocument>.Filter.And(
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.Slug, evt.Slug));
        }
        else
        {
            filter = Builders<StorefrontPageDocument>.Filter.And(
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.PageGroupId, evt.PageGroupId));
        }

        // Delete any stale slug-based projections to handle slug renames (modern pages only)
        // Legacy pages are identified by slug, so we skip this cleanup for them
        // Use evt.Slug (the old/previous slug) to find and remove stale projections when slug changes
        if (evt.PageGroupId != Guid.Empty)
        {
            var slugFilter = Builders<StorefrontPageDocument>.Filter.And(
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.Slug, evt.Slug),
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.PageGroupId, Guid.Empty));

            await TombstoneProjectionIfFreshAsync(
                slugFilter,
                tenantId,
                Guid.Empty,
                evt.Slug,
                evt.EventId,
                evt.PublishedAtUtc,
                evt.Version,
                cancellationToken);
        }

        var existing = await _pageCollection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        var document = MapToDocument(page, evt.EventId, evt.PublishedAtUtc, evt.Version);
        document.Id = existing?.Id ?? document.Id;

        var freshFilter = BuildFreshPageEventFilter(
            filter,
            evt.EventId,
            evt.PublishedAtUtc,
            evt.Version);

        RequireReviewedCmsPageMutation(
            DangerousProjectionPrimitive.MongoFullDocumentReplace,
            tenantId,
            page.PageGroupId,
            page.Slug,
            evt.EventId,
            evt.PublishedAtUtc,
            evt.Version,
            "SQL CMS page recomposition with tenant lineage and freshness filter before reviewed Mongo replacement.");
        var applied = await ReplacePageIfFreshAsync(
            freshFilter,
            document,
            isUpsert: true,
            cancellationToken);

        if (!applied)
        {
            _logger.LogInformation(
                "Storefront CMS page publish ignored as stale or duplicate | TenantId: {TenantId} | PageGroupId: {PageGroupId} | Slug: {Slug} | EventId: {EventId} | PublishedAtUtc: {PublishedAtUtc} | Version: {Version}",
                tenantId,
                evt.PageGroupId,
                evt.Slug,
                evt.EventId,
                evt.PublishedAtUtc,
                evt.Version);
            await fence.CompleteAsync(
                evt.EventId,
                tenantId,
                evt.CorrelationId ?? evt.EventId.ToString("N"),
                fenceKey,
                cancellationToken);
            return;
        }

        await fence.CompleteAsync(
            evt.EventId,
            tenantId,
            evt.CorrelationId ?? evt.EventId.ToString("N"),
            fenceKey,
            cancellationToken);

        _logger.LogInformation(
            "Storefront CMS page projected to MongoDB | TenantId: {TenantId} | PageGroupId: {PageGroupId} | Slug: {Slug} | Version: {Version}",
            tenantId,
            evt.PageGroupId,
            page.Slug,
            page.Version);
    }

    public async Task Consume(ConsumeContext<StorefrontCmsPageDeletedEvent> context)
    {
        var evt = context.Message;
        if (!evt.TenantId.HasValue)
        {
            _logger.LogError(
                "Storefront CMS delete event missing TenantId | PageGroupId: {PageGroupId} | Slug: {Slug}",
                evt.PageGroupId,
                evt.Slug);
            return;
        }

        var tenantId = evt.TenantId.Value;
        var entityId = ResolveCmsProjectionEntityId(evt.PageGroupId, evt.Slug, evt.EventId);
        var fenceKey = new ProjectionEventFenceKey(
            ConsumerName,
            nameof(StorefrontCmsPageDeletedEvent),
            entityId);

        await using var fence = await ProjectionEventFence.TryAcquireAsync(
            _dbContext,
            evt.EventId,
            fenceKey,
            _logger,
            context.CancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "Storefront CMS page delete duplicate or active replay collapsed before MongoDB side effects | TenantId: {TenantId} | PageGroupId: {PageGroupId} | Slug: {Slug} | EventId: {EventId}",
                tenantId,
                evt.PageGroupId,
                evt.Slug,
                evt.EventId);
            return;
        }

        await DeleteProjectionAsync(
            tenantId,
            evt.PageGroupId,
            evt.Slug,
            evt.EventId,
            evt.DeletedAtUtc,
            evt.EventVersion,
            context.CancellationToken);

        await fence.CompleteAsync(
            evt.EventId,
            tenantId,
            evt.CorrelationId ?? evt.EventId.ToString("N"),
            fenceKey,
            context.CancellationToken);

        _logger.LogInformation(
            "Storefront CMS page projection removed | TenantId: {TenantId} | PageGroupId: {PageGroupId} | Slug: {Slug}",
            tenantId,
            evt.PageGroupId,
            evt.Slug);
    }

    private async Task FailClosedCmsPageProjectionAsync(
        Guid tenantId,
        Guid pageGroupId,
        string slug,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        if (pageGroupId != Guid.Empty)
        {
            var pageGroupFilter = Builders<StorefrontPageDocument>.Filter.And(
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.PageGroupId, pageGroupId));

            await TombstoneProjectionIfFreshAsync(
                pageGroupFilter,
                tenantId,
                pageGroupId,
                slug,
                eventId,
                occurredAtUtc,
                eventVersion,
                cancellationToken);
        }

        if (pageGroupId == Guid.Empty)
        {
            var slugFilter = Builders<StorefrontPageDocument>.Filter.And(
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.Slug, slug),
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.PageGroupId, Guid.Empty));

            await TombstoneProjectionIfFreshAsync(
                slugFilter,
                tenantId,
                pageGroupId,
                slug,
                eventId,
                occurredAtUtc,
                eventVersion,
                cancellationToken);
        }
    }

    private async Task DeleteProjectionAsync(
        Guid tenantId,
        Guid pageGroupId,
        string slug,
        Guid eventId,
        DateTime deletedAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        // Skip PageGroupId-based deletion if it's Guid.Empty (legacy page)
        if (pageGroupId != Guid.Empty)
        {
            var pageGroupFilter = Builders<StorefrontPageDocument>.Filter.And(
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.PageGroupId, pageGroupId));

            await TombstoneProjectionIfFreshAsync(
                pageGroupFilter,
                tenantId,
                pageGroupId,
                slug,
                eventId,
                deletedAtUtc,
                eventVersion,
                cancellationToken);
        }
        
        // Only perform slug-based deletion for legacy pages (PageGroupId == Guid.Empty)
        // This prevents accidentally deleting modern pages that happen to share the same slug
        if (pageGroupId == Guid.Empty)
        {
            var slugFilter = Builders<StorefrontPageDocument>.Filter.And(
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.Slug, slug),
                Builders<StorefrontPageDocument>.Filter.Eq(item => item.PageGroupId, Guid.Empty));

            await TombstoneProjectionIfFreshAsync(
                slugFilter,
                tenantId,
                pageGroupId,
                slug,
                eventId,
                deletedAtUtc,
                eventVersion,
                cancellationToken);
        }
    }

    private async Task TombstoneProjectionIfFreshAsync(
        FilterDefinition<StorefrontPageDocument> baseFilter,
        Guid tenantId,
        Guid pageGroupId,
        string slug,
        Guid eventId,
        DateTime deletedAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        // PHASE CMS FIX: Check if document exists before tombstoning to avoid creating orphan tombstones
        // Previously used IsUpsert = true which could create tombstone documents for pages that were never projected
        var exists = await _pageCollection
            .Find(baseFilter)
            .AnyAsync(cancellationToken);

        if (!exists)
        {
            _logger.LogDebug(
                "Storefront CMS page delete skipped - no projection exists | TenantId: {TenantId} | PageGroupId: {PageGroupId} | Slug: {Slug} | EventId: {EventId}",
                tenantId,
                pageGroupId,
                slug,
                eventId);
            return;
        }

        var update = Builders<StorefrontPageDocument>.Update
            .Set(item => item.IsPublished, false)
            .Set(item => item.UpdatedAt, deletedAtUtc)
            .Set(item => item.LastEventId, eventId)
            .Set(item => item.LastEventOccurredAtUtc, deletedAtUtc)
            .Set(item => item.LastEventVersion, eventVersion);

        try
        {
            await _pageCollection.UpdateOneAsync(
                BuildFreshPageEventFilter(baseFilter, eventId, deletedAtUtc, eventVersion),
                update,
                new UpdateOptions { IsUpsert = false },
                cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogInformation(
                "Storefront CMS page delete ignored as stale or duplicate | TenantId: {TenantId} | PageGroupId: {PageGroupId} | Slug: {Slug} | EventId: {EventId}",
                tenantId,
                pageGroupId,
                slug,
                eventId);
        }
    }

    private async Task<bool> ReplacePageIfFreshAsync(
        FilterDefinition<StorefrontPageDocument> filter,
        StorefrontPageDocument document,
        bool isUpsert,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _pageCollection.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions { IsUpsert = isUpsert },
                cancellationToken);

            return result.MatchedCount > 0 || result.ModifiedCount > 0 || result.UpsertedId != null;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    private static ReviewedDerivativeProjectionMutation RequireReviewedCmsPageMutation(
        DangerousProjectionPrimitive primitive,
        Guid tenantId,
        Guid pageGroupId,
        string slug,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        string reviewReason)
    {
        return DerivativeProjectionMutationGuard.RequireReviewedMutation(
            new ReviewedDerivativeProjectionMutation(
                primitive,
                ConsumerName,
                nameof(StorefrontPageDocument),
                $"{tenantId:N}:{(pageGroupId == Guid.Empty ? slug : pageGroupId.ToString("N"))}",
                tenantId,
                eventId,
                occurredAtUtc,
                eventVersion,
                SqlAuthorityOverlayKind.StorefrontCms |
                SqlAuthorityOverlayKind.StoreMetadata |
                SqlAuthorityOverlayKind.Tombstone,
                HasSqlAuthority: true,
                HasFreshnessGuard: true,
                FailsClosedWhenAuthorityMissing: true,
                reviewReason));
    }

    private static Guid ResolveCmsProjectionEntityId(Guid pageGroupId, string slug, Guid fallbackEventId)
    {
        if (pageGroupId != Guid.Empty)
        {
            return pageGroupId;
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            return fallbackEventId;
        }

        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"storefront-cms-page:{slug.Trim().ToLowerInvariant()}"));
        return new Guid(hash);
    }

    private static FilterDefinition<StorefrontPageDocument> BuildFreshPageEventFilter(
        FilterDefinition<StorefrontPageDocument> baseFilter,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        var filter = Builders<StorefrontPageDocument>.Filter;

        return filter.And(
            baseFilter,
            filter.Ne(item => item.LastEventId, eventId),
            filter.Or(
                filter.Exists(item => item.LastEventOccurredAtUtc, false),
                filter.Eq(item => item.LastEventOccurredAtUtc, null),
                filter.Lt(item => item.LastEventOccurredAtUtc, occurredAtUtc),
                filter.And(
                    filter.Eq(item => item.LastEventOccurredAtUtc, occurredAtUtc),
                    filter.Lt(item => item.LastEventVersion, eventVersion))));
    }

    private static StorefrontPageDocument MapToDocument(
        Core.Entities.CmsPage page,
        Guid eventId,
        DateTime eventOccurredAtUtc,
        int eventVersion)
    {
        return new StorefrontPageDocument
        {
            TenantId = page.TenantId,
            PageGroupId = page.PageGroupId,
            Slug = page.Slug,
            Title = page.Title,
            ContentType = page.ContentType,
            Content = page.Content ?? string.Empty,
            Seo = new CmsPageSeo
            {
                MetaTitle = page.MetaTitle ?? page.Title,
                MetaDescription = page.MetaDescription ?? string.Empty,
                MetaKeywords = page.MetaKeywords,
                OpenGraphImageUrl = page.OpenGraphImageUrl,
                NoIndex = false
            },
            Sections = new List<CmsPageSection>(),
            IsPublished = page.IsPublished,
            DisplayOrder = 0,
            UpdatedAt = page.UpdatedAt,
            PublishedAt = page.PublishedAt,
            SchemaVersion = page.SchemaVersion,
            LastEventId = eventId,
            LastEventOccurredAtUtc = eventOccurredAtUtc,
            LastEventVersion = eventVersion
        };
    }
}

/// <summary>
/// MassTransit consumer definition for CmsPageProjectionConsumer.
/// </summary>
public class CmsPageProjectionConsumerDefinition : ConsumerDefinition<CmsPageProjectionConsumer>
{
    public CmsPageProjectionConsumerDefinition()
    {
        EndpointName = "cms-page-projection-v1";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<CmsPageProjectionConsumer> consumerConfigurator)
    {
        endpointConfigurator.UseMessageRetry(r =>
        {
            r.Exponential(
                retryLimit: 5,
                minInterval: TimeSpan.FromSeconds(1),
                maxInterval: TimeSpan.FromSeconds(16),
                intervalDelta: TimeSpan.FromSeconds(1));
            r.Handle<global::MongoDB.Driver.MongoException>();
            r.Handle<RedisException>();
            r.Handle<TimeoutException>();
        });
    }
}
