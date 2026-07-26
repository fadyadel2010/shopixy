using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Promotions;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Helpers;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for Promotion domain events.
/// 
/// ARCHITECTURE (CQRS COMPLIANCE):
/// - Projects Promotion data from PostgreSQL to MongoDB
/// - MongoDB is the READ-ONLY source for Storefront badges/display
/// - PostgreSQL is write-only for promotions
/// - All writes are idempotent by (TenantId, PromotionId)
/// - Redis cache is invalidated on promotion changes
/// - Elasticsearch is synced after MongoDB for promotion-aware search
/// 
/// Events handled:
/// - PromotionCreatedEvent: Upsert promotion document
/// - PromotionUpdatedEvent: Upsert promotion document (same as create)
/// - PromotionDeletedEvent: Remove promotion document
/// - PromotionUsageChangedEvent: Update CurrentUsage field only
/// - PromotionAppliedEvent: Update CurrentUsage and log application
/// </summary>
public class PromotionProjectionConsumer :
    IConsumer<PromotionCreatedEvent>,
    IConsumer<PromotionUpdatedEvent>,
    IConsumer<PromotionDeletedEvent>,
    IConsumer<PromotionExpiredEvent>,
    IConsumer<PromotionUsageChangedEvent>,
    IConsumer<PromotionAppliedEvent>
{
    private readonly IMongoCollection<PromotionDocument> _collection;
    private readonly IMongoCollection<ProductDocument> _productCollection;
    private readonly IMongoCollection<CategoryDocument> _categoryCollection;
    private readonly ApplicationDbContext _dbContext;
    private readonly IPromotionCacheService _cacheService;
    private readonly IProductCacheService _productCacheService;
    private readonly IProductSearchIndexService _searchIndexService;
    private readonly ILogger<PromotionProjectionConsumer> _logger;

    private const string CollectionName = "promotions";
    private const string ProductsCollectionName = "products";
    private const string CategoriesCollectionName = "categories";
    private const string ConsumerName = nameof(PromotionProjectionConsumer);

    public PromotionProjectionConsumer(
        IMongoDatabase database,
        ApplicationDbContext dbContext,
        IPromotionCacheService cacheService,
        IProductCacheService productCacheService,
        IProductSearchIndexService searchIndexService,
        ILogger<PromotionProjectionConsumer> logger)
    {
        _collection = database.GetCollection<PromotionDocument>(CollectionName);
        _productCollection = database.GetCollection<ProductDocument>(ProductsCollectionName);
        _categoryCollection = database.GetCollection<CategoryDocument>(CategoriesCollectionName);
        _dbContext = dbContext;
        _cacheService = cacheService;
        _productCacheService = productCacheService;
        _searchIndexService = searchIndexService;
        _logger = logger;
        EnsureIndexes();
    }

    /// <summary>
    /// Handle PromotionCreatedEvent - upsert promotion document to MongoDB
    /// </summary>
    public async Task Consume(ConsumeContext<PromotionCreatedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing PromotionCreatedEvent | EventId: {EventId} | PromotionId: {PromotionId} | Name: {Name} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.PromotionId, @event.Name, @event.TenantId, correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "PromotionCreatedEvent rejected: TenantId is missing | EventId: {EventId} | PromotionId: {PromotionId}",
                @event.EventId, @event.PromotionId);
            return;
        }

        try
        {
            await WithPromotionEventFenceAsync(
                @event,
                typeof(PromotionCreatedEvent).FullName ?? nameof(PromotionCreatedEvent),
                async () =>
                {
                    var sqlPromotion = await LoadLiveSqlPromotionAuthorityAsync(
                        @event.TenantId.Value,
                        @event.PromotionId,
                        @event.Version,
                        context.CancellationToken);
                    if (sqlPromotion == null)
                    {
                        _logger.LogWarning(
                            "PromotionCreatedEvent rejected by SQL authority; no projection side effects will be applied | EventId: {EventId} | PromotionId: {PromotionId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                            @event.EventId, @event.PromotionId, @event.TenantId, @event.Version);
                        return;
                    }

                    var existingDocument = await FindPromotionDocumentAsync(
                        @event.TenantId.Value,
                        @event.PromotionId,
                        context.CancellationToken);
                    var document = BuildPromotionDocument(sqlPromotion, @event);
                    var applied = await UpsertDocumentAsync(document, @event, context.CancellationToken);
                    if (!applied)
                    {
                        _logger.LogWarning(
                            "PromotionCreatedEvent ignored as stale; no projection side effects will be applied | EventId: {EventId} | PromotionId: {PromotionId} | TenantId: {TenantId}",
                            @event.EventId, @event.PromotionId, @event.TenantId);
                        return;
                    }

                    await RefreshAffectedPromotionVisibilityAsync(
                        @event.TenantId.Value,
                        sqlPromotion,
                        existingDocument,
                        context.CancellationToken);
                },
                context.CancellationToken);

            _logger.LogInformation(
                "PromotionCreatedEvent processed | PromotionId: {PromotionId} | Name: {Name} | TenantId: {TenantId}",
                @event.PromotionId, @event.Name, @event.TenantId);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing PromotionCreatedEvent | EventId: {EventId} | PromotionId: {PromotionId}",
                @event.EventId, @event.PromotionId);
            throw;
        }
    }

    /// <summary>
    /// Handle PromotionUpdatedEvent - upsert promotion document to MongoDB
    /// </summary>
    public async Task Consume(ConsumeContext<PromotionUpdatedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing PromotionUpdatedEvent | EventId: {EventId} | PromotionId: {PromotionId} | Name: {Name} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.PromotionId, @event.Name, @event.TenantId, correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "PromotionUpdatedEvent rejected: TenantId is missing | EventId: {EventId} | PromotionId: {PromotionId}",
                @event.EventId, @event.PromotionId);
            return;
        }

        try
        {
            await WithPromotionEventFenceAsync(
                @event,
                typeof(PromotionUpdatedEvent).FullName ?? nameof(PromotionUpdatedEvent),
                async () =>
                {
                    var sqlPromotion = await LoadLiveSqlPromotionAuthorityAsync(
                        @event.TenantId.Value,
                        @event.PromotionId,
                        @event.Version,
                        context.CancellationToken);
                    if (sqlPromotion == null)
                    {
                        _logger.LogWarning(
                            "PromotionUpdatedEvent rejected by SQL authority; no projection side effects will be applied | EventId: {EventId} | PromotionId: {PromotionId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                            @event.EventId, @event.PromotionId, @event.TenantId, @event.Version);
                        return;
                    }

                    var existingDocument = await FindPromotionDocumentAsync(
                        @event.TenantId.Value,
                        @event.PromotionId,
                        context.CancellationToken);
                    var document = BuildPromotionDocument(sqlPromotion, @event);
                    var applied = await UpsertDocumentAsync(document, @event, context.CancellationToken);
                    if (!applied)
                    {
                        _logger.LogWarning(
                            "PromotionUpdatedEvent ignored as stale; no projection side effects will be applied | EventId: {EventId} | PromotionId: {PromotionId} | TenantId: {TenantId}",
                            @event.EventId, @event.PromotionId, @event.TenantId);
                        return;
                    }

                    await RefreshAffectedPromotionVisibilityAsync(
                        @event.TenantId.Value,
                        sqlPromotion,
                        existingDocument,
                        context.CancellationToken);
                },
                context.CancellationToken);

            _logger.LogInformation(
                "PromotionUpdatedEvent processed | PromotionId: {PromotionId} | Name: {Name} | TenantId: {TenantId}",
                @event.PromotionId, @event.Name, @event.TenantId);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing PromotionUpdatedEvent | EventId: {EventId} | PromotionId: {PromotionId}",
                @event.EventId, @event.PromotionId);
            throw;
        }
    }

    /// <summary>
    /// Handle PromotionDeletedEvent - remove promotion document from MongoDB
    /// </summary>
    public async Task Consume(ConsumeContext<PromotionDeletedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing PromotionDeletedEvent | EventId: {EventId} | PromotionId: {PromotionId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.PromotionId, @event.TenantId, correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "PromotionDeletedEvent rejected: TenantId is missing | EventId: {EventId} | PromotionId: {PromotionId}",
                @event.EventId, @event.PromotionId);
            return;
        }

        try
        {
            await WithPromotionEventFenceAsync(
                @event,
                typeof(PromotionDeletedEvent).FullName ?? nameof(PromotionDeletedEvent),
                async () =>
                {
                    var existingDocument = await FindPromotionDocumentAsync(
                        @event.TenantId.Value,
                        @event.PromotionId,
                        context.CancellationToken);
                    var sqlPromotion = await LoadDeletedSqlPromotionAuthorityAsync(
                        @event.TenantId.Value,
                        @event.PromotionId,
                        @event.Version,
                        context.CancellationToken);

                    var document = BuildDeletedPromotionDocument(@event, sqlPromotion);
                    var applied = await UpsertDocumentAsync(document, @event, context.CancellationToken);
                    if (!applied)
                    {
                        _logger.LogWarning(
                            "PromotionDeletedEvent ignored as stale; no projection side effects will be applied | EventId: {EventId} | PromotionId: {PromotionId} | TenantId: {TenantId}",
                            @event.EventId, @event.PromotionId, @event.TenantId);
                        return;
                    }

                    foreach (var productId in ResolveAffectedProductIds(@event, existingDocument, sqlPromotion))
                    {
                        await RefreshProductPromotionProjectionAsync(
                            @event.TenantId.Value,
                            productId,
                            context.CancellationToken);
                    }

                    foreach (var categoryId in ResolveAffectedCategoryIds(@event, existingDocument, sqlPromotion))
                    {
                        await RefreshCategoryPromotionProjectionAsync(
                            @event.TenantId.Value,
                            categoryId,
                            context.CancellationToken);
                    }

                    await _cacheService.InvalidatePromotionCacheAsync(@event.TenantId.Value, context.CancellationToken);
                },
                context.CancellationToken);

            _logger.LogInformation(
                "PromotionDeletedEvent processed | PromotionId: {PromotionId} | TenantId: {TenantId}",
                @event.PromotionId, @event.TenantId);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing PromotionDeletedEvent | EventId: {EventId} | PromotionId: {PromotionId}",
                @event.EventId, @event.PromotionId);
            throw;
        }
    }

    /// <summary>
    /// Handle PromotionUsageChangedEvent - update CurrentUsage field only
    /// </summary>
    public async Task Consume(ConsumeContext<PromotionUsageChangedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing PromotionUsageChangedEvent | EventId: {EventId} | PromotionId: {PromotionId} | CurrentUsage: {CurrentUsage} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.PromotionId, @event.CurrentUsage, @event.TenantId, correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "PromotionUsageChangedEvent rejected: TenantId is missing | EventId: {EventId} | PromotionId: {PromotionId}",
                @event.EventId, @event.PromotionId);
            return;
        }

        try
        {
            var sqlPromotion = await LoadLiveSqlPromotionAuthorityAsync(
                @event.TenantId.Value,
                @event.PromotionId,
                @event.Version,
                context.CancellationToken);
            if (sqlPromotion == null)
            {
                _logger.LogWarning(
                    "PromotionUsageChangedEvent rejected by SQL authority | EventId: {EventId} | PromotionId: {PromotionId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                    @event.EventId, @event.PromotionId, @event.TenantId, @event.Version);
                return;
            }

            var filter = Builders<PromotionDocument>.Filter.And(
                Builders<PromotionDocument>.Filter.Eq(x => x.TenantId, @event.TenantId.Value),
                Builders<PromotionDocument>.Filter.Eq(x => x.PromotionId, @event.PromotionId),
                BuildFreshnessFilter(@event)
            );

            var update = Builders<PromotionDocument>.Update
                .Set(x => x.CurrentUsage, sqlPromotion.CurrentUsage)
                .Set(x => x.SyncedAtUtc, DateTime.UtcNow)
                .Set(x => x.LastEventId, @event.EventId)
                .Set(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastEventVersion, ProductEventVersionHelper.ToProductEventVersion(sqlPromotion.ProjectionMutationVersion));

            var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: context.CancellationToken);

            _logger.LogInformation(
                "PromotionUsageChangedEvent processed | PromotionId: {PromotionId} | CurrentUsage: {CurrentUsage} | Modified: {Modified} | TenantId: {TenantId}",
                @event.PromotionId, @event.CurrentUsage, result.ModifiedCount > 0, @event.TenantId);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing PromotionUsageChangedEvent | EventId: {EventId} | PromotionId: {PromotionId}",
                @event.EventId, @event.PromotionId);
            throw;
        }
    }

    /// <summary>
    /// Handle PromotionAppliedEvent - update CurrentUsage from application
    /// </summary>
    public async Task Consume(ConsumeContext<PromotionAppliedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing PromotionAppliedEvent | EventId: {EventId} | PromotionId: {PromotionId} | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.PromotionId, @event.OrderId, @event.TenantId, correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "PromotionAppliedEvent rejected: TenantId is missing | EventId: {EventId} | PromotionId: {PromotionId}",
                @event.EventId, @event.PromotionId);
            return;
        }

        try
        {
            var sqlPromotion = await LoadLiveSqlPromotionAuthorityAsync(
                @event.TenantId.Value,
                @event.PromotionId,
                @event.Version,
                context.CancellationToken);
            if (sqlPromotion == null)
            {
                _logger.LogWarning(
                    "PromotionAppliedEvent rejected by SQL authority | EventId: {EventId} | PromotionId: {PromotionId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                    @event.EventId, @event.PromotionId, @event.TenantId, @event.Version);
                return;
            }

            var filter = Builders<PromotionDocument>.Filter.And(
                Builders<PromotionDocument>.Filter.Eq(x => x.TenantId, @event.TenantId.Value),
                Builders<PromotionDocument>.Filter.Eq(x => x.PromotionId, @event.PromotionId),
                BuildFreshnessFilter(@event)
            );

            var update = Builders<PromotionDocument>.Update
                .Set(x => x.CurrentUsage, sqlPromotion.CurrentUsage)
                .Set(x => x.SyncedAtUtc, DateTime.UtcNow)
                .Set(x => x.LastEventId, @event.EventId)
                .Set(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastEventVersion, ProductEventVersionHelper.ToProductEventVersion(sqlPromotion.ProjectionMutationVersion));

            var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: context.CancellationToken);

            _logger.LogInformation(
                "PromotionAppliedEvent processed | PromotionId: {PromotionId} | NewUsage: {NewUsage} | OrderId: {OrderId} | Modified: {Modified} | TenantId: {TenantId}",
                @event.PromotionId, @event.NewTotalUsageCount, @event.OrderId, result.ModifiedCount > 0, @event.TenantId);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing PromotionAppliedEvent | EventId: {EventId} | PromotionId: {PromotionId} | OrderId: {OrderId}",
                @event.EventId, @event.PromotionId, @event.OrderId);
            throw;
        }
    }

    /// <summary>
    /// Handle PromotionExpiredEvent - clear promotion badges from products and categories
    /// This is triggered either by a scheduled job or when checking promotion validity
    /// </summary>
    public async Task Consume(ConsumeContext<PromotionExpiredEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing PromotionExpiredEvent | EventId: {EventId} | PromotionId: {PromotionId} | Name: {Name} | TenantId: {TenantId} | ExpiredAt: {ExpiredAt} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.PromotionId, @event.Name, @event.TenantId, @event.ExpiredAt, correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "PromotionExpiredEvent rejected: TenantId is missing | EventId: {EventId} | PromotionId: {PromotionId}",
                @event.EventId, @event.PromotionId);
            return;
        }

        try
        {
            await WithPromotionEventFenceAsync(
                @event,
                typeof(PromotionExpiredEvent).FullName ?? nameof(PromotionExpiredEvent),
                async () =>
                {
                    var existingDocument = await FindPromotionDocumentAsync(
                        @event.TenantId.Value,
                        @event.PromotionId,
                        context.CancellationToken);
                    var sqlPromotion = await LoadExpiredSqlPromotionAuthorityAsync(
                        @event.TenantId.Value,
                        @event.PromotionId,
                        @event.Version,
                        context.CancellationToken);
                    if (sqlPromotion == null)
                    {
                        _logger.LogWarning(
                            "PromotionExpiredEvent rejected by SQL authority; no projection side effects will be applied | EventId: {EventId} | PromotionId: {PromotionId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                            @event.EventId, @event.PromotionId, @event.TenantId, @event.Version);
                        return;
                    }

                    var document = BuildPromotionDocument(sqlPromotion, @event);
                    document.IsActive = false;
                    var applied = await UpsertDocumentAsync(document, @event, context.CancellationToken);
                    if (!applied)
                    {
                        _logger.LogWarning(
                            "PromotionExpiredEvent ignored as stale; no projection side effects will be applied | EventId: {EventId} | PromotionId: {PromotionId} | TenantId: {TenantId}",
                            @event.EventId, @event.PromotionId, @event.TenantId);
                        return;
                    }

                    foreach (var productId in ResolveAffectedProductIds(@event, existingDocument, sqlPromotion))
                    {
                        await RefreshProductPromotionProjectionAsync(
                            @event.TenantId.Value,
                            productId,
                            context.CancellationToken);
                    }

                    foreach (var categoryId in ResolveAffectedCategoryIds(@event, existingDocument, sqlPromotion))
                    {
                        await RefreshCategoryPromotionProjectionAsync(
                            @event.TenantId.Value,
                            categoryId,
                            context.CancellationToken);
                    }

                    await _cacheService.InvalidatePromotionCacheAsync(@event.TenantId.Value, context.CancellationToken);
                },
                context.CancellationToken);

            _logger.LogInformation(
                "PromotionExpiredEvent processed | PromotionId: {PromotionId} | Name: {Name} | TenantId: {TenantId} | Badges cleared",
                @event.PromotionId, @event.Name, @event.TenantId);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing PromotionExpiredEvent | EventId: {EventId} | PromotionId: {PromotionId}",
                @event.EventId, @event.PromotionId);
            throw;
        }
    }

    #region Private Methods

    private static PromotionDocument MapToDocument(PromotionCreatedEvent @event)
    {
        var badge = @event.DiscountType.Equals("percentage", StringComparison.OrdinalIgnoreCase)
            ? PromotionVisibilityProjectionSupport.BuildPromotionBadge(Core.Enums.DiscountType.Percentage, @event.DiscountValue)
            : PromotionVisibilityProjectionSupport.BuildPromotionBadge(Core.Enums.DiscountType.FixedAmount, @event.DiscountValue);

        return new PromotionDocument
        {
            TenantId = @event.TenantId!.Value,
            PromotionId = @event.PromotionId,
            Name = @event.Name,
            Description = @event.Description,
            DiscountType = @event.DiscountType,
            DiscountValue = @event.DiscountValue,
            MaxDiscountAmount = @event.MaxDiscountAmount,
            Scope = @event.Scope,
            AppliesToId = @event.AppliesToId,
            StartDate = @event.StartDate,
            EndDate = @event.EndDate,
            Priority = @event.Priority,
            IsActive = @event.IsActive,
            IsExclusive = @event.IsExclusive,
            MinOrderAmount = @event.MinOrderAmount,
            MaxUsage = @event.MaxUsage,
            CurrentUsage = @event.CurrentUsage,
            Badge = badge,
            CreatedAtUtc = @event.OccurredAtUtc,
            SyncedAtUtc = DateTime.UtcNow,
            LastEventId = @event.EventId,
            LastEventOccurredAtUtc = @event.OccurredAtUtc,
            LastEventVersion = @event.Version
        };
    }

    private static PromotionDocument MapToDocument(PromotionUpdatedEvent @event)
    {
        var badge = @event.DiscountType.Equals("percentage", StringComparison.OrdinalIgnoreCase)
            ? PromotionVisibilityProjectionSupport.BuildPromotionBadge(Core.Enums.DiscountType.Percentage, @event.DiscountValue)
            : PromotionVisibilityProjectionSupport.BuildPromotionBadge(Core.Enums.DiscountType.FixedAmount, @event.DiscountValue);

        return new PromotionDocument
        {
            TenantId = @event.TenantId!.Value,
            PromotionId = @event.PromotionId,
            Name = @event.Name,
            Description = @event.Description,
            DiscountType = @event.DiscountType,
            DiscountValue = @event.DiscountValue,
            MaxDiscountAmount = @event.MaxDiscountAmount,
            Scope = @event.Scope,
            AppliesToId = @event.AppliesToId,
            StartDate = @event.StartDate,
            EndDate = @event.EndDate,
            Priority = @event.Priority,
            IsActive = @event.IsActive,
            IsExclusive = @event.IsExclusive,
            MinOrderAmount = @event.MinOrderAmount,
            MaxUsage = @event.MaxUsage,
            CurrentUsage = @event.CurrentUsage,
            Badge = badge,
            SyncedAtUtc = DateTime.UtcNow,
            LastEventId = @event.EventId,
            LastEventOccurredAtUtc = @event.OccurredAtUtc,
            LastEventVersion = @event.Version
        };
    }

    private async Task<bool> UpsertDocumentAsync(PromotionDocument document, IntegrationEvent @event, CancellationToken cancellationToken)
    {
        var filter = Builders<PromotionDocument>.Filter.And(
            Builders<PromotionDocument>.Filter.Eq(x => x.TenantId, document.TenantId),
            Builders<PromotionDocument>.Filter.Eq(x => x.PromotionId, document.PromotionId),
            BuildFreshnessFilter(@event)
        );

        var options = new ReplaceOptions { IsUpsert = true };

        try
        {
            var result = await _collection.ReplaceOneAsync(filter, document, options, cancellationToken);
            if (result.MatchedCount > 0 || result.ModifiedCount > 0 || result.UpsertedId != null)
            {
                return true;
            }

            return await IsSameEventAlreadyProjectedAsync(
                document.TenantId,
                document.PromotionId,
                @event.EventId,
                @event.Version,
                cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var retry = await _collection.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions { IsUpsert = false },
                cancellationToken);

            if (retry.MatchedCount > 0 || retry.ModifiedCount > 0)
            {
                return true;
            }

            return await IsSameEventAlreadyProjectedAsync(
                document.TenantId,
                document.PromotionId,
                @event.EventId,
                @event.Version,
                cancellationToken);
        }
    }

    private async Task<bool> IsSameEventAlreadyProjectedAsync(
        Guid tenantId,
        Guid promotionId,
        Guid eventId,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        return await _collection.Find(
                Builders<PromotionDocument>.Filter.And(
                    Builders<PromotionDocument>.Filter.Eq(x => x.TenantId, tenantId),
                    Builders<PromotionDocument>.Filter.Eq(x => x.PromotionId, promotionId),
                    Builders<PromotionDocument>.Filter.Eq(x => x.LastEventId, eventId),
                    Builders<PromotionDocument>.Filter.Eq(x => x.LastEventVersion, eventVersion)))
            .AnyAsync(cancellationToken);
    }

    private async Task<PromotionDocument?> FindPromotionDocumentAsync(
        Guid tenantId,
        Guid promotionId,
        CancellationToken cancellationToken)
    {
        return await _collection.Find(
                Builders<PromotionDocument>.Filter.And(
                    Builders<PromotionDocument>.Filter.Eq(x => x.TenantId, tenantId),
                    Builders<PromotionDocument>.Filter.Eq(x => x.PromotionId, promotionId)))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task WithPromotionEventFenceAsync(
        IntegrationEvent @event,
        string eventType,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        ProjectionConsumerEventFence? fence = null;
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                @event,
                ConsumerName,
                eventType,
                ResolvePromotionEventId(@event),
                cancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "Duplicate or active promotion projection replay collapsed before side effects | EventId: {EventId} | EventType: {EventType}",
                    @event.EventId,
                    eventType);
                return;
            }

            await action();
            await fence.CompleteAsync(
                @event,
                ConsumerName,
                eventType,
                ResolvePromotionEventId(@event),
                cancellationToken);
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    private static Guid ResolvePromotionEventId(IntegrationEvent @event) =>
        @event switch
        {
            PromotionCreatedEvent created => created.PromotionId,
            PromotionUpdatedEvent updated => updated.PromotionId,
            PromotionDeletedEvent deleted => deleted.PromotionId,
            PromotionExpiredEvent expired => expired.PromotionId,
            PromotionUsageChangedEvent usage => usage.PromotionId,
            PromotionAppliedEvent applied => applied.PromotionId,
            _ => @event.EventId
        };

    private async Task<Promotion?> LoadLiveSqlPromotionAuthorityAsync(
        Guid tenantId,
        Guid promotionId,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var promotion = await LoadSqlPromotionAsync(tenantId, promotionId, cancellationToken);
        if (promotion == null || promotion.IsDeleted)
        {
            return null;
        }

        var validation = ProjectionAuthorityValidator.ValidateSqlVersion(
            ProductEventVersionHelper.ToProductEventVersion(promotion.ProjectionMutationVersion),
            eventVersion,
            requireExactVersion: true);
        return validation.IsAccepted ? promotion : null;
    }

    private async Task<Promotion?> LoadExpiredSqlPromotionAuthorityAsync(
        Guid tenantId,
        Guid promotionId,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var promotion = await LoadSqlPromotionAsync(tenantId, promotionId, cancellationToken);
        if (promotion == null || promotion.IsDeleted || promotion.IsActive)
        {
            return null;
        }

        var validation = ProjectionAuthorityValidator.ValidateSqlVersion(
            ProductEventVersionHelper.ToProductEventVersion(promotion.ProjectionMutationVersion),
            eventVersion,
            requireExactVersion: true);
        return validation.IsAccepted ? promotion : null;
    }

    private async Task<Promotion?> LoadDeletedSqlPromotionAuthorityAsync(
        Guid tenantId,
        Guid promotionId,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var promotion = await LoadSqlPromotionAsync(tenantId, promotionId, cancellationToken);
        if (promotion == null)
        {
            return null;
        }

        var validation = ProjectionAuthorityValidator.ValidateSqlVersion(
            ProductEventVersionHelper.ToProductEventVersion(promotion.ProjectionMutationVersion),
            eventVersion,
            requireExactVersion: true);
        if (!validation.IsAccepted)
        {
            return null;
        }

        return promotion.IsDeleted || !promotion.IsActive
            ? promotion
            : null;
    }

    private async Task<Promotion?> LoadSqlPromotionAsync(
        Guid tenantId,
        Guid promotionId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Promotions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                promotion => promotion.TenantId == tenantId && promotion.Id == promotionId,
                cancellationToken);
    }

    private static PromotionDocument BuildPromotionDocument(Promotion promotion, IntegrationEvent @event)
    {
        return new PromotionDocument
        {
            TenantId = promotion.TenantId,
            PromotionId = promotion.Id,
            Name = promotion.Name,
            Description = promotion.Description,
            DiscountType = PromotionVisibilityProjectionSupport.ToDiscountTypeString(promotion.DiscountType),
            DiscountValue = promotion.DiscountValue,
            MaxDiscountAmount = promotion.MaxDiscountAmount,
            Scope = promotion.Scope.ToString().ToLowerInvariant(),
            AppliesToId = promotion.AppliesToId,
            StartDate = promotion.StartDate,
            EndDate = promotion.EndDate,
            Priority = promotion.Priority,
            IsActive = promotion.IsActive,
            IsExclusive = promotion.IsExclusive,
            MinOrderAmount = promotion.MinOrderAmount,
            MaxUsage = promotion.MaxUsage,
            CurrentUsage = promotion.CurrentUsage,
            Badge = PromotionVisibilityProjectionSupport.BuildPromotionBadge(promotion.DiscountType, promotion.DiscountValue),
            CreatedAtUtc = promotion.CreatedAt,
            SyncedAtUtc = DateTime.UtcNow,
            LastEventId = @event.EventId,
            LastEventOccurredAtUtc = @event.OccurredAtUtc,
            LastEventVersion = ProductEventVersionHelper.ToProductEventVersion(promotion.ProjectionMutationVersion)
        };
    }

    private static PromotionDocument BuildDeletedPromotionDocument(
        PromotionDeletedEvent @event,
        Promotion? sqlPromotion)
    {
        if (sqlPromotion != null)
        {
            var document = BuildPromotionDocument(sqlPromotion, @event);
            document.IsActive = false;
            return document;
        }

        return new PromotionDocument
        {
            TenantId = @event.TenantId!.Value,
            PromotionId = @event.PromotionId,
            Name = string.Empty,
            IsActive = false,
            StartDate = @event.OccurredAtUtc,
            EndDate = @event.OccurredAtUtc,
            CreatedAtUtc = @event.OccurredAtUtc,
            SyncedAtUtc = DateTime.UtcNow,
            LastEventId = @event.EventId,
            LastEventOccurredAtUtc = @event.OccurredAtUtc,
            LastEventVersion = @event.Version
        };
    }

    private async Task RefreshAffectedPromotionVisibilityAsync(
        Guid tenantId,
        Promotion sqlPromotion,
        PromotionDocument? previousDocument,
        CancellationToken cancellationToken)
    {
        foreach (var productId in ResolveAffectedProductIds(sqlPromotion, previousDocument))
        {
            await RefreshProductPromotionProjectionAsync(
                tenantId,
                productId,
                cancellationToken);
        }

        foreach (var categoryId in ResolveAffectedCategoryIds(sqlPromotion, previousDocument))
        {
            await RefreshCategoryPromotionProjectionAsync(
                tenantId,
                categoryId,
                cancellationToken);
        }

        await _cacheService.InvalidatePromotionCacheAsync(tenantId, cancellationToken);
    }

    private static IReadOnlyList<Guid> ResolveAffectedProductIds(
        PromotionDeletedEvent @event,
        PromotionDocument? existingDocument,
        Promotion? sqlPromotion)
    {
        var productIds = new HashSet<Guid>();
        if (sqlPromotion is { Scope: PointOfSale.Core.Enums.PromotionScope.Product, AppliesToId: { } sqlProductId })
        {
            productIds.Add(sqlProductId);
        }

        if (existingDocument is { Scope: "product", AppliesToId: { } productId })
        {
            productIds.Add(productId);
        }

        if (string.Equals(@event.Scope, "product", StringComparison.OrdinalIgnoreCase) &&
            @event.AppliesToId.HasValue)
        {
            productIds.Add(@event.AppliesToId.Value);
        }

        return productIds.ToList();
    }

    private static IReadOnlyList<Guid> ResolveAffectedProductIds(
        PromotionExpiredEvent @event,
        PromotionDocument? existingDocument,
        Promotion? sqlPromotion)
    {
        var productIds = new HashSet<Guid>();
        if (sqlPromotion is { Scope: PointOfSale.Core.Enums.PromotionScope.Product, AppliesToId: { } sqlProductId })
        {
            productIds.Add(sqlProductId);
        }

        if (existingDocument is { Scope: "product", AppliesToId: { } productId })
        {
            productIds.Add(productId);
        }

        if (string.Equals(@event.Scope, "product", StringComparison.OrdinalIgnoreCase) &&
            @event.AppliesToId.HasValue)
        {
            productIds.Add(@event.AppliesToId.Value);
        }

        return productIds.ToList();
    }

    private static IReadOnlyList<Guid> ResolveAffectedProductIds(
        Promotion promotion,
        PromotionDocument? existingDocument)
    {
        var productIds = new HashSet<Guid>();
        if (promotion is { Scope: PointOfSale.Core.Enums.PromotionScope.Product, AppliesToId: { } productId })
        {
            productIds.Add(productId);
        }

        if (existingDocument is { Scope: "product", AppliesToId: { } previousProductId })
        {
            productIds.Add(previousProductId);
        }

        return productIds.ToList();
    }

    private static IReadOnlyList<Guid> ResolveAffectedCategoryIds(
        PromotionDeletedEvent @event,
        PromotionDocument? existingDocument,
        Promotion? sqlPromotion)
    {
        var categoryIds = new HashSet<Guid>();
        if (sqlPromotion is { Scope: PointOfSale.Core.Enums.PromotionScope.Category, AppliesToId: { } sqlCategoryId })
        {
            categoryIds.Add(sqlCategoryId);
        }

        if (existingDocument is { Scope: "category", AppliesToId: { } categoryId })
        {
            categoryIds.Add(categoryId);
        }

        if (string.Equals(@event.Scope, "category", StringComparison.OrdinalIgnoreCase) &&
            @event.AppliesToId.HasValue)
        {
            categoryIds.Add(@event.AppliesToId.Value);
        }

        return categoryIds.ToList();
    }

    private static IReadOnlyList<Guid> ResolveAffectedCategoryIds(
        PromotionExpiredEvent @event,
        PromotionDocument? existingDocument,
        Promotion? sqlPromotion)
    {
        var categoryIds = new HashSet<Guid>();
        if (sqlPromotion is { Scope: PointOfSale.Core.Enums.PromotionScope.Category, AppliesToId: { } sqlCategoryId })
        {
            categoryIds.Add(sqlCategoryId);
        }

        if (existingDocument is { Scope: "category", AppliesToId: { } categoryId })
        {
            categoryIds.Add(categoryId);
        }

        if (string.Equals(@event.Scope, "category", StringComparison.OrdinalIgnoreCase) &&
            @event.AppliesToId.HasValue)
        {
            categoryIds.Add(@event.AppliesToId.Value);
        }

        return categoryIds.ToList();
    }

    private static IReadOnlyList<Guid> ResolveAffectedCategoryIds(
        Promotion promotion,
        PromotionDocument? existingDocument)
    {
        var categoryIds = new HashSet<Guid>();
        if (promotion is { Scope: PointOfSale.Core.Enums.PromotionScope.Category, AppliesToId: { } categoryId })
        {
            categoryIds.Add(categoryId);
        }

        if (existingDocument is { Scope: "category", AppliesToId: { } previousCategoryId })
        {
            categoryIds.Add(previousCategoryId);
        }

        return categoryIds.ToList();
    }

    private async Task RefreshProductPromotionProjectionAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var promotions = await _dbContext.Promotions
            .AsNoTracking()
            .Where(promotion =>
                promotion.TenantId == tenantId &&
                !promotion.IsDeleted &&
                promotion.Scope == PointOfSale.Core.Enums.PromotionScope.Product &&
                promotion.AppliesToId == productId)
            .ToListAsync(cancellationToken);
        var winningPromotion = PromotionVisibilityProjectionSupport.SelectWinningProductPromotion(promotions, nowUtc);
        var filter = Builders<ProductDocument>.Filter.And(
            Builders<ProductDocument>.Filter.Eq(document => document.TenantId, tenantId),
            Builders<ProductDocument>.Filter.Eq(document => document.ProductId, productId));

        var productDocument = await _productCollection
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);
        if (productDocument == null)
        {
            return;
        }

        var update = winningPromotion == null
            ? PromotionVisibilityProjectionSupport.BuildClearProductPromotionUpdate()
            : PromotionVisibilityProjectionSupport.BuildApplyProductPromotionUpdate(winningPromotion);
        update = Builders<ProductDocument>.Update.Combine(
            update,
            Builders<ProductDocument>.Update.Set(document => document.SyncedAtUtc, nowUtc));

        await _productCollection.UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);
        await RefreshProductCachesAndSearchAsync(tenantId, productId, cancellationToken);
    }

    private async Task RefreshProductCachesAndSearchAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var product = await _productCollection.Find(
                Builders<ProductDocument>.Filter.And(
                    Builders<ProductDocument>.Filter.Eq(document => document.TenantId, tenantId),
                    Builders<ProductDocument>.Filter.Eq(document => document.ProductId, productId)))
            .FirstOrDefaultAsync(cancellationToken);
        if (product == null)
        {
            return;
        }

        try
        {
            await _productCacheService.InvalidateProductAsync(
                tenantId,
                product.ProductId,
                product.Slug,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to invalidate product cache after promotion projection refresh | ProductId: {ProductId}",
                productId);
        }

        await SyncProductToElasticsearchAsync(tenantId, productId, cancellationToken);
    }

    private async Task RefreshCategoryPromotionProjectionAsync(
        Guid tenantId,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var promotions = await _dbContext.Promotions
            .AsNoTracking()
            .Where(promotion =>
                promotion.TenantId == tenantId &&
                !promotion.IsDeleted &&
                promotion.Scope == PointOfSale.Core.Enums.PromotionScope.Category &&
                promotion.AppliesToId == categoryId)
            .ToListAsync(cancellationToken);
        var winningPromotion = PromotionVisibilityProjectionSupport.SelectWinningCategoryPromotion(promotions, nowUtc);
        var filter = Builders<CategoryDocument>.Filter.And(
            Builders<CategoryDocument>.Filter.Eq(document => document.TenantId, tenantId),
            Builders<CategoryDocument>.Filter.Eq(document => document.CategoryId, categoryId));

        var categoryDocument = await _categoryCollection
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);
        if (categoryDocument == null)
        {
            return;
        }

        PromotionVisibilityProjectionSupport.ApplyCategoryPromotionOverlay(categoryDocument, winningPromotion);
        await _categoryCollection.ReplaceOneAsync(
            filter,
            categoryDocument,
            new ReplaceOptions { IsUpsert = false },
            cancellationToken);
    }

    private void EnsureIndexes()
    {
        var index = new CreateIndexModel<PromotionDocument>(
            Builders<PromotionDocument>.IndexKeys
                .Ascending(document => document.TenantId)
                .Ascending(document => document.PromotionId),
            new CreateIndexOptions
            {
                Unique = true,
                Name = "idx_tenant_promotion_unique",
                Background = true
            });

        _collection.Indexes.CreateOne(index);
    }

    private static FilterDefinition<PromotionDocument> BuildFreshnessFilter(IntegrationEvent @event)
    {
        return Builders<PromotionDocument>.Filter.And(
            Builders<PromotionDocument>.Filter.Ne(x => x.LastEventId, @event.EventId),
            Builders<PromotionDocument>.Filter.Or(
                Builders<PromotionDocument>.Filter.Exists(x => x.LastEventOccurredAtUtc, false),
                Builders<PromotionDocument>.Filter.Eq(x => x.LastEventOccurredAtUtc, null),
                Builders<PromotionDocument>.Filter.Exists(x => x.LastEventVersion, false),
                Builders<PromotionDocument>.Filter.Eq(x => x.LastEventVersion, 0),
                Builders<PromotionDocument>.Filter.Lt(x => x.LastEventVersion, @event.Version),
                Builders<PromotionDocument>.Filter.And(
                    Builders<PromotionDocument>.Filter.Lt(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc),
                    Builders<PromotionDocument>.Filter.Lt(x => x.LastEventVersion, @event.Version))));
    }

    private async Task UpdateProductPromotionBadgeAsync(
        Guid tenantId,
        Guid productId,
        Guid promotionId,
        string badge,
        string discountType,
        decimal discountValue,
        DateTime startDate,
        DateTime endDate,
        double? searchBoostOverride,
        CancellationToken cancellationToken)
    {
        try
        {
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(x => x.ProductId, productId)
            );

            var update = Builders<ProductDocument>.Update
                .Set(x => x.HasActivePromotion, true)
                .Set(x => x.PromotionBadge, badge)
                .Set("ActivePromotionId", promotionId.ToString())
                .Set(x => x.PromotionDiscountType, discountType)
                .Set(x => x.PromotionDiscountValue, discountValue)
                .Set(x => x.PromotionMaxDiscountAmount, (decimal?)null)
                // Smart enhancement fields
                .Set(x => x.PromotionStartDate, startDate)
                .Set(x => x.PromotionEndDate, endDate)
                .Set(x => x.SearchBoostOverride, searchBoostOverride ?? 0);

            await _productCollection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Updated product promotion badge | ProductId: {ProductId} | Badge: {Badge} | StartDate: {StartDate} | EndDate: {EndDate} | BoostOverride: {BoostOverride}",
                productId, badge, startDate, endDate, searchBoostOverride);

            // CRITICAL: Sync to Elasticsearch AFTER MongoDB update and SQL authority recomposition.
            // This ensures ES receives current promotion data without bypassing product authority.
            await SyncProductToElasticsearchAsync(tenantId, productId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to update product promotion badge | ProductId: {ProductId}",
                productId);
        }
    }

    /// <summary>
    /// Syncs a SQL-recomposed product projection to Elasticsearch for promotion-aware search.
    /// Called after MongoDB update so promotion fields are available as projection candidates.
    /// </summary>
    private async Task SyncProductToElasticsearchAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Fetch updated projection candidate from MongoDB.
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(x => x.ProductId, productId)
            );

            var product = await _productCollection.Find(filter).FirstOrDefaultAsync(cancellationToken);
            if (product == null)
            {
                _logger.LogDebug(
                    "Product not found for ES sync | ProductId: {ProductId}",
                    productId);
                return;
            }

            product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                product,
                tenantId,
                cancellationToken);

            if (product == null)
            {
                await _searchIndexService.DeleteAsync(tenantId, productId, cancellationToken);
                return;
            }

            // Map to ES document
            var document = ProductSearchDocumentMapper.MapFromProductDocument(product);

            var success = await _searchIndexService.IndexAsync(document, cancellationToken);
            
            if (success)
            {
                _logger.LogInformation(
                    "Product synced to ES after promotion update | ProductId: {ProductId} | HasActivePromotion: {HasPromotion} | DiscountValue: {DiscountValue}",
                    productId, document.HasActivePromotion, document.PromotionDiscountValue);
            }
            else
            {
                _logger.LogWarning(
                    "Failed to sync product to ES | ProductId: {ProductId}",
                    productId);
            }
        }
        catch (Exception ex)
        {
            // Don't throw - ES sync failure should not block promotion projection
            _logger.LogWarning(ex,
                "Exception syncing product to ES | ProductId: {ProductId}",
                productId);
        }
    }

    private async Task RemoveProductPromotionBadgeAsync(
        Guid tenantId,
        Guid productId,
        Guid promotionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(x => x.ProductId, productId),
                Builders<ProductDocument>.Filter.Eq("ActivePromotionId", promotionId.ToString())
            );

            var update = Builders<ProductDocument>.Update
                .Set(x => x.HasActivePromotion, false)
                .Set(x => x.PromotionBadge, (string?)null)
                .Set("ActivePromotionId", (string?)null)
                .Set(x => x.PromotionDiscountType, (string?)null)
                .Set(x => x.PromotionDiscountValue, (decimal?)null)
                .Set(x => x.PromotionMaxDiscountAmount, (decimal?)null)
                // Also clear smart enhancement fields
                .Set(x => x.PromotionStartDate, (DateTime?)null)
                .Set(x => x.PromotionEndDate, (DateTime?)null)
                .Set(x => x.SearchBoostOverride, 0.0);

            await _productCollection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Removed product promotion badge | ProductId: {ProductId} | PromotionId: {PromotionId}",
                productId, promotionId);

            // CRITICAL: Sync to Elasticsearch AFTER MongoDB update and SQL authority recomposition.
            await SyncProductToElasticsearchAsync(tenantId, productId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to remove product promotion badge | ProductId: {ProductId}",
                productId);
        }
    }

    private async Task RemovePromotionFromAllProductsAsync(
        Guid tenantId,
        Guid promotionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq("ActivePromotionId", promotionId.ToString())
            );

            var update = Builders<ProductDocument>.Update
                .Set(x => x.HasActivePromotion, false)
                .Set(x => x.PromotionBadge, (string?)null)
                .Set("ActivePromotionId", (string?)null)
                .Set(x => x.PromotionDiscountType, (string?)null)
                .Set(x => x.PromotionDiscountValue, (decimal?)null)
                .Set(x => x.PromotionMaxDiscountAmount, (decimal?)null)
                // Also clear smart enhancement fields
                .Set(x => x.PromotionStartDate, (DateTime?)null)
                .Set(x => x.PromotionEndDate, (DateTime?)null)
                .Set(x => x.SearchBoostOverride, 0.0);

            var result = await _productCollection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Removed promotion from all products | PromotionId: {PromotionId} | ProductsUpdated: {Count}",
                promotionId, result.ModifiedCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to remove promotion from products | PromotionId: {PromotionId}",
                promotionId);
        }
    }

    #endregion
}

/// <summary>
/// Consumer definition for PromotionProjectionConsumer.
/// Configures retry policy and error handling.
/// </summary>
public class PromotionProjectionConsumerDefinition : ConsumerDefinition<PromotionProjectionConsumer>
{
    public PromotionProjectionConsumerDefinition()
    {
        EndpointName = "promotion-projection";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PromotionProjectionConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Configure retry policy for transient failures
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
    }
}
