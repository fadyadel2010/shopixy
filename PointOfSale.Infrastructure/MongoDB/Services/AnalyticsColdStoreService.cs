using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// MongoDB implementation for analytics cold storage.
/// 
/// COLLECTION: analytics_events
/// 
/// INDEXES (created on startup):
/// 1. Unique: (TenantId, EventId) - For idempotency
/// 2. Compound: (TenantId, OccurredAtUtc) - For time-range queries
/// 3. Compound: (TenantId, EventType, OccurredAtUtc) - For type-filtered queries
/// 
/// APPEND-ONLY: No updates allowed - events are immutable.
/// </summary>
public class AnalyticsColdStoreService : IAnalyticsColdStoreService
{
    private readonly IMongoCollection<AnalyticsEventDocument> _collection;
    private readonly ILogger<AnalyticsColdStoreService> _logger;

    public AnalyticsColdStoreService(
        IMongoDatabase mongoDatabase,
        ILogger<AnalyticsColdStoreService> logger)
    {
        _collection = mongoDatabase.GetCollection<AnalyticsEventDocument>("analytics_events");
        _logger = logger;

        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        try
        {
            var indexKeys = Builders<AnalyticsEventDocument>.IndexKeys;

            // Unique index for idempotency
            var idempotencyIndex = new CreateIndexModel<AnalyticsEventDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.EventId),
                new CreateIndexOptions { Unique = true, Name = "IX_TenantId_EventId" });

            // Time-range query index
            var timeRangeIndex = new CreateIndexModel<AnalyticsEventDocument>(
                indexKeys.Ascending(x => x.TenantId).Descending(x => x.OccurredAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_OccurredAtUtc" });

            // Type-filtered query index
            var typeFilterIndex = new CreateIndexModel<AnalyticsEventDocument>(
                indexKeys.Ascending(x => x.TenantId)
                    .Ascending(x => x.EventType)
                    .Descending(x => x.OccurredAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_EventType_OccurredAtUtc" });

            // Customer journey index
            var customerIndex = new CreateIndexModel<AnalyticsEventDocument>(
                indexKeys.Ascending(x => x.TenantId)
                    .Ascending(x => x.CustomerId)
                    .Descending(x => x.OccurredAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_CustomerId_OccurredAtUtc" });

            // Session tracking index
            var sessionIndex = new CreateIndexModel<AnalyticsEventDocument>(
                indexKeys.Ascending(x => x.TenantId)
                    .Ascending(x => x.SessionId)
                    .Descending(x => x.OccurredAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_SessionId_OccurredAtUtc" });

            _collection.Indexes.CreateMany(new[]
            {
                idempotencyIndex,
                timeRangeIndex,
                typeFilterIndex,
                customerIndex,
                sessionIndex
            });

            _logger.LogInformation("[Analytics] MongoDB indexes created for analytics_events collection");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Analytics] Failed to create MongoDB indexes (may already exist)");
        }
    }

    public async Task<bool> StoreEventAsync(
        AnalyticsEvent analyticsEvent,
        CancellationToken cancellationToken = default)
    {
        return await StoreEventWithOutcomeAsync(analyticsEvent, cancellationToken) !=
            AnalyticsEventStoreOutcome.Failed;
    }

    public async Task<AnalyticsEventStoreOutcome> StoreEventWithOutcomeAsync(
        AnalyticsEvent analyticsEvent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var document = MapToDocument(analyticsEvent);
            await _collection.InsertOneAsync(document, cancellationToken: cancellationToken);

            _logger.LogDebug(
                "[Analytics] Event stored in MongoDB | TenantId: {TenantId} | EventId: {EventId} | Type: {Type}",
                analyticsEvent.TenantId, analyticsEvent.EventId, analyticsEvent.Type);

            return AnalyticsEventStoreOutcome.Stored;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Idempotency - event already exists
            _logger.LogDebug(
                "[Analytics] Event already exists (idempotent) | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId, analyticsEvent.EventId);
            return AnalyticsEventStoreOutcome.AlreadyExists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Analytics] Failed to store event in MongoDB | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId, analyticsEvent.EventId);
            return AnalyticsEventStoreOutcome.Failed;
        }
    }

    public async Task<bool> EventExistsAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<AnalyticsEventDocument>.Filter.And(
                Builders<AnalyticsEventDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<AnalyticsEventDocument>.Filter.Eq(x => x.EventId, eventId));

            return await _collection.Find(filter).AnyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics] Failed to check event existence in MongoDB | TenantId: {TenantId} | EventId: {EventId}",
                tenantId, eventId);
            return false;
        }
    }

    public async Task<List<AnalyticsEvent>> GetEventsByTimeRangeAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        AnalyticsEventType? eventType = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        // Validate pagination parameters before entering try/catch
        if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip), "Skip must be non-negative.");
        if (take < 0) throw new ArgumentOutOfRangeException(nameof(take), "Take must be non-negative.");
        if (take == 0) return new List<AnalyticsEvent>();

        try
        {
            var filterBuilder = Builders<AnalyticsEventDocument>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(x => x.TenantId, tenantId),
                filterBuilder.Gte(x => x.OccurredAtUtc, startUtc),
                filterBuilder.Lt(x => x.OccurredAtUtc, endUtc));

            if (eventType.HasValue)
            {
                filter = filterBuilder.And(filter,
                    filterBuilder.Eq(x => x.EventType, eventType.Value.ToString()));
            }

            var events = new List<AnalyticsEvent>();
            var currentSkip = skip;
            var sourceWindowEndLong = (long)skip + take;
            var sourceWindowEnd = (int)Math.Min(sourceWindowEndLong, int.MaxValue); // Bound reads to source window
            var sort = Builders<AnalyticsEventDocument>.Sort
                .Descending(x => x.OccurredAtUtc)
                .Ascending(x => x.EventId); // Tiebreaker for stable ordering when timestamps match

            // Page through source with per-item error handling
            // Read only within the original source window to maintain stable pagination
            // Accept returning fewer than 'take' events if mapping fails
            const int maxReadLimit = 10000; // Safety limit to prevent infinite loops
            var totalDocumentsRead = 0;

            while (events.Count < take && currentSkip < sourceWindowEnd && totalDocumentsRead < maxReadLimit)
            {
                // Calculate remaining documents in the source window
                var remainingInWindow = sourceWindowEnd - currentSkip;
                var currentBatchSize = Math.Min(1000, remainingInWindow);

                var documents = await _collection
                    .Find(filter)
                    .Sort(sort)
                    .Skip(currentSkip)
                    .Limit(currentBatchSize)
                    .ToListAsync(cancellationToken);

                if (documents.Count == 0)
                {
                    // Source exhausted
                    break;
                }

                // Map documents with per-item error handling to prevent one bad document from poisoning the entire result
                foreach (var doc in documents)
                {
                    try
                    {
                        events.Add(MapToEvent(doc));
                        
                        // Stop if we've reached the desired count
                        if (events.Count >= take)
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "[Analytics] Failed to map document to event (skipping) | TenantId: {TenantId} | EventId: {EventId} | Reason: {Reason}",
                            doc.TenantId, doc.EventId, ex.Message);
                    }
                }

                // Increment skip by the number of documents read from source
                currentSkip += documents.Count;
                totalDocumentsRead += documents.Count;
            }

            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Analytics] Failed to get events from MongoDB | TenantId: {TenantId}",
                tenantId);
            return new List<AnalyticsEvent>();
        }
    }

    public async Task<Dictionary<AnalyticsEventType, long>> CountEventsByTypeAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<AnalyticsEventType, long>();

        try
        {
            var filterBuilder = Builders<AnalyticsEventDocument>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(x => x.TenantId, tenantId),
                filterBuilder.Gte(x => x.OccurredAtUtc, startUtc),
                filterBuilder.Lt(x => x.OccurredAtUtc, endUtc));

            var pipeline = _collection.Aggregate()
                .Match(filter)
                .Group(
                    x => x.EventType,
                    g => new { EventType = g.Key, Count = g.Count() });

            var counts = await pipeline.ToListAsync(cancellationToken);

            foreach (var item in counts)
            {
                if (Enum.TryParse<AnalyticsEventType>(item.EventType, out var eventType))
                {
                    result[eventType] = item.Count;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Analytics] Failed to count events in MongoDB | TenantId: {TenantId}",
                tenantId);
        }

        return result;
    }

    #region Mapping

    private static AnalyticsEventDocument MapToDocument(AnalyticsEvent evt)
    {
        var currency = FinancialCurrencyGuard.RequireCurrency(
            evt.Context?.Currency,
            "analytics.context.currency",
            $"tenantId:{evt.TenantId:D};eventId:{evt.EventId:D}");

        return new AnalyticsEventDocument
        {
            EventId = evt.EventId,
            TenantId = evt.TenantId,
            EventType = evt.Type.ToString(),
            OccurredAtUtc = evt.OccurredAtUtc,
            ReceivedAtUtc = evt.ReceivedAtUtc,
            StoredAtUtc = DateTime.UtcNow,
            SchemaVersion = evt.SchemaVersion,

            // Actor
            CustomerId = evt.Actor?.CustomerId,
            SessionId = evt.Actor?.SessionId,
            IsAuthenticated = evt.Actor?.IsAuthenticated ?? false,

            // Context
            PageUrl = evt.Context?.PageUrl ?? string.Empty,
            Referrer = evt.Context?.Referrer,
            UserAgent = evt.Context?.UserAgent,
            IpAddress = evt.Context?.IpAddress,
            Language = evt.Context?.Language,
            Currency = currency,
            DeviceType = evt.Context?.DeviceType,
            UtmSource = evt.Context?.UtmSource,
            UtmMedium = evt.Context?.UtmMedium,
            UtmCampaign = evt.Context?.UtmCampaign,
            UtmContent = evt.Context?.UtmContent,
            UtmTerm = evt.Context?.UtmTerm,

            // Payload
            ProductId = evt.Payload?.ProductId,
            VariantId = evt.Payload?.VariantId,
            OrderId = evt.Payload?.OrderId,
            CategoryId = evt.Payload?.CategoryId,
            Value = evt.Payload?.Value,
            Quantity = evt.Payload?.Quantity,
            SearchQuery = evt.Payload?.SearchQuery,
            ProductName = evt.Payload?.ProductName,
            ProductSku = evt.Payload?.ProductSku,
            Filters = evt.Payload?.Filters != null
                ? new BsonDocument(evt.Payload.Filters.ToDictionary(k => k.Key, v => BsonValue.Create(v.Value)))
                : null,
            Extra = evt.Payload?.Extra != null
                ? new BsonDocument(evt.Payload.Extra.ToDictionary(k => k.Key, v => BsonValue.Create(v.Value)))
                : null
        };
    }

    private static AnalyticsEvent MapToEvent(AnalyticsEventDocument doc)
    {
        var eventType = Enum.TryParse<AnalyticsEventType>(doc.EventType, out var type)
            ? type
            : AnalyticsEventType.PageView;

        var currency = FinancialCurrencyGuard.RequireCurrency(
            doc.Currency,
            "analytics.document.currency",
            $"tenantId:{doc.TenantId:D};eventId:{doc.EventId:D}");

        return new AnalyticsEvent
        {
            EventId = doc.EventId,
            TenantId = doc.TenantId,
            Type = eventType,
            OccurredAtUtc = doc.OccurredAtUtc,
            ReceivedAtUtc = doc.ReceivedAtUtc,
            SchemaVersion = doc.SchemaVersion,
            Actor = new AnalyticsActor
            {
                CustomerId = doc.CustomerId,
                SessionId = doc.SessionId,
                IsAuthenticated = doc.IsAuthenticated
            },
            Context = new AnalyticsContext
            {
                PageUrl = doc.PageUrl,
                Referrer = doc.Referrer,
                UserAgent = doc.UserAgent ?? string.Empty,
                IpAddress = doc.IpAddress ?? string.Empty,
                Language = doc.Language ?? "en",
                Currency = currency,
                DeviceType = doc.DeviceType ?? "unknown",
                UtmSource = doc.UtmSource,
                UtmMedium = doc.UtmMedium,
                UtmCampaign = doc.UtmCampaign,
                UtmContent = doc.UtmContent,
                UtmTerm = doc.UtmTerm
            },
            Payload = new AnalyticsPayload
            {
                ProductId = doc.ProductId,
                VariantId = doc.VariantId,
                OrderId = doc.OrderId,
                CategoryId = doc.CategoryId,
                Value = doc.Value,
                Quantity = doc.Quantity,
                SearchQuery = doc.SearchQuery,
                ProductName = doc.ProductName,
                ProductSku = doc.ProductSku,
                Filters = doc.Filters?.ToDictionary(
                    x => x.Name,
                    x => x.Value?.AsString ?? string.Empty),
                Extra = doc.Extra?.ToDictionary(
                    x => x.Name,
                    x => (object)(x.Value?.AsString ?? string.Empty))
            }
        };
    }

    #endregion
}
