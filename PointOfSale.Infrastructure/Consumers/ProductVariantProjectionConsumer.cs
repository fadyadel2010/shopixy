using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Utilities;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for Product Variant domain events.
/// 
/// PHASE 11.3.1 — PRODUCT VARIANT MONGODB PROJECTION
/// 
/// ARCHITECTURE (CQRS COMPLIANCE):
/// - Projects Variant data from PostgreSQL events to MongoDB
/// - Variants are EMBEDDED inside ProductDocument (Shopify model)
/// - VariantId is the ONLY identity (no attribute-based lookup)
/// - MongoDB is READ-ONLY source for Storefront
/// - PostgreSQL is write-only for variants
/// 
/// Events handled:
/// - ProductVariantCreatedEvent: Add variant to ProductDocument.Variants[]
/// - ProductVariantUpdatedEvent: Update existing variant in ProductDocument.Variants[]
/// - ProductVariantDeletedEvent: Remove variant from ProductDocument.Variants[]
/// - ProductVariantStockChangedEvent: Update variant's TotalStock and InStock
/// 
/// Cache Invalidation:
/// - Invalidates product cache after every variant change
/// - Uses pattern-based invalidation for all product keys
/// </summary>
public class ProductVariantProjectionConsumer :
    IConsumer<ProductVariantCreatedEvent>,
    IConsumer<ProductVariantUpdatedEvent>,
    IConsumer<ProductVariantDeletedEvent>,
    IConsumer<ProductVariantStockChangedEvent>
{
    private const string VariantCreatedConsumerName = nameof(ProductVariantProjectionConsumer) + ":" + nameof(ProductVariantCreatedEvent);
    private const string VariantUpdatedConsumerName = nameof(ProductVariantProjectionConsumer) + ":" + nameof(ProductVariantUpdatedEvent);
    private const string VariantStockConsumerName = nameof(ProductVariantProjectionConsumer) + ":" + nameof(ProductVariantStockChangedEvent);
    private const string VariantDeletedConsumerName = nameof(ProductVariantProjectionConsumer) + ":" + nameof(ProductVariantDeletedEvent);

    private readonly IMongoCollection<ProductDocument> _collection;
    private readonly IConnectionMultiplexer _redis;
    private readonly IProductSearchIndexService _searchIndexService;
    private readonly IProductSqlAuthorityProjectionWriter? _productProjectionWriter;
    private readonly ILogger<ProductVariantProjectionConsumer> _logger;
    private readonly string _redisInstanceName;
    private readonly ApplicationDbContext? _dbContext;

    public ProductVariantProjectionConsumer(
        IMongoDatabase database,
        IConnectionMultiplexer redis,
        IProductSearchIndexService searchIndexService,
        IOptions<RedisSettings> redisSettings,
        ILogger<ProductVariantProjectionConsumer> logger,
        ApplicationDbContext? dbContext = null,
        IProductSqlAuthorityProjectionWriter? productProjectionWriter = null)
    {
        _collection = database.GetCollection<ProductDocument>("products");
        _redis = redis;
        _searchIndexService = searchIndexService;
        _productProjectionWriter = productProjectionWriter;
        _redisInstanceName = redisSettings.Value.InstanceName ?? "pos";
        _logger = logger;
        _dbContext = dbContext;
    }

    #region ProductVariantCreatedEvent

    /// <summary>
    /// Handle ProductVariantCreatedEvent - append variant to ProductDocument.Variants[]
    /// </summary>
    public async Task Consume(ConsumeContext<ProductVariantCreatedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing ProductVariantCreatedEvent | EventId: {EventId} | VariantId: {VariantId} | ProductId: {ProductId} | SKU: {SKU} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.VariantId, @event.ProductId, @event.SKU, @event.TenantId, correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "ProductVariantCreatedEvent rejected: TenantId is missing | EventId: {EventId} | VariantId: {VariantId}",
                @event.EventId, @event.VariantId);
            return;
        }

        ProjectionConsumerEventFence? fence = null;
        try
        {
            if (_dbContext != null)
            {
                var eventType = typeof(ProductVariantCreatedEvent).FullName ?? nameof(ProductVariantCreatedEvent);
                fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                    _dbContext,
                    @event,
                    VariantCreatedConsumerName,
                    eventType,
                    @event.VariantId,
                    context.CancellationToken);

                if (fence == null)
                {
                    _logger.LogInformation(
                        "ProductVariantCreatedEvent duplicate or active replay collapsed before side effects | EventId: {EventId} | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId}",
                        @event.EventId,
                        @event.VariantId,
                        @event.ProductId,
                        @event.TenantId);
                    return;
                }

                var sqlAuthority = await LoadSqlVariantAuthorityAsync(
                    @event.TenantId.Value,
                    @event.ProductId,
                    @event.VariantId,
                    @event.EventVersion,
                    requireExactEventVersion: false,
                    context.CancellationToken);

                if (sqlAuthority == null)
                {
                    _logger.LogWarning(
                        "ProductVariantCreatedEvent rejected by SQL variant/media authority | EventId: {EventId} | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                        @event.EventId,
                        @event.VariantId,
                        @event.ProductId,
                        @event.TenantId,
                        @event.EventVersion);

                    await fence.CompleteAsync(
                        @event,
                        VariantCreatedConsumerName,
                        eventType,
                        @event.VariantId,
                        context.CancellationToken);
                    return;
                }

                ApplySqlVariantAuthority(@event, sqlAuthority);
            }

            // Map event to variant document
            var variantDoc = MapToVariantDocument(@event);

            // Find product and add variant
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(p => p.TenantId, @event.TenantId.Value),
                Builders<ProductDocument>.Filter.Eq(p => p.ProductId, @event.ProductId),
                BuildNoNewerVariantDeleteTombstoneFilter(
                    @event.VariantId,
                    @event.OccurredAtUtc,
                    @event.EventVersion),
                Builders<ProductDocument>.Filter.Not(
                    Builders<ProductDocument>.Filter.ElemMatch(p => p.Variants,
                        v => v.VariantId == @event.VariantId))
            );

            // If this is the default variant and there are existing ones, un-default them
            UpdateDefinition<ProductDocument> update;
            if (@event.IsDefault)
            {
                // First, un-default all existing variants, then add the new one
                var undefaultUpdate = Builders<ProductDocument>.Update.Set(
                    "Variants.$[].IsDefault", false);
                
                await _collection.UpdateOneAsync(filter, undefaultUpdate, cancellationToken: context.CancellationToken);
            }

            // Now push the new variant
            update = Builders<ProductDocument>.Update
                .PullFilter(p => p.DeletedVariantTombstones, tombstone => tombstone.VariantId == @event.VariantId)
                .Push(p => p.Variants, variantDoc)
                .Set(p => p.SyncedAtUtc, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: context.CancellationToken);

            if (result.ModifiedCount == 0)
            {
                var existingProduct = await _collection.Find(
                        Builders<ProductDocument>.Filter.And(
                            Builders<ProductDocument>.Filter.Eq(p => p.TenantId, @event.TenantId.Value),
                            Builders<ProductDocument>.Filter.Eq(p => p.ProductId, @event.ProductId)))
                    .FirstOrDefaultAsync(context.CancellationToken);

                if (existingProduct?.Variants.Any(variant => variant.VariantId == @event.VariantId) == true)
                {
                    _logger.LogInformation(
                        "ProductVariantCreatedEvent duplicate ignored | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId}",
                        @event.ProductId,
                        @event.VariantId,
                        @event.TenantId);
                    if (fence != null && _dbContext != null)
                    {
                        await fence.CompleteAsync(
                            @event,
                            VariantCreatedConsumerName,
                            typeof(ProductVariantCreatedEvent).FullName ?? nameof(ProductVariantCreatedEvent),
                            @event.VariantId,
                            context.CancellationToken);
                    }

                    return;
                }

                if (existingProduct?.DeletedVariantTombstones.Any(tombstone =>
                        tombstone.VariantId == @event.VariantId &&
                        IsDeleteTombstoneNewerThanOrEqualToEvent(
                            tombstone,
                            @event.OccurredAtUtc,
                            @event.EventVersion)) == true)
                {
                    _logger.LogWarning(
                        "ProductVariantCreatedEvent stale create ignored after delete tombstone | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId} | EventId: {EventId}",
                        @event.ProductId,
                        @event.VariantId,
                        @event.TenantId,
                        @event.EventId);
                    if (fence != null && _dbContext != null)
                    {
                        await fence.CompleteAsync(
                            @event,
                            VariantCreatedConsumerName,
                            typeof(ProductVariantCreatedEvent).FullName ?? nameof(ProductVariantCreatedEvent),
                            @event.VariantId,
                            context.CancellationToken);
                    }

                    return;
                }

                if (_productProjectionWriter == null)
                {
                    _logger.LogWarning(
                        "ProductVariantCreatedEvent: Product not found in MongoDB, will retry | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId}",
                        @event.ProductId, @event.VariantId, @event.TenantId);

                    throw new InvalidOperationException(
                        $"Product {@event.ProductId} not found in MongoDB. ProductCreatedEvent may not have been processed yet. Retrying...");
                }

                _logger.LogWarning(
                    "ProductVariantCreatedEvent: Product missing from MongoDB; recomposing parent projection from SQL authority | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId}",
                    @event.ProductId, @event.VariantId, @event.TenantId);
            }

            // Invalidate cache
            await InvalidateProductCacheAsync(@event.TenantId.Value, @event.ProductId, correlationId);

            await RecomposeParentProductProjectionAsync(
                @event.TenantId.Value,
                @event.ProductId,
                @event.EventId,
                @event.OccurredAtUtc,
                @event.EventVersion,
                context.CancellationToken);
            
            // CRITICAL: Sync HasVariants=true to Elasticsearch for header search
            // This ensures "Multiple options" is shown instead of price
            await SyncHasVariantsToElasticsearchAsync(@event.TenantId.Value, @event.ProductId, true, context.CancellationToken);

            _logger.LogInformation(
                "ProductVariantCreatedEvent processed | VariantId: {VariantId} | ProductId: {ProductId} | SKU: {SKU} | IsDefault: {IsDefault} | TenantId: {TenantId}",
                @event.VariantId, @event.ProductId, @event.SKU, @event.IsDefault, @event.TenantId);

            if (fence != null && _dbContext != null)
            {
                await fence.CompleteAsync(
                    @event,
                    VariantCreatedConsumerName,
                    typeof(ProductVariantCreatedEvent).FullName ?? nameof(ProductVariantCreatedEvent),
                    @event.VariantId,
                    context.CancellationToken);
            }
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing ProductVariantCreatedEvent | EventId: {EventId} | VariantId: {VariantId}",
                @event.EventId, @event.VariantId);
            throw;
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    #endregion

    #region ProductVariantUpdatedEvent

    /// <summary>
    /// Handle ProductVariantUpdatedEvent - update existing variant in ProductDocument.Variants[]
    /// </summary>
    public async Task Consume(ConsumeContext<ProductVariantUpdatedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing ProductVariantUpdatedEvent | EventId: {EventId} | VariantId: {VariantId} | ProductId: {ProductId} | ChangedFields: [{ChangedFields}] | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.VariantId, @event.ProductId, string.Join(", ", @event.ChangedFields ?? Array.Empty<string>()), @event.TenantId, correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "ProductVariantUpdatedEvent rejected: TenantId is missing | EventId: {EventId} | VariantId: {VariantId}",
                @event.EventId, @event.VariantId);
            return;
        }

        ProjectionConsumerEventFence? fence = null;
        try
        {
            if (_dbContext != null)
            {
                var eventType = typeof(ProductVariantUpdatedEvent).FullName ?? nameof(ProductVariantUpdatedEvent);
                fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                    _dbContext,
                    @event,
                    VariantUpdatedConsumerName,
                    eventType,
                    @event.VariantId,
                    context.CancellationToken);

                if (fence == null)
                {
                    _logger.LogInformation(
                        "ProductVariantUpdatedEvent duplicate or active replay collapsed before side effects | EventId: {EventId} | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId}",
                        @event.EventId,
                        @event.VariantId,
                        @event.ProductId,
                        @event.TenantId);
                    return;
                }

                var sqlAuthority = await LoadSqlVariantAuthorityAsync(
                    @event.TenantId.Value,
                    @event.ProductId,
                    @event.VariantId,
                    @event.EventVersion,
                    requireExactEventVersion: true,
                    context.CancellationToken);

                if (sqlAuthority == null)
                {
                    _logger.LogWarning(
                        "ProductVariantUpdatedEvent rejected by SQL variant/media authority | EventId: {EventId} | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                        @event.EventId,
                        @event.VariantId,
                        @event.ProductId,
                        @event.TenantId,
                        @event.EventVersion);

                    await fence.CompleteAsync(
                        @event,
                        VariantUpdatedConsumerName,
                        eventType,
                        @event.VariantId,
                        context.CancellationToken);
                    return;
                }

                ApplySqlVariantAuthority(@event, sqlAuthority);
            }

            // Build filter to find the specific variant in the array
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(p => p.TenantId, @event.TenantId.Value),
                Builders<ProductDocument>.Filter.Eq(p => p.ProductId, @event.ProductId),
                BuildNoNewerVariantDeleteTombstoneFilter(
                    @event.VariantId,
                    @event.OccurredAtUtc,
                    @event.EventVersion),
                Builders<ProductDocument>.Filter.ElemMatch(p => p.Variants, 
                    v => v.VariantId == @event.VariantId &&
                         (v.LastEventVersion < @event.EventVersion ||
                          (v.LastEventVersion == @event.EventVersion &&
                           (!v.LastEventOccurredAtUtc.HasValue ||
                            v.LastEventOccurredAtUtc < @event.OccurredAtUtc ||
                            v.LastEventOccurredAtUtc == @event.OccurredAtUtc))))
            );

            // Build update for array element using positional operator
            var updateBuilder = Builders<ProductDocument>.Update;
            var updates = new List<UpdateDefinition<ProductDocument>>();

            // Update fields using positional operator ($)
            if (@event.SKU != null)
                updates.Add(updateBuilder.Set("Variants.$.SKU", @event.SKU));
            if (@event.Barcode != null)
                updates.Add(updateBuilder.Set("Variants.$.Barcode", @event.Barcode));
            if (@event.Price.HasValue)
                updates.Add(updateBuilder.Set("Variants.$.Price", @event.Price.Value));
            if (@event.OnlinePrice.HasValue)
                updates.Add(updateBuilder.Set("Variants.$.OnlinePrice", @event.OnlinePrice.Value));
            if (@event.IsActive.HasValue)
                updates.Add(updateBuilder.Set("Variants.$.IsActive", @event.IsActive.Value));
            if ((@event.ChangedFields ?? Array.Empty<string>()).Contains("MediaId"))
                updates.Add(updateBuilder.Set("Variants.$.MediaId", @event.MediaId));
            if ((@event.ChangedFields ?? Array.Empty<string>()).Contains("ImageUrl"))
                updates.Add(updateBuilder.Set("Variants.$.ImageUrl", @event.ImageUrl));
            if ((@event.ChangedFields ?? Array.Empty<string>()).Contains("SortOrder"))
                updates.Add(updateBuilder.Set("Variants.$.SortOrder", @event.SortOrder ?? 0));
            if (@event.Attributes != null)
            {
                var attributeDocs = @event.Attributes
                    .OrderBy(a => a.SortOrder)
                    .Select(a => new VariantAttributeDocument
                    {
                        Name = a.Name,
                        Value = a.Value,
                        SortOrder = a.SortOrder
                    })
                    .ToList();
                updates.Add(updateBuilder.Set("Variants.$.Attributes", attributeDocs));
            }

            // Handle IsDefault change
            if (@event.IsDefault.HasValue && @event.IsDefault.Value)
            {
                updates.Add(updateBuilder.Set("Variants.$.IsDefault", true));
            }
            else if (@event.IsDefault.HasValue)
            {
                updates.Add(updateBuilder.Set("Variants.$.IsDefault", @event.IsDefault.Value));
            }

            // Add sync timestamp
            updates.Add(updateBuilder.Set("Variants.$.LastEventOccurredAtUtc", @event.OccurredAtUtc));
            updates.Add(updateBuilder.Set("Variants.$.LastEventVersion", @event.EventVersion));
            updates.Add(updateBuilder.Set(p => p.SyncedAtUtc, DateTime.UtcNow));

            if (updates.Count > 0)
            {
                // S40: For IsDefault promotion, add guard to prevent promotion if a newer default exists
                var updateFilter = filter;
                if (@event.IsDefault == true)
                {
                    // Add condition: no other variant should have IsDefault=true with a newer timestamp
                    // Use same tie-breaker as target-variant filter: compare (OccurredAtUtc, EventVersion)
                    updateFilter = Builders<ProductDocument>.Filter.And(
                        filter,
                        Builders<ProductDocument>.Filter.Not(
                            Builders<ProductDocument>.Filter.ElemMatch(p => p.Variants,
                                v => v.VariantId != @event.VariantId &&
                                     v.IsDefault == true &&
                                     v.LastEventOccurredAtUtc.HasValue &&
                                     (v.LastEventOccurredAtUtc > @event.OccurredAtUtc ||
                                      (v.LastEventOccurredAtUtc == @event.OccurredAtUtc &&
                                       v.LastEventVersion > @event.EventVersion)))));
                }

                var combinedUpdate = updateBuilder.Combine(updates);
                var result = await _collection.UpdateOneAsync(updateFilter, combinedUpdate, cancellationToken: context.CancellationToken);

                if (result.ModifiedCount == 0)
                {
                    if (await IsProjectionMissingForCurrentSqlVariantAsync(
                            @event.TenantId.Value,
                            @event.ProductId,
                            @event.VariantId,
                            @event.EventVersion,
                            context.CancellationToken))
                    {
                        if (_productProjectionWriter == null)
                        {
                            throw new ProjectionNotReadyException(
                                $"Product variant projection {@event.VariantId} is not ready for update event {@event.EventId}.");
                        }

                        _logger.LogWarning(
                            "ProductVariantUpdatedEvent: Variant missing from MongoDB; recomposing parent projection from SQL authority | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId} | EventId: {EventId}",
                            @event.ProductId,
                            @event.VariantId,
                            @event.TenantId,
                            @event.EventId);

                        await InvalidateProductCacheAsync(@event.TenantId.Value, @event.ProductId, correlationId);
                        await RecomposeParentProductProjectionAsync(
                            @event.TenantId.Value,
                            @event.ProductId,
                            @event.EventId,
                            @event.OccurredAtUtc,
                            @event.EventVersion,
                            context.CancellationToken);

                        if (fence != null && _dbContext != null)
                        {
                            await fence.CompleteAsync(
                                @event,
                                VariantUpdatedConsumerName,
                                typeof(ProductVariantUpdatedEvent).FullName ?? nameof(ProductVariantUpdatedEvent),
                                @event.VariantId,
                                context.CancellationToken);
                        }

                        return;
                    }

                    _logger.LogWarning(
                        "ProductVariantUpdatedEvent: Variant not found or stale update ignored | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId} | EventId: {EventId}",
                        @event.VariantId, @event.ProductId, @event.TenantId, @event.EventId);
                    if (fence != null && _dbContext != null)
                    {
                        await fence.CompleteAsync(
                            @event,
                            VariantUpdatedConsumerName,
                            typeof(ProductVariantUpdatedEvent).FullName ?? nameof(ProductVariantUpdatedEvent),
                            @event.VariantId,
                            context.CancellationToken);
                    }

                    return;
                }

                if (@event.IsDefault == true)
                {
                    // S40: Clear IsDefault on other variants atomically after successful promotion
                    // Only clear variants that are older than this event (haven't been updated by newer events)
                    // Use same tie-breaker as target-variant filter: compare (OccurredAtUtc, EventVersion)
                    var undefaultFilter = Builders<ProductDocument>.Filter.And(
                        Builders<ProductDocument>.Filter.Eq(p => p.TenantId, @event.TenantId.Value),
                        Builders<ProductDocument>.Filter.Eq(p => p.ProductId, @event.ProductId),
                        Builders<ProductDocument>.Filter.ElemMatch(p => p.Variants,
                            v => v.VariantId != @event.VariantId &&
                                 v.IsDefault == true &&
                                 (!v.LastEventOccurredAtUtc.HasValue ||
                                  v.LastEventOccurredAtUtc < @event.OccurredAtUtc ||
                                  (v.LastEventOccurredAtUtc == @event.OccurredAtUtc &&
                                   v.LastEventVersion < @event.EventVersion))));
                    var undefaultUpdate = Builders<ProductDocument>.Update.Set(
                        "Variants.$[variant].IsDefault",
                        false);
                    var arrayFilters = new[]
                    {
                        new BsonDocumentArrayFilterDefinition<ProductVariantDocument>(
                            new global::MongoDB.Bson.BsonDocument
                            {
                                { "variant.VariantId", new global::MongoDB.Bson.BsonDocument("$ne", new global::MongoDB.Bson.BsonString(@event.VariantId.ToString())) },
                                { "variant.IsDefault", true },
                                { "$or", new global::MongoDB.Bson.BsonArray
                                    {
                                        new global::MongoDB.Bson.BsonDocument("variant.LastEventOccurredAtUtc", new global::MongoDB.Bson.BsonDocument("$exists", false)),
                                        new global::MongoDB.Bson.BsonDocument("variant.LastEventOccurredAtUtc", new global::MongoDB.Bson.BsonDocument("$lt", @event.OccurredAtUtc)),
                                        new global::MongoDB.Bson.BsonDocument
                                        {
                                            { "variant.LastEventOccurredAtUtc", @event.OccurredAtUtc },
                                            { "variant.LastEventVersion", new global::MongoDB.Bson.BsonDocument("$lt", @event.EventVersion) }
                                        }
                                    }
                                }
                            })
                    };

                    await _collection.UpdateOneAsync(
                        undefaultFilter,
                        undefaultUpdate,
                        new UpdateOptions { ArrayFilters = arrayFilters },
                        context.CancellationToken);
                }
            }

            // Invalidate cache
            await InvalidateProductCacheAsync(@event.TenantId.Value, @event.ProductId, correlationId);

            await RecomposeParentProductProjectionAsync(
                @event.TenantId.Value,
                @event.ProductId,
                @event.EventId,
                @event.OccurredAtUtc,
                @event.EventVersion,
                context.CancellationToken);

            _logger.LogInformation(
                "ProductVariantUpdatedEvent processed | VariantId: {VariantId} | ProductId: {ProductId} | UpdatedFields: {FieldCount} | TenantId: {TenantId}",
                @event.VariantId, @event.ProductId, updates.Count, @event.TenantId);

            if (fence != null && _dbContext != null)
            {
                await fence.CompleteAsync(
                    @event,
                    VariantUpdatedConsumerName,
                    typeof(ProductVariantUpdatedEvent).FullName ?? nameof(ProductVariantUpdatedEvent),
                    @event.VariantId,
                    context.CancellationToken);
            }
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing ProductVariantUpdatedEvent | EventId: {EventId} | VariantId: {VariantId}",
                @event.EventId, @event.VariantId);
            throw;
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    private async Task<SqlVariantAuthoritySnapshot?> LoadSqlVariantAuthorityAsync(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        int eventVersion,
        bool requireExactEventVersion,
        CancellationToken cancellationToken)
    {
        if (_dbContext == null)
        {
            return null;
        }

        var variant = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(item => item.Attributes)
            .WherePubliclyVisibleLineage(tenantId, requireVariantActive: false)
            .Where(item =>
                item.ProductId == productId &&
                item.Id == variantId)
            .SingleOrDefaultAsync(cancellationToken);

        if (variant == null)
        {
            return null;
        }

        var sqlVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion);
        var versionValidation = ProjectionAuthorityValidator.ValidateSqlVersion(
            sqlVersion,
            eventVersion,
            requireExactEventVersion);
        if (!versionValidation.IsAccepted)
        {
            return null;
        }

        if (variant.MediaId.HasValue)
        {
            var mediaValid = await _dbContext.ProductMedia
                .AsNoTracking()
                .IgnoreQueryFilters()
                .AnyAsync(media =>
                    media.TenantId == tenantId &&
                    media.ProductId == productId &&
                    media.Id == variant.MediaId.Value &&
                    media.MediaType == "image",
                    cancellationToken);

            if (!mediaValid)
            {
                return null;
            }
        }

        return new SqlVariantAuthoritySnapshot(
            sqlVersion,
            variant.SKU,
            variant.Barcode,
            variant.Price,
            variant.OnlinePrice,
            variant.CostPrice,
            variant.IsActive,
            variant.MediaId,
            variant.ImageUrl,
            variant.SortOrder,
            variant.Attributes
                .OrderBy(attribute => attribute.SortOrder)
                .Select(attribute => new VariantAttributeData
                {
                    Name = attribute.Name,
                    Value = attribute.Value,
                    SortOrder = attribute.SortOrder
                })
                .ToList());
    }

    private static void ApplySqlVariantAuthority(
        ProductVariantUpdatedEvent @event,
        SqlVariantAuthoritySnapshot authority)
    {
        @event.SKU = authority.SKU;
        @event.Barcode = authority.Barcode;
        @event.Price = authority.Price;
        @event.OnlinePrice = authority.OnlinePrice;
        @event.CostPrice = authority.CostPrice;
        @event.IsActive = authority.IsActive;
        @event.MediaId = authority.MediaId;
        @event.ImageUrl = authority.ImageUrl;
        @event.SortOrder = authority.SortOrder;
        @event.Attributes = authority.Attributes;
        @event.EventVersion = authority.EventVersion;
        @event.ChangedFields =
        [
            "SKU",
            "Barcode",
            "Price",
            "OnlinePrice",
            "CostPrice",
            "IsActive",
            "MediaId",
            "ImageUrl",
            "SortOrder",
            "Attributes"
        ];
    }

    private static void ApplySqlVariantAuthority(
        ProductVariantCreatedEvent @event,
        SqlVariantAuthoritySnapshot authority)
    {
        @event.SKU = authority.SKU;
        @event.Barcode = authority.Barcode;
        @event.Price = authority.Price;
        @event.OnlinePrice = authority.OnlinePrice;
        @event.CostPrice = authority.CostPrice;
        @event.IsActive = authority.IsActive;
        @event.MediaId = authority.MediaId;
        @event.ImageUrl = authority.ImageUrl;
        @event.SortOrder = authority.SortOrder;
        @event.Attributes = authority.Attributes;
        @event.EventVersion = authority.EventVersion;
    }

    private async Task<bool> IsProjectionMissingForCurrentSqlVariantAsync(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        if (_dbContext == null)
        {
            return false;
        }

        var sqlVersion = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WherePubliclyVisibleLineage(tenantId, requireVariantActive: false)
            .Where(variant =>
                variant.ProductId == productId &&
                variant.Id == variantId)
            .Select(variant => (long?)variant.ProjectionMutationVersion)
            .SingleOrDefaultAsync(cancellationToken);

        if (!sqlVersion.HasValue ||
            ProductEventVersionHelper.ToProductEventVersion(sqlVersion.Value) != eventVersion)
        {
            return false;
        }

        var product = await _collection.Find(
                Builders<ProductDocument>.Filter.And(
                    Builders<ProductDocument>.Filter.Eq(item => item.TenantId, tenantId),
                    Builders<ProductDocument>.Filter.Eq(item => item.ProductId, productId)))
            .FirstOrDefaultAsync(cancellationToken);

        if (product == null)
        {
            return true;
        }

        if (product.Variants.Any(variant => variant.VariantId == variantId))
        {
            return false;
        }

        return !product.DeletedVariantTombstones.Any(tombstone =>
            tombstone.VariantId == variantId &&
            tombstone.LastEventVersion >= eventVersion);
    }

    #endregion

    #region ProductVariantStockChangedEvent

    /// <summary>
    /// Handle ProductVariantStockChangedEvent - update variant's TotalStock and InStock
    /// </summary>
    public async Task Consume(ConsumeContext<ProductVariantStockChangedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing ProductVariantStockChangedEvent | EventId: {EventId} | VariantId: {VariantId} | ProductId: {ProductId} | TotalStock: {TotalStock} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.VariantId, @event.ProductId, @event.TotalStockQuantity, @event.TenantId, correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "ProductVariantStockChangedEvent rejected: TenantId is missing | EventId: {EventId} | VariantId: {VariantId}",
                @event.EventId, @event.VariantId);
            return;
        }

        ProjectionConsumerEventFence? fence = null;
        var eventType = typeof(ProductVariantStockChangedEvent).FullName ?? nameof(ProductVariantStockChangedEvent);
        try
        {
            if (_dbContext != null)
            {
                fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                    _dbContext,
                    @event,
                    VariantStockConsumerName,
                    eventType,
                    @event.VariantId,
                    context.CancellationToken);

                if (fence == null)
                {
                    _logger.LogInformation(
                        "ProductVariantStockChangedEvent duplicate or active replay collapsed before side effects | EventId: {EventId} | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId}",
                        @event.EventId,
                        @event.VariantId,
                        @event.ProductId,
                        @event.TenantId);
                    return;
                }

                var sqlAuthority = await LoadSqlVariantStockAuthorityAsync(
                    @event.TenantId.Value,
                    @event.ProductId,
                    @event.VariantId,
                    @event.EventVersion,
                    @event.StockUpdatedAtUtc,
                    context.CancellationToken);

                if (sqlAuthority == null)
                {
                    _logger.LogWarning(
                        "ProductVariantStockChangedEvent rejected by SQL stock authority | EventId: {EventId} | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                        @event.EventId,
                        @event.VariantId,
                        @event.ProductId,
                        @event.TenantId,
                        @event.EventVersion);

                    await fence.CompleteAsync(
                        @event,
                        VariantStockConsumerName,
                        eventType,
                        @event.VariantId,
                        context.CancellationToken);
                    return;
                }

                @event.TotalStockQuantity = sqlAuthority.TotalStockQuantity;
                @event.StockUpdatedAtUtc = sqlAuthority.StockUpdatedAtUtc;
            }

            var stockUpdatedAtUtc = StockProjectionTimestamp.NormalizeUtc(@event.StockUpdatedAtUtc);

            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(p => p.TenantId, @event.TenantId.Value),
                Builders<ProductDocument>.Filter.Eq(p => p.ProductId, @event.ProductId),
                Builders<ProductDocument>.Filter.ElemMatch(p => p.Variants,
                    v => v.VariantId == @event.VariantId)
            );

            var product = await _collection.Find(filter).FirstOrDefaultAsync(context.CancellationToken);
            if (product == null)
            {
                if (await IsCurrentSqlVariantAvailableForProjectionAsync(
                        @event.TenantId.Value,
                        @event.ProductId,
                        @event.VariantId,
                        context.CancellationToken))
                {
                    if (_productProjectionWriter == null)
                    {
                        throw new ProjectionNotReadyException(
                            $"Product projection {@event.ProductId} is not ready for stock event {@event.EventId}.");
                    }

                    _logger.LogWarning(
                        "ProductVariantStockChangedEvent: Product missing from MongoDB; recomposing parent projection from SQL authority | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId} | EventId: {EventId}",
                        @event.ProductId,
                        @event.VariantId,
                        @event.TenantId,
                        @event.EventId);

                    await InvalidateProductCacheAsync(@event.TenantId.Value, @event.ProductId, correlationId);
                    await RecomposeParentProductProjectionAsync(
                        @event.TenantId.Value,
                        @event.ProductId,
                        @event.EventId,
                        stockUpdatedAtUtc,
                        @event.EventVersion,
                        context.CancellationToken);

                    if (fence != null)
                    {
                        await fence.CompleteAsync(
                            @event,
                            VariantStockConsumerName,
                            eventType,
                            @event.VariantId,
                            context.CancellationToken);
                    }

                    return;
                }

                _logger.LogWarning(
                    "ProductVariantStockChangedEvent: Variant not found in MongoDB | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.VariantId, @event.ProductId, @event.TenantId);
                return;
            }

            if (product.Variants.All(variant => variant.VariantId != @event.VariantId) &&
                await IsCurrentSqlVariantAvailableForProjectionAsync(
                    @event.TenantId.Value,
                    @event.ProductId,
                    @event.VariantId,
                    context.CancellationToken))
            {
                throw new ProjectionNotReadyException(
                    $"Variant projection {@event.VariantId} is not ready for stock event {@event.EventId}.");
            }

            // Build atomic update filter with duplicate and staleness guards
            var updateFilter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(p => p.TenantId, @event.TenantId.Value),
                Builders<ProductDocument>.Filter.Eq(p => p.ProductId, @event.ProductId),
                Builders<ProductDocument>.Filter.ElemMatch(p => p.Variants,
                    v => v.VariantId == @event.VariantId),
                BuildNoNewerVariantDeleteTombstoneFilter(
                    @event.VariantId,
                    @event.OccurredAtUtc,
                    @event.EventVersion),
                // Duplicate guard: LastEventId must not match this event
                Builders<ProductDocument>.Filter.Ne(p => p.LastEventId, @event.EventId),
                BuildFreshVariantStockEventFilter(@event.VariantId, stockUpdatedAtUtc, @event.EventVersion)
            );

            var totalStock = Math.Max(0, @event.TotalStockQuantity);
            var inStock = totalStock > 0;

            var update = Builders<ProductDocument>.Update
                .Set("Variants.$.TotalStock", totalStock)
                .Set("Variants.$.InStock", inStock)
                .Set("Variants.$.StockUpdatedAtUtc", stockUpdatedAtUtc)
                .Set("Variants.$.LastStockEventId", @event.EventId)
                .Set("Variants.$.LastStockUpdatedAtUtc", stockUpdatedAtUtc)
                .Set("Variants.$.LastStockEventVersion", @event.EventVersion)
                .Set("Variants.$.SyncedAtUtc", DateTime.UtcNow)
                .Set("Variants.$.LastEventOccurredAtUtc", @event.OccurredAtUtc)
                .Set("Variants.$.LastEventVersion", @event.EventVersion)
                .Set(p => p.SyncedAtUtc, DateTime.UtcNow)
                .Set(p => p.LastEventId, @event.EventId);

            var result = await _collection.UpdateOneAsync(updateFilter, update, cancellationToken: context.CancellationToken);

            // Check if update was applied (ModifiedCount > 0 means the filter matched and update succeeded)
            if (result.ModifiedCount == 0)
            {
                // Either duplicate or stale - determine which for logging
                var existingVariant = product.Variants.FirstOrDefault(v => v.VariantId == @event.VariantId);
                
                if (product.LastEventId == @event.EventId)
                {
                    _logger.LogDebug(
                        "ProductVariantStockChangedEvent duplicate ignored (atomic guard) | EventId: {EventId} | VariantId: {VariantId}",
                        @event.EventId,
                        @event.VariantId);
                }
                else if (IsStaleStockProjection(
                             existingVariant?.LastStockUpdatedAtUtc ?? existingVariant?.StockUpdatedAtUtc,
                             existingVariant?.LastStockEventVersion ?? 0,
                             stockUpdatedAtUtc,
                             @event.EventVersion))
                {
                    _logger.LogWarning(
                        "Rejected stale stock projection update (atomic guard) | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId} | ExistingStockUpdatedAtUtc: {ExistingStockUpdatedAtUtc} | ExistingStockEventVersion: {ExistingStockEventVersion} | IncomingStockUpdatedAtUtc: {IncomingStockUpdatedAtUtc} | IncomingStockEventVersion: {IncomingStockEventVersion} | EventId: {EventId}",
                        @event.VariantId,
                        @event.ProductId,
                        @event.TenantId,
                        existingVariant?.LastStockUpdatedAtUtc ?? existingVariant?.StockUpdatedAtUtc,
                        existingVariant?.LastStockEventVersion ?? 0,
                        stockUpdatedAtUtc,
                        @event.EventVersion,
                        @event.EventId);
                }
                else
                {
                    _logger.LogWarning(
                        "Stock update rejected by atomic guard (race condition prevented) | VariantId: {VariantId} | ProductId: {ProductId} | EventId: {EventId}",
                        @event.VariantId,
                        @event.ProductId,
                        @event.EventId);
                }

                if (fence != null)
                {
                    await fence.CompleteAsync(
                        @event,
                        VariantStockConsumerName,
                        eventType,
                        @event.VariantId,
                        context.CancellationToken);
                }

                return;
            }

            // Invalidate cache
            await InvalidateProductCacheAsync(@event.TenantId.Value, @event.ProductId, correlationId);

            await RecomposeParentProductProjectionAsync(
                @event.TenantId.Value,
                @event.ProductId,
                @event.EventId,
                stockUpdatedAtUtc,
                @event.EventVersion,
                context.CancellationToken);

            // PHASE 11.8: Structured logging for stock sync (matching CheckoutService log format)
            _logger.LogInformation(
                "Variant stock synced | VariantId={VariantId} Available={AvailableStock} InStock={InStock} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                @event.VariantId, totalStock, inStock, @event.TenantId, correlationId);

            if (fence != null)
            {
                await fence.CompleteAsync(
                    @event,
                    VariantStockConsumerName,
                    eventType,
                    @event.VariantId,
                    context.CancellationToken);
            }
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing ProductVariantStockChangedEvent | EventId: {EventId} | VariantId: {VariantId}",
                @event.EventId, @event.VariantId);
            throw;
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    private async Task<bool> IsCurrentSqlVariantAvailableForProjectionAsync(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        CancellationToken cancellationToken)
    {
        return _dbContext != null &&
               await _dbContext.ProductVariants
                   .AsNoTracking()
                   .IgnoreQueryFilters()
                   .WherePubliclyVisibleLineage(tenantId)
                   .AnyAsync(variant =>
                       variant.ProductId == productId &&
                       variant.Id == variantId,
                       cancellationToken);
    }

    private async Task<SqlVariantStockAuthoritySnapshot?> LoadSqlVariantStockAuthorityAsync(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        int eventVersion,
        DateTime incomingStockUpdatedAtUtc,
        CancellationToken cancellationToken)
    {
        if (_dbContext == null)
        {
            return null;
        }

        var variant = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WherePubliclyVisibleLineage(tenantId)
            .Where(item =>
                item.ProductId == productId &&
                item.Id == variantId)
            .Select(item => new
            {
                item.ProjectionMutationVersion,
                item.StockUpdatedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (variant == null)
        {
            return null;
        }

        var sqlVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion);
        var sqlStockUpdatedAtUtc = StockProjectionTimestamp.NormalizeUtc(variant.StockUpdatedAtUtc);
        var incomingUtc = StockProjectionTimestamp.NormalizeUtc(incomingStockUpdatedAtUtc);

        var stockValidation = ProjectionAuthorityValidator.ValidateSqlVersionAndTimestamp(
            sqlVersion,
            eventVersion,
            sqlStockUpdatedAtUtc,
            incomingUtc);
        if (!stockValidation.IsAccepted)
        {
            return null;
        }

        var totalStockQuantity = await _dbContext.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WhereCurrentInventoryStockOwner(tenantId)
            .Where(stock => stock.VariantId == variantId)
            .Select(stock => stock.Quantity - stock.ReservedQuantity)
            .DefaultIfEmpty()
            .SumAsync(cancellationToken);

        return new SqlVariantStockAuthoritySnapshot(
            Math.Max(0, totalStockQuantity),
            sqlStockUpdatedAtUtc.GetValueOrDefault());
    }

    private async Task<bool> IsSqlVariantDeleteAuthorityAsync(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        if (_dbContext == null)
        {
            return false;
        }

        var variant = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId &&
                item.ProductId == productId &&
                item.Id == variantId)
            .Select(item => new
            {
                item.IsDeleted,
                item.ProjectionMutationVersion
            })
            .SingleOrDefaultAsync(cancellationToken);

        return variant is { IsDeleted: true } &&
               ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion) == eventVersion;
    }

    #endregion

    #region ProductVariantDeletedEvent

    /// <summary>
    /// Handle ProductVariantDeletedEvent - remove variant from ProductDocument.Variants[]
    /// If deleted variant was default, set first remaining variant as default
    /// </summary>
    public async Task Consume(ConsumeContext<ProductVariantDeletedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing ProductVariantDeletedEvent | EventId: {EventId} | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.VariantId, @event.ProductId, @event.TenantId, correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "ProductVariantDeletedEvent rejected: TenantId is missing | EventId: {EventId} | VariantId: {VariantId}",
                @event.EventId, @event.VariantId);
            return;
        }

        ProjectionConsumerEventFence? fence = null;
        var eventType = typeof(ProductVariantDeletedEvent).FullName ?? nameof(ProductVariantDeletedEvent);
        try
        {
            if (_dbContext != null)
            {
                fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                    _dbContext,
                    @event,
                    VariantDeletedConsumerName,
                    eventType,
                    @event.VariantId,
                    context.CancellationToken);

                if (fence == null)
                {
                    _logger.LogInformation(
                        "ProductVariantDeletedEvent duplicate or active replay collapsed before side effects | EventId: {EventId} | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId}",
                        @event.EventId,
                        @event.VariantId,
                        @event.ProductId,
                        @event.TenantId);
                    return;
                }

                if (!await IsSqlVariantDeleteAuthorityAsync(
                        @event.TenantId.Value,
                        @event.ProductId,
                        @event.VariantId,
                        @event.EventVersion,
                        context.CancellationToken))
                {
                    _logger.LogWarning(
                        "ProductVariantDeletedEvent rejected by SQL tombstone authority | EventId: {EventId} | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                        @event.EventId,
                        @event.VariantId,
                        @event.ProductId,
                        @event.TenantId,
                        @event.EventVersion);

                    await fence.CompleteAsync(
                        @event,
                        VariantDeletedConsumerName,
                        eventType,
                        @event.VariantId,
                        context.CancellationToken);
                    return;
                }
            }

            // First, check if the variant being deleted is the default
            var findFilter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(p => p.TenantId, @event.TenantId.Value),
                Builders<ProductDocument>.Filter.Eq(p => p.ProductId, @event.ProductId)
            );

            var product = await _collection.Find(findFilter).FirstOrDefaultAsync(context.CancellationToken);
            if (product == null)
            {
                _logger.LogWarning(
                    "ProductVariantDeletedEvent: Product not found in MongoDB | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.ProductId, @event.TenantId);

                await RecomposeParentProductProjectionAsync(
                    @event.TenantId.Value,
                    @event.ProductId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    @event.EventVersion,
                    context.CancellationToken);

                if (fence != null)
                {
                    await fence.CompleteAsync(
                        @event,
                        VariantDeletedConsumerName,
                        eventType,
                        @event.VariantId,
                        context.CancellationToken);
                }

                return;
            }

            var variantToDelete = product.Variants?.FirstOrDefault(v => v.VariantId == @event.VariantId);
            var wasDefault = variantToDelete?.IsDefault ?? false;

            if (variantToDelete != null && IsVariantMetadataNewerThanEvent(
                    variantToDelete,
                    @event.OccurredAtUtc,
                    @event.EventVersion))
            {
                _logger.LogWarning(
                    "ProductVariantDeletedEvent stale delete ignored | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId} | EventId: {EventId}",
                    @event.VariantId,
                    @event.ProductId,
                    @event.TenantId,
                    @event.EventId);
                if (fence != null)
                {
                    await fence.CompleteAsync(
                        @event,
                        VariantDeletedConsumerName,
                        eventType,
                        @event.VariantId,
                        context.CancellationToken);
                }

                return;
            }

            var tombstone = new ProductVariantDeletionTombstoneDocument
            {
                VariantId = @event.VariantId,
                LastEventId = @event.EventId,
                LastEventOccurredAtUtc = @event.OccurredAtUtc,
                LastEventVersion = @event.EventVersion
            };

            var deleteUpdate = BuildVariantDeleteProjectionUpdate(tombstone, DateTime.UtcNow);

            var deleteFilter = Builders<ProductDocument>.Filter.And(
                findFilter,
                BuildNoNewerVariantDeleteTombstoneFilter(
                    @event.VariantId,
                    @event.OccurredAtUtc,
                    @event.EventVersion));

            if (variantToDelete != null)
            {
                deleteFilter = Builders<ProductDocument>.Filter.And(
                    deleteFilter,
                    Builders<ProductDocument>.Filter.ElemMatch(p => p.Variants,
                        v => v.VariantId == @event.VariantId &&
                             (v.LastEventVersion < @event.EventVersion ||
                              (v.LastEventOccurredAtUtc == @event.OccurredAtUtc &&
                               v.LastEventVersion <= @event.EventVersion) ||
                              (v.LastEventVersion == @event.EventVersion &&
                               (!v.LastEventOccurredAtUtc.HasValue ||
                                v.LastEventOccurredAtUtc < @event.OccurredAtUtc)))));
            }

            var result = await _collection.UpdateOneAsync(deleteFilter, deleteUpdate, cancellationToken: context.CancellationToken);

            if (result.ModifiedCount == 0)
            {
                _logger.LogWarning(
                    "ProductVariantDeletedEvent: stale or duplicate delete ignored | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.VariantId, @event.ProductId, @event.TenantId);
                if (fence != null)
                {
                    await fence.CompleteAsync(
                        @event,
                        VariantDeletedConsumerName,
                        eventType,
                        @event.VariantId,
                        context.CancellationToken);
                }

                return;
            }

            // Trim tombstones to prevent unbounded growth
            await TrimDeletedVariantTombstonesAsync(@event.TenantId.Value, @event.ProductId, context.CancellationToken);

            // If deleted variant was default, set first remaining variant as default
            if (wasDefault)
            {
                var setDefaultFilter = Builders<ProductDocument>.Filter.And(
                    Builders<ProductDocument>.Filter.Eq(p => p.TenantId, @event.TenantId.Value),
                    Builders<ProductDocument>.Filter.Eq(p => p.ProductId, @event.ProductId),
                    Builders<ProductDocument>.Filter.SizeGt(p => p.Variants, 0)
                );

                // Set the first variant (index 0) as default
                var setDefaultUpdate = Builders<ProductDocument>.Update
                    .Set("Variants.0.IsDefault", true);

                await _collection.UpdateOneAsync(setDefaultFilter, setDefaultUpdate, cancellationToken: context.CancellationToken);

                _logger.LogInformation(
                    "ProductVariantDeletedEvent: Set first remaining variant as default | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.ProductId, @event.TenantId);
            }

            // Invalidate cache
            await InvalidateProductCacheAsync(@event.TenantId.Value, @event.ProductId, correlationId);

            await RecomposeParentProductProjectionAsync(
                @event.TenantId.Value,
                @event.ProductId,
                @event.EventId,
                @event.OccurredAtUtc,
                @event.EventVersion,
                context.CancellationToken);
            
            // CRITICAL: Sync HasVariants to Elasticsearch (may change to false if only 1 variant left)
            await SyncHasVariantsToElasticsearchAsync(@event.TenantId.Value, @event.ProductId, false, context.CancellationToken);

            _logger.LogInformation(
                "ProductVariantDeletedEvent processed | VariantId: {VariantId} | ProductId: {ProductId} | WasDefault: {WasDefault} | TenantId: {TenantId}",
                @event.VariantId, @event.ProductId, wasDefault, @event.TenantId);

            if (fence != null)
            {
                await fence.CompleteAsync(
                    @event,
                    VariantDeletedConsumerName,
                    eventType,
                    @event.VariantId,
                    context.CancellationToken);
            }
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing ProductVariantDeletedEvent | EventId: {EventId} | VariantId: {VariantId}",
                @event.EventId, @event.VariantId);
            throw;
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Maps ProductVariantCreatedEvent to ProductVariantDocument
    /// </summary>
    private static ProductVariantDocument MapToVariantDocument(ProductVariantCreatedEvent @event)
    {
        return new ProductVariantDocument
        {
            VariantId = @event.VariantId,
            SKU = @event.SKU ?? string.Empty,
            Barcode = @event.Barcode,
            Price = @event.Price,
            OnlinePrice = @event.OnlinePrice,
            // CostPrice is NOT stored in MongoDB (internal data not for storefront)
            MediaId = @event.MediaId,
            ImageUrl = @event.ImageUrl,
            IsDefault = @event.IsDefault,
            IsActive = @event.IsActive,
            TotalStock = 0, // Stock comes from separate StockChangedEvent
            SortOrder = @event.SortOrder ?? 0,
            SyncedAtUtc = DateTime.UtcNow,
            StockUpdatedAtUtc = null,
            LastStockEventId = null,
            LastStockUpdatedAtUtc = null,
            LastStockEventVersion = 0,
            LastEventOccurredAtUtc = @event.OccurredAtUtc,
            LastEventVersion = @event.EventVersion,
            Attributes = @event.Attributes
                .OrderBy(a => a.SortOrder)
                .Select(a => new VariantAttributeDocument
                {
                    Name = a.Name,
                    Value = a.Value,
                    SortOrder = a.SortOrder
                })
                .ToList()
        };
    }

    private static FilterDefinition<ProductDocument> BuildFreshVariantStockEventFilter(
        Guid variantId,
        DateTime stockUpdatedAtUtc,
        int eventVersion)
    {
        var filter = Builders<ProductDocument>.Filter;

        return filter.ElemMatch(
            product => product.Variants,
            variant =>
                variant.VariantId == variantId &&
                ((variant.LastStockUpdatedAtUtc.HasValue &&
                  (variant.LastStockUpdatedAtUtc < stockUpdatedAtUtc ||
                   (variant.LastStockUpdatedAtUtc == stockUpdatedAtUtc &&
                    variant.LastStockEventVersion <= eventVersion))) ||
                 (!variant.LastStockUpdatedAtUtc.HasValue &&
                  (!variant.StockUpdatedAtUtc.HasValue ||
                   variant.StockUpdatedAtUtc <= stockUpdatedAtUtc))));
    }

    private static bool IsStaleStockProjection(
        DateTime? existingStockUpdatedAtUtc,
        int existingStockEventVersion,
        DateTime incomingStockUpdatedAtUtc,
        int incomingStockEventVersion)
    {
        return ProjectionAuthorityValidator.IsIncomingStockStale(
            existingStockUpdatedAtUtc,
            existingStockEventVersion,
            incomingStockUpdatedAtUtc,
            incomingStockEventVersion);
    }

    /// <summary>
    /// Trims DeletedVariantTombstones to prevent unbounded growth.
    /// Keeps only the newest MAX_TOMBSTONES entries per product.
    /// Uses atomic aggregation pipeline to avoid race conditions.
    /// </summary>
    private async Task TrimDeletedVariantTombstonesAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Use atomic aggregation pipeline to trim tombstones server-side
            // This prevents race conditions from concurrent deletes
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(p => p.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(p => p.ProductId, productId),
                Builders<ProductDocument>.Filter.SizeGt(p => p.DeletedVariantTombstones, ProductDocument.MAX_TOMBSTONES));

            // Aggregation pipeline to sort and slice tombstones atomically
            var pipeline = new[]
            {
                new BsonDocument("$set", new BsonDocument("DeletedVariantTombstones",
                    new BsonDocument("$slice", new BsonArray
                    {
                        // Sort by LastEventOccurredAtUtc desc, then LastEventVersion desc
                        new BsonDocument("$sortArray", new BsonDocument
                        {
                            { "input", "$DeletedVariantTombstones" },
                            { "sortBy", new BsonDocument
                                {
                                    { "LastEventOccurredAtUtc", -1 },
                                    { "LastEventVersion", -1 }
                                }
                            }
                        }),
                        ProductDocument.MAX_TOMBSTONES
                    })
                ))
            };

            // PRODUCTION HARDENING: Wrap pipeline in UpdateDefinition for proper driver handling
            var updateDefinition = Builders<ProductDocument>.Update.Pipeline(pipeline);

            var result = await _collection.UpdateOneAsync(
                filter,
                updateDefinition,
                cancellationToken: cancellationToken);

            if (result.ModifiedCount > 0)
            {
                _logger.LogInformation(
                    "Trimmed DeletedVariantTombstones atomically | ProductId: {ProductId} | TenantId: {TenantId} | MaxTombstones: {MaxTombstones}",
                    productId, tenantId, ProductDocument.MAX_TOMBSTONES);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal - log and continue
            _logger.LogWarning(ex,
                "Failed to trim DeletedVariantTombstones | ProductId: {ProductId} | TenantId: {TenantId}",
                productId, tenantId);
        }
    }

    private static UpdateDefinition<ProductDocument> BuildVariantDeleteProjectionUpdate(
        ProductVariantDeletionTombstoneDocument tombstone,
        DateTime syncedAtUtc)
    {
        var variantId = tombstone.VariantId.ToString();
        var tombstoneDocument = new BsonDocument
        {
            { "VariantId", variantId },
            { "LastEventId", tombstone.LastEventId?.ToString() ?? string.Empty },
            { "LastEventOccurredAtUtc", tombstone.LastEventOccurredAtUtc },
            { "LastEventVersion", tombstone.LastEventVersion }
        };

        var pipeline = new[]
        {
            new BsonDocument("$set", new BsonDocument
            {
                {
                    "Variants",
                    new BsonDocument("$filter", new BsonDocument
                    {
                        { "input", new BsonDocument("$ifNull", new BsonArray { "$Variants", new BsonArray() }) },
                        { "as", "variant" },
                        { "cond", new BsonDocument("$ne", new BsonArray { "$$variant.VariantId", variantId }) }
                    })
                },
                {
                    "DeletedVariantTombstones",
                    new BsonDocument("$concatArrays", new BsonArray
                    {
                        new BsonDocument("$filter", new BsonDocument
                        {
                            { "input", new BsonDocument("$ifNull", new BsonArray { "$DeletedVariantTombstones", new BsonArray() }) },
                            { "as", "tombstone" },
                            { "cond", new BsonDocument("$ne", new BsonArray { "$$tombstone.VariantId", variantId }) }
                        }),
                        new BsonArray { tombstoneDocument }
                    })
                },
                { "SyncedAtUtc", syncedAtUtc }
            }),
            new BsonDocument("$set", new BsonDocument
            {
                {
                    "DeletedVariantTombstones",
                    new BsonDocument("$slice", new BsonArray
                    {
                        new BsonDocument("$sortArray", new BsonDocument
                        {
                            { "input", "$DeletedVariantTombstones" },
                            { "sortBy", new BsonDocument
                                {
                                    { "LastEventOccurredAtUtc", -1 },
                                    { "LastEventVersion", -1 }
                                }
                            }
                        }),
                        ProductDocument.MAX_TOMBSTONES
                    })
                }
            })
        };

        return Builders<ProductDocument>.Update.Pipeline(pipeline);
    }

    private static FilterDefinition<ProductDocument> BuildNoNewerVariantDeleteTombstoneFilter(
        Guid variantId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        return Builders<ProductDocument>.Filter.Not(
            Builders<ProductDocument>.Filter.ElemMatch(p => p.DeletedVariantTombstones,
                tombstone =>
                    tombstone.VariantId == variantId &&
                    (tombstone.LastEventOccurredAtUtc > occurredAtUtc ||
                     (tombstone.LastEventOccurredAtUtc == occurredAtUtc &&
                      tombstone.LastEventVersion >= eventVersion))));
    }

    private static bool IsVariantMetadataNewerThanEvent(
        ProductVariantDocument variant,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        if (variant.LastEventVersion > eventVersion)
        {
            return true;
        }

        return variant.LastEventVersion == eventVersion &&
               variant.LastEventOccurredAtUtc.HasValue &&
               variant.LastEventOccurredAtUtc.Value > occurredAtUtc;
    }

    private static bool IsDeleteTombstoneNewerThanOrEqualToEvent(
        ProductVariantDeletionTombstoneDocument tombstone,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        return ProjectionTombstoneGuard.IsTombstoneNewerThanOrEqualToEvent(
            tombstone.LastEventOccurredAtUtc,
            tombstone.LastEventVersion,
            occurredAtUtc,
            eventVersion);
    }

    /// <summary>
    /// Invalidates all cached product data for a specific product.
    /// </summary>
    private async Task InvalidateProductCacheAsync(Guid tenantId, Guid productId, string correlationId)
    {
        try
        {
            var db = _redis.GetDatabase();

            // First, get the product slug from MongoDB for slug-based cache invalidation
            var product = await _collection.Find(
                Builders<ProductDocument>.Filter.And(
                    Builders<ProductDocument>.Filter.Eq(p => p.TenantId, tenantId),
                    Builders<ProductDocument>.Filter.Eq(p => p.ProductId, productId)
                ))
                .Project(p => new { p.Slug })
                .FirstOrDefaultAsync();

            var keysToDelete = new List<RedisKey>();

            // Invalidate product by ID
            var productByIdKey = CacheKeyBuilder.ProductByIdKey(_redisInstanceName, tenantId, productId);
            keysToDelete.Add(productByIdKey);

            // Invalidate product by slug (CRITICAL for storefront)
            if (!string.IsNullOrEmpty(product?.Slug))
            {
                var productBySlugKey = CacheKeyBuilder.ProductBySlugKey(_redisInstanceName, tenantId, product.Slug);
                keysToDelete.Add(productBySlugKey);
            }

            // Also invalidate product list cache (since variant data appears there too potentially)
            var productsAllKey = CacheKeyBuilder.ProductsAllKey(_redisInstanceName, tenantId);
            keysToDelete.Add(productsAllKey);

            // Invalidate homepage sections that might include this product
            var featuredKey = CacheKeyBuilder.HomeFeaturedProductsKey(_redisInstanceName, tenantId);
            var newArrivalsKey = CacheKeyBuilder.HomeNewArrivalsKey(_redisInstanceName, tenantId);
            keysToDelete.Add(featuredKey);
            keysToDelete.Add(newArrivalsKey);

            // Delete all keys at once
            await db.KeyDeleteAsync(keysToDelete.ToArray());

            _logger.LogDebug(
                "Product cache invalidated | ProductId: {ProductId} | Slug: {Slug} | KeysDeleted: {KeyCount} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                productId, product?.Slug ?? "unknown", keysToDelete.Count, tenantId, correlationId);
        }
        catch (RedisException ex)
        {
            // Cache invalidation failure is non-fatal - log and continue
            _logger.LogWarning(ex,
                "Failed to invalidate product cache | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                productId, tenantId, correlationId);
        }
    }

    /// <summary>
    /// Syncs HasVariants flag to Elasticsearch for header search.
    /// This ensures variant products show "Multiple options" instead of price.
    /// </summary>
    private async Task SyncHasVariantsToElasticsearchAsync(
        Guid tenantId,
        Guid productId,
        bool hasVariants,
        CancellationToken cancellationToken)
    {
        try
        {
            // Fetch product from MongoDB to get full data
            var product = await _collection.Find(
                Builders<ProductDocument>.Filter.And(
                    Builders<ProductDocument>.Filter.Eq(p => p.TenantId, tenantId),
                    Builders<ProductDocument>.Filter.Eq(p => p.ProductId, productId)
                )).FirstOrDefaultAsync(cancellationToken);

            if (product == null)
            {
                _logger.LogDebug(
                    "Product not found for ES HasVariants sync | ProductId: {ProductId}",
                    productId);
                return;
            }

            if (_dbContext != null)
            {
                product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                    _dbContext,
                    product,
                    tenantId,
                    cancellationToken);

                if (product == null)
                {
                    _logger.LogWarning(
                        "SQL product/variant authority not found for ES HasVariants sync; deleting search document fail-closed | ProductId: {ProductId} | TenantId: {TenantId}",
                        productId,
                        tenantId);
                    await _searchIndexService.DeleteAsync(tenantId, productId, cancellationToken);
                    return;
                }
            }

            var actualHasVariants = product.HasVariants;

            // Create ES document with updated HasVariants
            var document = ProductSearchDocumentMapper.MapFromProductDocument(
                product,
                hasVariantsOverride: actualHasVariants);

            var success = await _searchIndexService.IndexAsync(document, cancellationToken);

            if (success)
            {
                _logger.LogInformation(
                    "Synced HasVariants to Elasticsearch | ProductId: {ProductId} | HasVariants: {HasVariants} | TenantId: {TenantId}",
                    productId, actualHasVariants, tenantId);
            }
            else
            {
                _logger.LogWarning(
                    "Failed to sync HasVariants to Elasticsearch | ProductId: {ProductId}",
                    productId);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal - log and continue
            _logger.LogWarning(ex,
                "Exception syncing HasVariants to Elasticsearch | ProductId: {ProductId}",
                productId);
        }
    }

    private async Task RecomposeParentProductProjectionAsync(
        Guid tenantId,
        Guid productId,
        Guid eventId,
        DateTime eventOccurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        if (_productProjectionWriter == null)
        {
            return;
        }

        var appliedVersion = eventVersion;
        var appliedOccurredAtUtc = eventOccurredAtUtc;

        if (_dbContext != null)
        {
            var sqlProduct = await _dbContext.Products
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(product => product.TenantId == tenantId && product.Id == productId)
                .Select(product => new
                {
                    product.CreatedAt,
                    product.UpdatedAt,
                    product.ProjectionMutationVersion
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (sqlProduct != null)
            {
                appliedVersion = Math.Max(
                    eventVersion,
                    ProductEventVersionHelper.ToProductEventVersion(sqlProduct.ProjectionMutationVersion));

                var sqlOccurredAtUtc = sqlProduct.UpdatedAt ?? sqlProduct.CreatedAt;
                if (sqlOccurredAtUtc > appliedOccurredAtUtc)
                {
                    appliedOccurredAtUtc = sqlOccurredAtUtc;
                }
            }
        }

        var writeResult = await _productProjectionWriter.ApplyAsync(
            new ProductSqlAuthorityProjectionWriteRequest(
                tenantId,
                productId,
                new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = productId,
                    CreatedAtUtc = appliedOccurredAtUtc
                },
                eventId,
                appliedOccurredAtUtc,
                appliedVersion),
            cancellationToken);

        _logger.LogInformation(
            "Parent product projection recomposed from variant lifecycle event | ProductId: {ProductId} | TenantId: {TenantId} | EventId: {EventId} | EventVersion: {EventVersion} | AppliedVersion: {AppliedVersion} | Result: {Result}",
            productId,
            tenantId,
            eventId,
            eventVersion,
            appliedVersion,
            writeResult.Disposition);
    }

    private sealed record SqlVariantAuthoritySnapshot(
        int EventVersion,
        string? SKU,
        string? Barcode,
        decimal Price,
        decimal? OnlinePrice,
        decimal CostPrice,
        bool IsActive,
        Guid? MediaId,
        string? ImageUrl,
        int SortOrder,
        List<VariantAttributeData> Attributes);

    private sealed record SqlVariantStockAuthoritySnapshot(
        decimal TotalStockQuantity,
        DateTime StockUpdatedAtUtc);

    private sealed class ProjectionNotReadyException : Exception
    {
        public ProjectionNotReadyException(string message) : base(message)
        {
        }
    }

    #endregion
}

/// <summary>
/// Consumer definition for ProductVariantProjectionConsumer.
/// Configures retry policy and error handling.
/// </summary>
public class ProductVariantProjectionConsumerDefinition : ConsumerDefinition<ProductVariantProjectionConsumer>
{
    public ProductVariantProjectionConsumerDefinition()
    {
        EndpointName = "product-variant-projection";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ProductVariantProjectionConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Configure retry policy for transient failures
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30)));
    }
}
