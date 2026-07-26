using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;

namespace PointOfSale.Infrastructure.Consumers;

public sealed class StorefrontFooterProjectionConsumer : IConsumer<StorefrontFooterUpdatedEvent>
{
    private const string ConsumerName = nameof(StorefrontFooterProjectionConsumer);

    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoCollection<StorefrontFooterDocument> _footerCollection;
    private readonly ILogger<StorefrontFooterProjectionConsumer> _logger;

    public StorefrontFooterProjectionConsumer(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        ILogger<StorefrontFooterProjectionConsumer> logger)
    {
        _dbContext = dbContext;
        _footerCollection = mongoDatabase.GetCollection<StorefrontFooterDocument>("storefront_footer");
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StorefrontFooterUpdatedEvent> context)
    {
        var tenantId = context.Message.TenantId;
        var cancellationToken = context.CancellationToken;
        var eventTimestamp = context.Message.UpdatedAtUtc == default
            ? context.Message.OccurredAtUtc
            : context.Message.UpdatedAtUtc;
        var documentId = tenantId?.ToString() ?? "global";
        var fenceKey = new ProjectionEventFenceKey(
            ConsumerName,
            nameof(StorefrontFooterUpdatedEvent),
            tenantId ?? context.Message.EventId);

        var existingDocument = await _footerCollection
            .Find(Builders<StorefrontFooterDocument>.Filter.Eq(item => item.Id, documentId))
            .FirstOrDefaultAsync(cancellationToken);

        await using var fence = await ProjectionEventFence.TryAcquireAsync(
            _dbContext,
            context.Message.EventId,
            fenceKey,
            _logger,
            cancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "Storefront footer projection duplicate or active replay collapsed before MongoDB side effects | Scope: {Scope} | EventId: {EventId}",
                documentId,
                context.Message.EventId);
            return;
        }

        if (tenantId.HasValue &&
            !await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, tenantId.Value, cancellationToken))
        {
            await FailClosedFooterProjectionAsync(
                documentId,
                tenantId.Value,
                context.Message.EventId,
                cancellationToken);

            await fence.CompleteAsync(
                context.Message.EventId,
                tenantId,
                context.CorrelationId?.ToString() ?? context.Message.EventId.ToString("N"),
                fenceKey,
                cancellationToken);

            _logger.LogWarning(
                "Storefront footer projection failed closed because current SQL tenant lineage was not proven | TenantId: {TenantId} | EventId: {EventId}",
                tenantId.Value,
                context.Message.EventId);
            return;
        }

        if (ProjectionFreshnessHelper.IsExistingEventNewerOrEqual(
                existingDocument?.LastEventOccurredAtUtc,
                existingDocument?.LastEventVersion ?? 0,
                eventTimestamp,
                context.Message.EventVersion))
        {
            _logger.LogWarning(
                "Storefront footer projection ignored stale event | Scope: {Scope} | EventId: {EventId} | EventTimestamp: {EventTimestamp} | EventVersion: {EventVersion} | ExistingTimestamp: {ExistingTimestamp} | ExistingVersion: {ExistingVersion}",
                documentId,
                context.Message.EventId,
                eventTimestamp,
                context.Message.EventVersion,
                existingDocument?.LastEventOccurredAtUtc,
                existingDocument?.LastEventVersion);

            await fence.CompleteAsync(
                context.Message.EventId,
                tenantId,
                context.CorrelationId?.ToString() ?? context.Message.EventId.ToString("N"),
                fenceKey,
                cancellationToken);
            return;
        }

        var sections = await _dbContext.StorefrontFooterSections
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(section => section.TenantId == tenantId)
            .Include(section => section.Links)
            .OrderBy(section => section.SortOrder)
            .ThenBy(section => section.Title)
            .ToListAsync(cancellationToken);

        var socialLinks = await _dbContext.StorefrontSocialLinks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(link => link.TenantId == tenantId)
            .OrderBy(link => link.SortOrder)
            .ThenBy(link => link.Platform)
            .ToListAsync(cancellationToken);

        var sectionDocuments = sections.Select(section => new StorefrontFooterSectionDocument
        {
            Title = section.Title,
            TitleAr = section.TitleAr,
            SortOrder = section.SortOrder,
            IsActive = section.IsActive,
            UpdatedAt = section.UpdatedAt,
            Links = section.Links
                .OrderBy(link => link.SortOrder)
                .Select(link => new StorefrontFooterLinkDocument
                {
                    Label = link.Label,
                    LabelAr = link.LabelAr,
                    Type = link.LinkType,
                    Url = link.Url,
                    CmsPageSlug = link.CmsPageSlug,
                    OpenInNewTab = link.OpenInNewTab,
                    SortOrder = link.SortOrder,
                    IsActive = link.IsActive,
                    UpdatedAt = link.UpdatedAt
                })
                .ToList()
        }).ToList();

        var socialLinkDocuments = socialLinks.Select(link => new StorefrontSocialLinkDocument
        {
            Platform = link.Platform,
            Url = link.Url,
            Icon = link.Icon,
            SortOrder = link.SortOrder,
            IsActive = link.IsActive,
            UpdatedAt = link.UpdatedAt
        }).ToList();

        // Precompute the max UpdatedAt timestamp to avoid iteration at read time
        var allTimestamps = new List<DateTime>();
        allTimestamps.Add(eventTimestamp);
        allTimestamps.AddRange(sectionDocuments.Select(s => s.UpdatedAt));
        allTimestamps.AddRange(sectionDocuments.SelectMany(s => s.Links.Select(l => l.UpdatedAt)));
        allTimestamps.AddRange(socialLinkDocuments.Select(l => l.UpdatedAt));
        var maxUpdatedAt = allTimestamps.Max();

        var document = new StorefrontFooterDocument
        {
            Id = documentId,
            TenantId = tenantId,
            UpdatedAt = maxUpdatedAt,
            LastEventId = context.Message.EventId,
            LastEventOccurredAtUtc = eventTimestamp,
            LastEventVersion = context.Message.EventVersion,
            Sections = sectionDocuments,
            SocialLinks = socialLinkDocuments
        };

        var freshFilter = BuildFreshFooterEventFilter(
            document.Id,
            context.Message.EventId,
            eventTimestamp,
            context.Message.EventVersion);
        DerivativeProjectionMutationGuard.RequireReviewedMutation(
            new ReviewedDerivativeProjectionMutation(
                DangerousProjectionPrimitive.MongoFullDocumentReplace,
                ConsumerName,
                nameof(StorefrontFooterDocument),
                document.Id,
                tenantId,
                context.Message.EventId,
                eventTimestamp,
                context.Message.EventVersion,
                SqlAuthorityOverlayKind.StorefrontFooter,
                HasSqlAuthority: true,
                HasFreshnessGuard: true,
                FailsClosedWhenAuthorityMissing: true,
                "SQL footer recomposition with freshness filter before reviewed Mongo replacement."));
        var replaceResult = await _footerCollection.ReplaceOneAsync(
            freshFilter,
            document,
            new ReplaceOptions { IsUpsert = false },
            cancellationToken);

        if (replaceResult.MatchedCount == 0)
        {
            try
            {
                await _footerCollection.InsertOneAsync(document, cancellationToken: cancellationToken);
            }
            catch (MongoWriteException ex) when (IsDuplicateKey(ex))
            {
                replaceResult = await _footerCollection.ReplaceOneAsync(
                    freshFilter,
                    document,
                    new ReplaceOptions { IsUpsert = false },
                    cancellationToken);

                if (replaceResult.MatchedCount == 0)
                {
                    _logger.LogWarning(
                        "Storefront footer projection ignored stale concurrent event | Scope: {Scope} | EventId: {EventId} | EventTimestamp: {EventTimestamp} | EventVersion: {EventVersion}",
                        document.Id,
                        context.Message.EventId,
                        eventTimestamp,
                        context.Message.EventVersion);

                    await fence.CompleteAsync(
                        context.Message.EventId,
                        tenantId,
                        context.CorrelationId?.ToString() ?? context.Message.EventId.ToString("N"),
                        fenceKey,
                        cancellationToken);
                    return;
                }
            }
        }

        await fence.CompleteAsync(
            context.Message.EventId,
            tenantId,
            context.CorrelationId?.ToString() ?? context.Message.EventId.ToString("N"),
            fenceKey,
            cancellationToken);

        _logger.LogInformation(
            "Storefront footer projected to MongoDB | Scope: {Scope} | Sections: {SectionCount} | SocialLinks: {SocialCount}",
            tenantId?.ToString() ?? "global",
            document.Sections.Count,
            document.SocialLinks.Count);
    }

    private async Task FailClosedFooterProjectionAsync(
        string documentId,
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        await _footerCollection.DeleteOneAsync(
            Builders<StorefrontFooterDocument>.Filter.Eq(item => item.Id, documentId),
            cancellationToken);

        OperationalConvergenceTelemetry.RecordAuthorityFailure(
            "storefront_footer_projection",
            OperationalConvergenceAuthority.TenantLineage,
            OperationalConvergenceReason.TenantInactiveOrDeleted,
            tenantId);

        OperationalConvergenceTelemetry.RecordReplayDecision(
            OperationalConvergencePhase.Projection,
            "storefront_footer_projection",
            OperationalConvergenceAuthority.TenantLineage,
            OperationalConvergenceDecision.Deleted,
            OperationalConvergenceReason.TenantInactiveOrDeleted,
            tenantId);

        _logger.LogInformation(
            "Storefront footer derivative visibility deleted after tenant-lineage rejection | TenantId: {TenantId} | EventId: {EventId}",
            tenantId,
            eventId);
    }

    /// <summary>
    /// Builds a filter that accepts only "fresh" events that haven't been processed yet.
    /// Uses composite comparison (OccurredAtUtc, EventVersion) to handle concurrent events.
    /// 
    /// Accepts event if:
    /// 1. No previous event exists (first event)
    /// 2. Event timestamp is strictly newer
    /// 3. Event timestamp equals last event but version is higher OR LastEventVersion is missing
    /// 
    /// The missing LastEventVersion check ensures backward compatibility with documents
    /// created before the LastEventVersion field was added.
    /// </summary>
    private static FilterDefinition<StorefrontFooterDocument> BuildFreshFooterEventFilter(
        string documentId,
        Guid eventId,
        DateTime eventTimestamp,
        int eventVersion)
    {
        var filter = Builders<StorefrontFooterDocument>.Filter;
        return filter.And(
            filter.Eq(item => item.Id, documentId),
            filter.Ne(item => item.LastEventId, eventId),
            filter.Or(
                filter.Exists(item => item.LastEventOccurredAtUtc, false),
                filter.Eq(item => item.LastEventOccurredAtUtc, null),
                filter.Lt(item => item.LastEventOccurredAtUtc, eventTimestamp),
                filter.And(
                    filter.Eq(item => item.LastEventOccurredAtUtc, eventTimestamp),
                    filter.Or(
                        filter.Lt(item => item.LastEventVersion, eventVersion),
                        filter.Exists(item => item.LastEventVersion, false)))));
    }

    private static bool IsDuplicateKey(MongoWriteException ex)
    {
        return ex.WriteError?.Category == ServerErrorCategory.DuplicateKey ||
               ex.WriteError?.Code == 11000;
    }
}

public sealed class StorefrontFooterProjectionConsumerDefinition : ConsumerDefinition<StorefrontFooterProjectionConsumer>
{
    public StorefrontFooterProjectionConsumerDefinition()
    {
        EndpointName = "storefront-footer-projection-v1";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<StorefrontFooterProjectionConsumer> consumerConfigurator)
    {
        endpointConfigurator.UseMessageRetry(r =>
        {
            r.Exponential(
                retryLimit: 5,
                minInterval: TimeSpan.FromSeconds(1),
                maxInterval: TimeSpan.FromSeconds(16),
                intervalDelta: TimeSpan.FromSeconds(1));
            r.Handle<global::MongoDB.Driver.MongoException>();
            r.Handle<TimeoutException>();
        });
    }
}
