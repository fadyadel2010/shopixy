using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Core.Analytics.Attribution;
using PointOfSale.Infrastructure.Analytics;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Redis.Configuration;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Attribution;

/// <summary>
/// Order Attribution Store implementation using MongoDB (primary) and Redis (cache).
/// 
/// STORAGE STRATEGY:
/// - MongoDB: Primary storage, append-only, permanent record
/// - Redis: Cache for quick dashboard lookups
/// 
/// KEY PATTERNS (Redis):
/// - Order: attribution:tenant:{tenantId}:order:{orderId}:{model}
/// 
/// IMMUTABILITY: Order attributions are NEVER updated after creation.
/// </summary>
public class OrderAttributionStore : IOrderAttributionStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMongoCollection<OrderAttributionDocument> _collection;
    private readonly IStorefrontAnalyticsAuthorityReader _authorityReader;
    private readonly ILogger<OrderAttributionStore> _logger;
    private readonly string _instanceName;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public OrderAttributionStore(
        IConnectionMultiplexer redis,
        IMongoDatabase mongoDatabase,
        IStorefrontAnalyticsAuthorityReader authorityReader,
        IOptions<RedisSettings> redisSettings,
        ILogger<OrderAttributionStore> logger)
    {
        _redis = redis;
        _collection = mongoDatabase.GetCollection<OrderAttributionDocument>("order_attributions");
        _authorityReader = authorityReader;
        _instanceName = redisSettings.Value.InstanceName;
        _logger = logger;

        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        try
        {
            var indexKeys = Builders<OrderAttributionDocument>.IndexKeys;

            // Unique index for order + model
            var orderModelIndex = new CreateIndexModel<OrderAttributionDocument>(
                indexKeys.Ascending(x => x.TenantId)
                    .Ascending(x => x.OrderId)
                    .Ascending(x => x.Model),
                new CreateIndexOptions { Unique = true, Name = "IX_TenantId_OrderId_Model" });

            // Time-range query index
            var timeRangeIndex = new CreateIndexModel<OrderAttributionDocument>(
                indexKeys.Ascending(x => x.TenantId)
                    .Descending(x => x.AttributedAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_AttributedAtUtc" });

            // Source ROI index
            var sourceIndex = new CreateIndexModel<OrderAttributionDocument>(
                indexKeys.Ascending(x => x.TenantId)
                    .Ascending(x => x.Source)
                    .Ascending(x => x.Model)
                    .Descending(x => x.AttributedAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_Source_Model_AttributedAtUtc" });

            // Campaign ROI index
            var campaignIndex = new CreateIndexModel<OrderAttributionDocument>(
                indexKeys.Ascending(x => x.TenantId)
                    .Ascending(x => x.Campaign)
                    .Ascending(x => x.Model)
                    .Descending(x => x.AttributedAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_Campaign_Model_AttributedAtUtc" });

            _collection.Indexes.CreateMany(new[]
            {
                orderModelIndex,
                timeRangeIndex,
                sourceIndex,
                campaignIndex
            });

            _logger.LogInformation("[Attribution] MongoDB indexes created for order_attributions collection");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Attribution] Failed to create MongoDB indexes (may already exist)");
        }
    }

    public async Task StoreAttributionAsync(
        OrderAttribution attribution,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Store in MongoDB
            var document = MapToDocument(attribution);
            await _collection.InsertOneAsync(document, cancellationToken: cancellationToken);

            // Cache in Redis
            var db = _redis.GetDatabase();
            var key = BuildKey(attribution.TenantId, attribution.OrderId, attribution.Model);
            var attributionJson = JsonSerializer.Serialize(attribution, JsonOptions);
            await db.StringSetAsync(key, attributionJson, CacheTtl);

            _logger.LogInformation(
                "[Attribution] Order attribution stored | TenantId: {TenantId} | OrderId: {OrderId} | Source: {Source} | Model: {Model} | Value: {Value}",
                attribution.TenantId, attribution.OrderId, attribution.Source, attribution.Model, attribution.OrderValue);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogDebug(
                "[Attribution] Order attribution already exists (idempotent) | TenantId: {TenantId} | OrderId: {OrderId} | Model: {Model}",
                attribution.TenantId, attribution.OrderId, attribution.Model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Attribution] Failed to store order attribution | TenantId: {TenantId} | OrderId: {OrderId}",
                attribution.TenantId, attribution.OrderId);
            throw;
        }
    }

    public async Task<OrderAttribution?> GetAttributionAsync(
        Guid tenantId,
        Guid orderId,
        AttributionModel model,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try Redis first
            var db = _redis.GetDatabase();
            var key = BuildKey(tenantId, orderId, model);
            var cached = await db.StringGetAsync(key);

            if (cached.HasValue)
            {
                var cachedAttribution = JsonSerializer.Deserialize<OrderAttribution>(cached!, JsonOptions);
                return cachedAttribution == null
                    ? null
                    : await ApplyAuthoritativeRevenueAsync(cachedAttribution, DateTime.UtcNow, cancellationToken);
            }

            // Fall back to MongoDB
            var filter = Builders<OrderAttributionDocument>.Filter.And(
                Builders<OrderAttributionDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<OrderAttributionDocument>.Filter.Eq(x => x.OrderId, orderId),
                Builders<OrderAttributionDocument>.Filter.Eq(x => x.Model, model.ToString()));

            var document = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);

            if (document != null)
            {
                var attribution = MapToAttribution(document);

                // Cache for future lookups
                var attributionJson = JsonSerializer.Serialize(attribution, JsonOptions);
                await db.StringSetAsync(key, attributionJson, CacheTtl);

                return await ApplyAuthoritativeRevenueAsync(attribution, DateTime.UtcNow, cancellationToken);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Attribution] Failed to get attribution | TenantId: {TenantId} | OrderId: {OrderId} | Model: {Model}",
                tenantId, orderId, model);
            return null;
        }
    }

    public async Task<List<OrderAttribution>> GetAttributionsByRangeAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        AttributionModel? model = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var page = await GetAttributionPageAsync(
            tenantId,
            startUtc,
            endUtc,
            model,
            skip: skip,
            take: take,
            cancellationToken: cancellationToken);
        return page.Items.ToList();
    }

    public async Task<OrderAttributionPage> GetAttributionPageAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        AttributionModel? model = null,
        string? source = null,
        string? campaign = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var commerce = await _authorityReader.GetCommerceSnapshotAsync(
                tenantId,
                startUtc,
                endUtc,
                cancellationToken);
            var purchaseOrderIds = commerce.PurchaseOrderIds.Distinct().ToArray();
            if (purchaseOrderIds.Length == 0)
            {
                return new OrderAttributionPage([], 0);
            }

            var filterBuilder = Builders<OrderAttributionDocument>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(x => x.TenantId, tenantId),
                filterBuilder.In(x => x.OrderId, purchaseOrderIds));

            if (model.HasValue)
            {
                filter = filterBuilder.And(filter,
                    filterBuilder.Eq(x => x.Model, model.Value.ToString()));
            }

            if (!string.IsNullOrWhiteSpace(source))
            {
                filter = filterBuilder.And(
                    filter,
                    filterBuilder.Regex(
                        x => x.Source,
                        new BsonRegularExpression($"^{Regex.Escape(source.Trim())}$", "i")));
            }

            if (!string.IsNullOrWhiteSpace(campaign))
            {
                filter = filterBuilder.And(
                    filter,
                    filterBuilder.Regex(
                        x => x.Campaign,
                        new BsonRegularExpression($"^{Regex.Escape(campaign.Trim())}$", "i")));
            }

            var totalCount = await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

            var documents = await _collection
                .Find(filter)
                .Sort(Builders<OrderAttributionDocument>.Sort.Descending(x => x.AttributedAtUtc))
                .Skip(skip)
                .Limit(take)
                .ToListAsync(cancellationToken);

            var items = documents
                .Select(document =>
                {
                    var attribution = MapToAttribution(document);
                    attribution.OrderValue = commerce.NetRevenueByOrder.GetValueOrDefault(document.OrderId, 0m);
                    return attribution;
                })
                .ToList();
            return new OrderAttributionPage(items, totalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Attribution] Failed to get attributions by range | TenantId: {TenantId}",
                tenantId);
            throw;
        }
    }

    public async Task<List<SourceRevenueSummary>> GetRevenueBySourceAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        AttributionModel model,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var attributions = await GetAttributionsByRangeAsync(
                tenantId,
                startUtc,
                endUtc,
                model,
                take: int.MaxValue,
                cancellationToken: cancellationToken);

            return attributions
                .GroupBy(x => new { x.Source, x.Medium })
                .Select(group => new SourceRevenueSummary
                {
                    Source = group.Key.Source,
                    Medium = group.Key.Medium,
                    TotalRevenue = group.Sum(x => x.OrderValue),
                    OrderCount = group.Select(x => x.OrderId).Distinct().Count()
                })
                .OrderByDescending(x => x.TotalRevenue)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Attribution] Failed to get revenue by source | TenantId: {TenantId}",
                tenantId);
            throw;
        }
    }

    public async Task<List<CampaignRevenueSummary>> GetRevenueByCampaignAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        AttributionModel model,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var attributions = await GetAttributionsByRangeAsync(
                tenantId,
                startUtc,
                endUtc,
                model,
                take: int.MaxValue,
                cancellationToken: cancellationToken);

            return attributions
                .Where(x => !string.IsNullOrWhiteSpace(x.Campaign))
                .GroupBy(x => new { x.Campaign, x.Source, x.Medium })
                .Select(group => new CampaignRevenueSummary
                {
                    Campaign = group.Key.Campaign ?? string.Empty,
                    Source = group.Key.Source,
                    Medium = group.Key.Medium,
                    TotalRevenue = group.Sum(x => x.OrderValue),
                    OrderCount = group.Select(x => x.OrderId).Distinct().Count()
                })
                .OrderByDescending(x => x.TotalRevenue)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Attribution] Failed to get revenue by campaign | TenantId: {TenantId}",
                tenantId);
            throw;
        }
    }

    public async Task<bool> HasAttributionAsync(
        Guid tenantId,
        Guid orderId,
        AttributionModel model,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try Redis first
            var db = _redis.GetDatabase();
            var key = BuildKey(tenantId, orderId, model);
            if (await db.KeyExistsAsync(key))
            {
                return true;
            }

            // Fall back to MongoDB
            var filter = Builders<OrderAttributionDocument>.Filter.And(
                Builders<OrderAttributionDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<OrderAttributionDocument>.Filter.Eq(x => x.OrderId, orderId),
                Builders<OrderAttributionDocument>.Filter.Eq(x => x.Model, model.ToString()));

            return await _collection.Find(filter).AnyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Attribution] Failed to check attribution existence | TenantId: {TenantId} | OrderId: {OrderId}",
                tenantId, orderId);
            return false;
        }
    }

    #region Key Building

    private string BuildKey(Guid tenantId, Guid orderId, AttributionModel model)
    {
        return $"{_instanceName}:attribution:tenant:{tenantId:N}:order:{orderId:N}:{model}";
    }

    #endregion

    #region Mapping

    private async Task<OrderAttribution> ApplyAuthoritativeRevenueAsync(
        OrderAttribution attribution,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var revenue = await _authorityReader.GetNetRevenueByOrderAsync(
            attribution.TenantId,
            [attribution.OrderId],
            asOfUtc,
            cancellationToken);
        attribution.OrderValue = revenue.GetValueOrDefault(attribution.OrderId, 0m);
        return attribution;
    }

    private static OrderAttributionDocument MapToDocument(OrderAttribution attribution)
    {
        return new OrderAttributionDocument
        {
            OrderId = attribution.OrderId,
            TenantId = attribution.TenantId,
            SessionId = attribution.SessionId,
            Model = attribution.Model.ToString(),
            Source = attribution.Source,
            Medium = attribution.Medium,
            Campaign = attribution.Campaign,
            Content = attribution.Content,
            Term = attribution.Term,
            OrderValue = attribution.OrderValue,
            Currency = attribution.Currency,
            AttributedAtUtc = attribution.AttributedAtUtc,
            CustomerId = attribution.CustomerId,
            TimeToConversionSeconds = attribution.TimeToConversion.HasValue
                ? (long)attribution.TimeToConversion.Value.TotalSeconds
                : null,
            StoredAtUtc = DateTime.UtcNow
        };
    }

    private static OrderAttribution MapToAttribution(OrderAttributionDocument document)
    {
        var model = Enum.TryParse<AttributionModel>(document.Model, out var m)
            ? m
            : AttributionModel.LastTouch;

        return new OrderAttribution
        {
            OrderId = document.OrderId,
            TenantId = document.TenantId,
            SessionId = document.SessionId,
            Model = model,
            Source = document.Source,
            Medium = document.Medium,
            Campaign = document.Campaign,
            Content = document.Content,
            Term = document.Term,
            OrderValue = document.OrderValue,
            Currency = document.Currency,
            AttributedAtUtc = document.AttributedAtUtc,
            CustomerId = document.CustomerId,
            TimeToConversion = document.TimeToConversionSeconds.HasValue
                ? TimeSpan.FromSeconds(document.TimeToConversionSeconds.Value)
                : null
        };
    }

    #endregion
}
