using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Projects Wishlist events from PostgreSQL (Write Model) to MongoDB (Read Model).
/// 
/// ARCHITECTURAL RULES:
/// - Handles all Wishlist events
/// - Idempotent projections (safe to replay)
/// - Tenant isolation enforced - Guid.Empty rejected
/// - Invalidates Redis cache on changes
/// - Uses embedded array pattern for wishlist items
/// 
/// COLLECTION STRUCTURE:
/// - One document per customer containing items array
/// - Uses $push/$pull for efficient array operations
/// - Maintains totalCount for badge display
/// 
/// Phase: 10.3.6 - Wishlist (Production-grade)
/// </summary>
public class OnlineCustomerWishlistProjectionConsumer :
    IConsumer<OnlineCustomerWishlistItemAddedEvent>,
    IConsumer<OnlineCustomerWishlistItemRemovedEvent>,
    IConsumer<OnlineCustomerWishlistClearedEvent>
{
    private readonly IMongoCollection<OnlineCustomerWishlistDocument> _collection;
    private readonly IStorefrontWishlistCacheInvalidator _cacheInvalidator;
    private readonly ILogger<OnlineCustomerWishlistProjectionConsumer> _logger;

    public OnlineCustomerWishlistProjectionConsumer(
        IMongoDatabase mongoDatabase,
        IStorefrontWishlistCacheInvalidator cacheInvalidator,
        ILogger<OnlineCustomerWishlistProjectionConsumer> logger)
    {
        _collection = mongoDatabase.GetCollection<OnlineCustomerWishlistDocument>("online_customer_wishlists");
        _cacheInvalidator = cacheInvalidator;
        _logger = logger;

        // PHASE 10.3.6 - Startup log for debugging consumer registration
        _logger.LogInformation("[Wishlist Projection Consumer] STARTED - Ready to process wishlist events via RabbitMQ");

        // Ensure indexes exist
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        try
        {
            var indexKeys = Builders<OnlineCustomerWishlistDocument>.IndexKeys;

            // Unique composite index on (TenantId, CustomerId)
            var uniqueIndex = new CreateIndexModel<OnlineCustomerWishlistDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.CustomerId),
                new CreateIndexOptions { Unique = true, Name = "IX_TenantId_CustomerId" });

            // Index on (TenantId, UpdatedAtUtc)
            var updatedAtIndex = new CreateIndexModel<OnlineCustomerWishlistDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.UpdatedAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_UpdatedAtUtc" });

            var clearOrderingIndex = new CreateIndexModel<OnlineCustomerWishlistDocument>(
                indexKeys
                    .Ascending(x => x.TenantId)
                    .Ascending(x => x.CustomerId)
                    .Descending(x => x.LastClearEventOccurredAtUtc)
                    .Descending(x => x.LastClearEventVersion),
                new CreateIndexOptions { Name = "IX_Wishlist_ClearEventOrdering" });

            _collection.Indexes.CreateMany(new[] { uniqueIndex, updatedAtIndex, clearOrderingIndex });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Wishlist] Failed to ensure MongoDB indexes for online_customer_wishlists collection");
        }
    }

    public async Task Consume(ConsumeContext<OnlineCustomerWishlistItemAddedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CustomerId, nameof(OnlineCustomerWishlistItemAddedEvent)))
            return;

        _logger.LogInformation(
            "[Wishlist] Processing {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductId: {ProductId} | TraceId: {TraceId}",
            "ITEM_ADDED",
            @event.TenantId,
            @event.CustomerId,
            @event.ProductId,
            context.MessageId);

        try
        {
            var newItem = new WishlistItemEntry
            {
                ProductId = @event.ProductId,
                AddedAtUtc = @event.AddedAtUtc,
                IsRemoved = false,
                LastEventId = @event.EventId,
                LastEventOccurredAtUtc = @event.OccurredAtUtc,
                LastEventVersion = @event.EventVersion
            };

            var applied = await UpdateExistingWishlistItemIfFreshAsync(
                @event.TenantId!.Value,
                @event.CustomerId,
                @event.ProductId,
                @event,
                Builders<OnlineCustomerWishlistDocument>.Update
                    .Set("Items.$[item].AddedAtUtc", @event.AddedAtUtc)
                    .Set("Items.$[item].IsRemoved", false)
                    .Set("Items.$[item].RemovedAtUtc", (DateTime?)null)
                    .Set("Items.$[item].LastEventId", @event.EventId.ToString())
                    .Set("Items.$[item].LastEventOccurredAtUtc", @event.OccurredAtUtc)
                    .Set("Items.$[item].LastEventVersion", @event.EventVersion)
                    .Set(x => x.UpdatedAtUtc, @event.OccurredAtUtc),
                cancellationToken);

            if (!applied)
            {
                applied = await PushWishlistItemIfMissingAndFreshAsync(
                    @event.TenantId!.Value,
                    @event.CustomerId,
                    @event.ProductId,
                    @event,
                    newItem,
                    cancellationToken);
            }

            if (!applied)
            {
                _logger.LogInformation(
                    "[Wishlist] ITEM_ADDED ignored as stale or duplicate | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductId: {ProductId} | EventId: {EventId}",
                    @event.TenantId,
                    @event.CustomerId,
                    @event.ProductId,
                    @event.EventId);
                return;
            }

            await RecalculateTotalCountAsync(@event.TenantId!.Value, @event.CustomerId, cancellationToken);

            await _cacheInvalidator.InvalidateAsync(
                @event.TenantId!.Value,
                @event.CustomerId,
                cancellationToken);

            _logger.LogInformation(
                "[Wishlist] {Action} projected successfully | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductId: {ProductId}",
                "ITEM_ADDED",
                @event.TenantId,
                @event.CustomerId,
                @event.ProductId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Wishlist] Failed to project {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductId: {ProductId}",
                "ITEM_ADDED",
                @event.TenantId,
                @event.CustomerId,
                @event.ProductId);
            throw; // Retry via MassTransit
        }
    }

    public async Task Consume(ConsumeContext<OnlineCustomerWishlistItemRemovedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CustomerId, nameof(OnlineCustomerWishlistItemRemovedEvent)))
            return;

        _logger.LogInformation(
            "[Wishlist] Processing {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductId: {ProductId} | TraceId: {TraceId}",
            "ITEM_REMOVED",
            @event.TenantId,
            @event.CustomerId,
            @event.ProductId,
            context.MessageId);

        try
        {
            var applied = await UpdateExistingWishlistItemIfFreshAsync(
                @event.TenantId!.Value,
                @event.CustomerId,
                @event.ProductId,
                @event,
                Builders<OnlineCustomerWishlistDocument>.Update
                    .Set("Items.$[item].IsRemoved", true)
                    .Set("Items.$[item].RemovedAtUtc", @event.RemovedAtUtc)
                    .Set("Items.$[item].LastEventId", @event.EventId.ToString())
                    .Set("Items.$[item].LastEventOccurredAtUtc", @event.OccurredAtUtc)
                    .Set("Items.$[item].LastEventVersion", @event.EventVersion)
                    .Set(x => x.UpdatedAtUtc, @event.OccurredAtUtc),
                cancellationToken);

            if (!applied)
            {
                applied = await PushWishlistItemIfMissingAndFreshAsync(
                    @event.TenantId!.Value,
                    @event.CustomerId,
                    @event.ProductId,
                    @event,
                    new WishlistItemEntry
                    {
                        ProductId = @event.ProductId,
                        AddedAtUtc = @event.RemovedAtUtc,
                        IsRemoved = true,
                        RemovedAtUtc = @event.RemovedAtUtc,
                        LastEventId = @event.EventId,
                        LastEventOccurredAtUtc = @event.OccurredAtUtc,
                        LastEventVersion = @event.EventVersion
                    },
                    cancellationToken);
            }

            if (!applied)
            {
                _logger.LogInformation(
                    "[Wishlist] ITEM_REMOVED ignored as stale or duplicate | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductId: {ProductId} | EventId: {EventId}",
                    @event.TenantId,
                    @event.CustomerId,
                    @event.ProductId,
                    @event.EventId);
                return;
            }

            await RecalculateTotalCountAsync(@event.TenantId!.Value, @event.CustomerId, cancellationToken);

            await _cacheInvalidator.InvalidateAsync(
                @event.TenantId!.Value,
                @event.CustomerId,
                cancellationToken);

            _logger.LogInformation(
                "[Wishlist] {Action} projected successfully | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductId: {ProductId}",
                "ITEM_REMOVED",
                @event.TenantId,
                @event.CustomerId,
                @event.ProductId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Wishlist] Failed to project {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductId: {ProductId}",
                "ITEM_REMOVED",
                @event.TenantId,
                @event.CustomerId,
                @event.ProductId);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<OnlineCustomerWishlistClearedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CustomerId, nameof(OnlineCustomerWishlistClearedEvent)))
            return;

        _logger.LogInformation(
            "[Wishlist] Processing {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | ItemsCleared: {ItemsCleared} | TraceId: {TraceId}",
            "WISHLIST_CLEARED",
            @event.TenantId,
            @event.CustomerId,
            @event.ItemsCleared,
            context.MessageId);

        try
        {
            var update = Builders<OnlineCustomerWishlistDocument>.Update
                .Set(x => x.Items, new List<WishlistItemEntry>())
                .Set(x => x.TotalCount, 0)
                .Set(x => x.UpdatedAtUtc, @event.ClearedAtUtc)
                .Set(x => x.LastClearEventId, @event.EventId)
                .Set(x => x.LastClearEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastClearEventVersion, @event.EventVersion)
                .Set(x => x.LastClearedAtUtc, @event.ClearedAtUtc)
                .SetOnInsert(x => x.Id, ObjectId.GenerateNewId().ToString())
                .SetOnInsert(x => x.TenantId, @event.TenantId!.Value)
                .SetOnInsert(x => x.CustomerId, @event.CustomerId)
                .SetOnInsert(x => x.SchemaVersion, 1);

            var result = await _collection.UpdateOneAsync(
                BuildFreshClearEventFilter(
                    @event.TenantId!.Value,
                    @event.CustomerId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    @event.EventVersion),
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);

            if (result.MatchedCount == 0 && result.ModifiedCount == 0 && result.UpsertedId == null)
            {
                _logger.LogInformation(
                    "[Wishlist] WISHLIST_CLEARED ignored as stale or duplicate | TenantId: {TenantId} | CustomerId: {CustomerId} | EventId: {EventId}",
                    @event.TenantId,
                    @event.CustomerId,
                    @event.EventId);
                return;
            }

            await _cacheInvalidator.InvalidateAsync(
                @event.TenantId!.Value,
                @event.CustomerId,
                cancellationToken);

            _logger.LogInformation(
                "[Wishlist] {Action} projected successfully | TenantId: {TenantId} | CustomerId: {CustomerId} | ItemsCleared: {ItemsCleared}",
                "WISHLIST_CLEARED",
                @event.TenantId,
                @event.CustomerId,
                @event.ItemsCleared);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Wishlist] Failed to project {Action} | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "WISHLIST_CLEARED",
                @event.TenantId,
                @event.CustomerId);
            throw;
        }
    }

    /// <summary>
    /// Validates TenantId is present and not Guid.Empty.
    /// </summary>
    private bool ValidateTenantId(Guid? tenantId, Guid customerId, string eventName)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            _logger.LogError(
                "[Wishlist] SECURITY: {EventName} REJECTED - Missing or empty TenantId | CustomerId: {CustomerId}",
                eventName, customerId);
            return false;
        }
        return true;
    }

    private async Task<bool> UpdateExistingWishlistItemIfFreshAsync(
        Guid tenantId,
        Guid customerId,
        Guid productId,
        IntegrationEvent @event,
        UpdateDefinition<OnlineCustomerWishlistDocument> update,
        CancellationToken cancellationToken)
    {
        var filter = Builders<OnlineCustomerWishlistDocument>.Filter.And(
            BuildNoNewerClearEventFilter(tenantId, customerId, @event.OccurredAtUtc, @event.EventVersion),
            Builders<OnlineCustomerWishlistDocument>.Filter.ElemMatch(x => x.Items, item => item.ProductId == productId));

        var options = new UpdateOptions
        {
            ArrayFilters = new[]
            {
                BuildFreshWishlistItemArrayFilter(productId, @event.EventId, @event.OccurredAtUtc, @event.EventVersion)
            }
        };

        var result = await _collection.UpdateOneAsync(filter, update, options, cancellationToken);
        return result.ModifiedCount > 0;
    }

    private async Task<bool> PushWishlistItemIfMissingAndFreshAsync(
        Guid tenantId,
        Guid customerId,
        Guid productId,
        IntegrationEvent @event,
        WishlistItemEntry item,
        CancellationToken cancellationToken)
    {
        var filter = Builders<OnlineCustomerWishlistDocument>.Filter.And(
            BuildNoNewerClearEventFilter(tenantId, customerId, @event.OccurredAtUtc, @event.EventVersion),
            Builders<OnlineCustomerWishlistDocument>.Filter.Not(
                Builders<OnlineCustomerWishlistDocument>.Filter.ElemMatch(x => x.Items, entry => entry.ProductId == productId)));

        var update = Builders<OnlineCustomerWishlistDocument>.Update
            .Push(x => x.Items, item)
            .Set(x => x.UpdatedAtUtc, @event.OccurredAtUtc)
            .SetOnInsert(x => x.Id, ObjectId.GenerateNewId().ToString())
            .SetOnInsert(x => x.TenantId, tenantId)
            .SetOnInsert(x => x.CustomerId, customerId)
            .SetOnInsert(x => x.SchemaVersion, 1);

        try
        {
            var result = await _collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);

            return result.ModifiedCount > 0 || result.UpsertedId != null;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return await UpdateExistingWishlistItemIfFreshAsync(
                tenantId,
                customerId,
                productId,
                @event,
                Builders<OnlineCustomerWishlistDocument>.Update
                    .Set("Items.$[item].AddedAtUtc", item.AddedAtUtc)
                    .Set("Items.$[item].IsRemoved", item.IsRemoved)
                    .Set("Items.$[item].RemovedAtUtc", item.RemovedAtUtc)
                    .Set("Items.$[item].LastEventId", @event.EventId.ToString())
                    .Set("Items.$[item].LastEventOccurredAtUtc", @event.OccurredAtUtc)
                    .Set("Items.$[item].LastEventVersion", @event.EventVersion)
                    .Set(x => x.UpdatedAtUtc, @event.OccurredAtUtc),
                cancellationToken);
        }
    }

    private async Task RecalculateTotalCountAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken)
    {
        var items = await _collection
            .Find(x => x.TenantId == tenantId && x.CustomerId == customerId)
            .Project(x => x.Items)
            .FirstOrDefaultAsync(cancellationToken);

        var activeCount = Math.Max(0, items?.Count(item => !item.IsRemoved) ?? 0);

        await _collection.UpdateOneAsync(
            Builders<OnlineCustomerWishlistDocument>.Filter.And(
                Builders<OnlineCustomerWishlistDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<OnlineCustomerWishlistDocument>.Filter.Eq(x => x.CustomerId, customerId)),
            Builders<OnlineCustomerWishlistDocument>.Update.Set(x => x.TotalCount, activeCount),
            cancellationToken: cancellationToken);
    }

    private static FilterDefinition<OnlineCustomerWishlistDocument> BuildNoNewerClearEventFilter(
        Guid tenantId,
        Guid customerId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        var filter = Builders<OnlineCustomerWishlistDocument>.Filter;

        return filter.And(
            filter.Eq(x => x.TenantId, tenantId),
            filter.Eq(x => x.CustomerId, customerId),
            filter.Or(
                filter.Exists(x => x.LastClearEventOccurredAtUtc, false),
                filter.Eq(x => x.LastClearEventOccurredAtUtc, null),
                filter.Lt(x => x.LastClearEventOccurredAtUtc, occurredAtUtc),
                filter.And(
                    filter.Eq(x => x.LastClearEventOccurredAtUtc, occurredAtUtc),
                    filter.Lt(x => x.LastClearEventVersion, eventVersion))));
    }

    private static FilterDefinition<OnlineCustomerWishlistDocument> BuildFreshClearEventFilter(
        Guid tenantId,
        Guid customerId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        var filter = Builders<OnlineCustomerWishlistDocument>.Filter;

        return filter.And(
            filter.Eq(x => x.TenantId, tenantId),
            filter.Eq(x => x.CustomerId, customerId),
            filter.Ne(x => x.LastClearEventId, eventId),
            filter.Or(
                filter.Exists(x => x.LastClearEventOccurredAtUtc, false),
                filter.Eq(x => x.LastClearEventOccurredAtUtc, null),
                filter.Lt(x => x.LastClearEventOccurredAtUtc, occurredAtUtc),
                filter.And(
                    filter.Eq(x => x.LastClearEventOccurredAtUtc, occurredAtUtc),
                    filter.Lt(x => x.LastClearEventVersion, eventVersion))));
    }

    private static ArrayFilterDefinition BuildFreshWishlistItemArrayFilter(
        Guid productId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        return new BsonDocumentArrayFilterDefinition<BsonDocument>(
            new BsonDocument
            {
                {
                    "$and",
                    new BsonArray
                    {
                        new BsonDocument("item.ProductId", productId.ToString()),
                        new BsonDocument("item.LastEventId", new BsonDocument("$ne", eventId.ToString())),
                        new BsonDocument
                        {
                            {
                                "$or",
                                new BsonArray
                                {
                                    new BsonDocument("item.LastEventOccurredAtUtc", new BsonDocument("$exists", false)),
                                    new BsonDocument("item.LastEventOccurredAtUtc", BsonNull.Value),
                                    new BsonDocument("item.LastEventOccurredAtUtc", new BsonDocument("$lt", occurredAtUtc)),
                                    new BsonDocument
                                    {
                                        {
                                            "$and",
                                            new BsonArray
                                            {
                                                new BsonDocument("item.LastEventOccurredAtUtc", occurredAtUtc),
                                                new BsonDocument("item.LastEventVersion", new BsonDocument("$lt", eventVersion))
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            });
    }

}

/// <summary>
/// Consumer definition for OnlineCustomerWishlistProjectionConsumer.
/// Configures endpoint name and retry policy.
/// </summary>
public class OnlineCustomerWishlistProjectionConsumerDefinition : ConsumerDefinition<OnlineCustomerWishlistProjectionConsumer>
{
    public OnlineCustomerWishlistProjectionConsumerDefinition()
    {
        EndpointName = "online-customer-wishlist-projection";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<OnlineCustomerWishlistProjectionConsumer> consumerConfigurator)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
    }
}
