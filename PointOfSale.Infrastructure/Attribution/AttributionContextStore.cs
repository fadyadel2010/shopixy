using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using PointOfSale.Core.Analytics.Attribution;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Redis.Configuration;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Attribution;

/// <summary>
/// Attribution Context Store implementation using Redis (hot) and MongoDB (cold).
/// 
/// STORAGE STRATEGY:
/// - Redis: Stores latest context for quick lookup during checkout
/// - MongoDB: Stores ALL contexts for historical first/last touch resolution
/// 
/// KEY PATTERNS (Redis):
/// - First Touch: attribution:tenant:{tenantId}:session:{sessionId}:first
/// - Last Touch: attribution:tenant:{tenantId}:session:{sessionId}:last
/// 
/// TTL: 30 days (sessions rarely last longer)
/// </summary>
public class AttributionContextStore : IAttributionContextStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMongoCollection<AttributionContextDocument> _collection;
    private readonly ILogger<AttributionContextStore> _logger;
    private readonly string _instanceName;

    private static readonly TimeSpan ContextTtl = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public AttributionContextStore(
        IConnectionMultiplexer redis,
        IMongoDatabase mongoDatabase,
        IOptions<RedisSettings> redisSettings,
        ILogger<AttributionContextStore> logger)
    {
        _redis = redis;
        _collection = mongoDatabase.GetCollection<AttributionContextDocument>("attribution_contexts");
        _instanceName = redisSettings.Value.InstanceName;
        _logger = logger;

        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        try
        {
            var indexKeys = Builders<AttributionContextDocument>.IndexKeys;

            // Index for session lookups
            var sessionIndex = new CreateIndexModel<AttributionContextDocument>(
                indexKeys.Ascending(x => x.TenantId)
                    .Ascending(x => x.SessionId)
                    .Ascending(x => x.CapturedAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_SessionId_CapturedAtUtc" });

            // Index for time-range queries
            var timeRangeIndex = new CreateIndexModel<AttributionContextDocument>(
                indexKeys.Ascending(x => x.TenantId)
                    .Descending(x => x.CapturedAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_CapturedAtUtc" });

            // Index for source analysis
            var sourceIndex = new CreateIndexModel<AttributionContextDocument>(
                indexKeys.Ascending(x => x.TenantId)
                    .Ascending(x => x.Source)
                    .Descending(x => x.CapturedAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_Source_CapturedAtUtc" });

            _collection.Indexes.CreateMany(new[]
            {
                sessionIndex,
                timeRangeIndex,
                sourceIndex
            });

            _logger.LogInformation("[Attribution] MongoDB indexes created for attribution_contexts collection");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Attribution] Failed to create MongoDB indexes (may already exist)");
        }
    }

    public async Task StoreContextAsync(
        AttributionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Store in MongoDB (append-only)
            var document = MapToDocument(context);
            // Stable Mongo identity permits attribution-stage retries without duplicate touchpoints.
            if (context.SourceEventId.HasValue)
                document.Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{context.TenantId:D}:{context.SourceEventId:D}"))[..12]).ToLowerInvariant();
            try { await _collection.InsertOneAsync(document, cancellationToken: cancellationToken); }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey) { }

            // Store in Redis (for quick lookup)
            var db = _redis.GetDatabase();
            var contextJson = JsonSerializer.Serialize(context, JsonOptions);

            // Always update last touch
            var lastTouchKey = BuildKey(context.TenantId, context.SessionId, "last");
            await db.StringSetAsync(lastTouchKey, contextJson, ContextTtl);

            // Only set first touch if it doesn't exist
            var firstTouchKey = BuildKey(context.TenantId, context.SessionId, "first");
            await db.StringSetAsync(firstTouchKey, contextJson, ContextTtl, When.NotExists);

            _logger.LogDebug(
                "[Attribution] Context stored | TenantId: {TenantId} | SessionId: {SessionId} | Source: {Source}",
                context.TenantId, context.SessionId, context.Source);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Attribution] Failed to store context | TenantId: {TenantId} | SessionId: {SessionId}",
                context.TenantId, context.SessionId);
            throw;
        }
    }

    public async Task<AttributionContext?> GetFirstTouchContextAsync(
        Guid tenantId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try Redis first
            var db = _redis.GetDatabase();
            var key = BuildKey(tenantId, sessionId, "first");
            var cached = await db.StringGetAsync(key);

            if (cached.HasValue)
            {
                return JsonSerializer.Deserialize<AttributionContext>(cached!, JsonOptions);
            }

            // Fall back to MongoDB
            var filter = Builders<AttributionContextDocument>.Filter.And(
                Builders<AttributionContextDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<AttributionContextDocument>.Filter.Eq(x => x.SessionId, sessionId));

            var document = await _collection
                .Find(filter)
                .Sort(Builders<AttributionContextDocument>.Sort.Ascending(x => x.CapturedAtUtc))
                .FirstOrDefaultAsync(cancellationToken);

            if (document != null)
            {
                var context = MapToContext(document);

                // Cache for future lookups
                var contextJson = JsonSerializer.Serialize(context, JsonOptions);
                await db.StringSetAsync(key, contextJson, ContextTtl);

                return context;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Attribution] Failed to get first touch context | TenantId: {TenantId} | SessionId: {SessionId}",
                tenantId, sessionId);
            return null;
        }
    }

    public async Task<AttributionContext?> GetLastTouchContextAsync(
        Guid tenantId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try Redis first
            var db = _redis.GetDatabase();
            var key = BuildKey(tenantId, sessionId, "last");
            var cached = await db.StringGetAsync(key);

            if (cached.HasValue)
            {
                return JsonSerializer.Deserialize<AttributionContext>(cached!, JsonOptions);
            }

            // Fall back to MongoDB
            var filter = Builders<AttributionContextDocument>.Filter.And(
                Builders<AttributionContextDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<AttributionContextDocument>.Filter.Eq(x => x.SessionId, sessionId));

            var document = await _collection
                .Find(filter)
                .Sort(Builders<AttributionContextDocument>.Sort.Descending(x => x.CapturedAtUtc))
                .FirstOrDefaultAsync(cancellationToken);

            if (document != null)
            {
                var context = MapToContext(document);

                // Cache for future lookups
                var contextJson = JsonSerializer.Serialize(context, JsonOptions);
                await db.StringSetAsync(key, contextJson, ContextTtl);

                return context;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Attribution] Failed to get last touch context | TenantId: {TenantId} | SessionId: {SessionId}",
                tenantId, sessionId);
            return null;
        }
    }

    public async Task<List<AttributionContext>> GetAllContextsForSessionAsync(
        Guid tenantId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<AttributionContextDocument>.Filter.And(
                Builders<AttributionContextDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<AttributionContextDocument>.Filter.Eq(x => x.SessionId, sessionId));

            var documents = await _collection
                .Find(filter)
                .Sort(Builders<AttributionContextDocument>.Sort.Ascending(x => x.CapturedAtUtc))
                .ToListAsync(cancellationToken);

            return documents.Select(MapToContext).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Attribution] Failed to get all contexts | TenantId: {TenantId} | SessionId: {SessionId}",
                tenantId, sessionId);
            throw;
        }
    }

    #region Key Building

    private string BuildKey(Guid tenantId, string sessionId, string type)
    {
        return $"{_instanceName}:attribution:tenant:{tenantId:N}:session:{sessionId}:{type}";
    }

    #endregion

    #region Mapping

    private static AttributionContextDocument MapToDocument(AttributionContext context)
    {
        return new AttributionContextDocument
        {
            TenantId = context.TenantId,
            SessionId = context.SessionId,
            Source = context.Source,
            Medium = context.Medium,
            Campaign = context.Campaign,
            Content = context.Content,
            Term = context.Term,
            Referrer = context.Referrer,
            LandingPageUrl = context.LandingPageUrl,
            CapturedAtUtc = context.CapturedAtUtc,
            CustomerId = context.CustomerId,
            StoredAtUtc = DateTime.UtcNow
        };
    }

    private static AttributionContext MapToContext(AttributionContextDocument document)
    {
        return new AttributionContext
        {
            TenantId = document.TenantId,
            SessionId = document.SessionId,
            Source = document.Source,
            Medium = document.Medium,
            Campaign = document.Campaign,
            Content = document.Content,
            Term = document.Term,
            Referrer = document.Referrer,
            LandingPageUrl = document.LandingPageUrl,
            CapturedAtUtc = document.CapturedAtUtc,
            CustomerId = document.CustomerId
        };
    }

    #endregion
}
