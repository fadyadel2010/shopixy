using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Analytics;
using PointOfSale.Infrastructure.Redis.Configuration;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// Redis implementation for analytics hot storage.
/// 
/// KEY PATTERNS:
/// - Event: analytics:tenant:{tenantId}:event:{eventId}
/// - Idempotency: analytics:tenant:{tenantId}:processed:{eventId}
/// - Counter: analytics:tenant:{tenantId}:counter:{name}:{window}
/// - Recent List: analytics:tenant:{tenantId}:recent:{eventType}
/// 
/// TTL STRATEGY:
/// - Events: 24 hours (for recent event queries)
/// - Idempotency: 7 days (for deduplication window)
/// - Counters: 25 hours (for hourly rollups)
/// - Recent List: 1 hour (for live dashboards)
/// </summary>
public class AnalyticsHotStoreService : IAnalyticsHotStoreService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<AnalyticsHotStoreService> _logger;
    private readonly string _instanceName;

    private static readonly TimeSpan DefaultEventTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan CounterTtl = TimeSpan.FromHours(25);
    private static readonly TimeSpan RecentListTtl = TimeSpan.FromHours(1);
    private const int MaxRecentEvents = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public AnalyticsHotStoreService(
        IConnectionMultiplexer redis,
        IOptions<RedisSettings> settings,
        ILogger<AnalyticsHotStoreService> logger)
    {
        _redis = redis;
        _instanceName = settings.Value.InstanceName;
        _logger = logger;
    }

    public async Task<bool> StoreEventAsync(
        AnalyticsEvent analyticsEvent,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var eventKey = BuildEventKey(analyticsEvent.TenantId, analyticsEvent.EventId);
            var eventJson = JsonSerializer.Serialize(analyticsEvent, JsonOptions);
            var expiry = ttl ?? DefaultEventTtl;

            // Store the event
            var stored = await db.StringSetAsync(eventKey, eventJson, expiry);

            if (stored)
            {
                // Add to recent events list (sorted by time)
                var recentKey = BuildRecentKey(analyticsEvent.TenantId, analyticsEvent.Type);
                var score = analyticsEvent.OccurredAtUtc.Ticks;
                
                await db.SortedSetAddAsync(recentKey, eventJson, score);
                await db.KeyExpireAsync(recentKey, RecentListTtl);
                
                // Trim to max size (keep most recent)
                await db.SortedSetRemoveRangeByRankAsync(recentKey, 0, -MaxRecentEvents - 1);

                _logger.LogDebug(
                    "[Analytics] Event stored in Redis | TenantId: {TenantId} | EventId: {EventId} | Type: {Type}",
                    analyticsEvent.TenantId, analyticsEvent.EventId, analyticsEvent.Type);
            }

            return stored;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics] Failed to store event in Redis (non-critical) | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId, analyticsEvent.EventId);
            return false;
        }
    }

    public async Task<bool> IsEventProcessedAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = BuildIdempotencyKey(tenantId, eventId);
            return await db.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics] Failed to check idempotency in Redis | TenantId: {TenantId} | EventId: {EventId}",
                tenantId, eventId);
            // On failure, assume not processed to avoid data loss
            return false;
        }
    }

    public async Task MarkEventProcessedAsync(
        Guid tenantId,
        Guid eventId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = BuildIdempotencyKey(tenantId, eventId);
            await db.StringSetAsync(key, "1", ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics] Failed to mark event as processed in Redis | TenantId: {TenantId} | EventId: {EventId}",
                tenantId, eventId);
        }
    }

    public async Task<long> IncrementCounterAsync(
        Guid tenantId,
        string counterName,
        DateTime timeWindow,
        long increment = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var windowKey = timeWindow.ToString("yyyyMMddHH"); // Hourly buckets
            var key = BuildCounterKey(tenantId, counterName, windowKey);
            
            var newValue = await db.StringIncrementAsync(key, increment);
            await db.KeyExpireAsync(key, CounterTtl);
            
            return newValue;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics] Failed to increment counter in Redis | TenantId: {TenantId} | Counter: {Counter}",
                tenantId, counterName);
            return 0;
        }
    }

    public async Task<List<AnalyticsEvent>> GetRecentEventsAsync(
        Guid tenantId,
        AnalyticsEventType? eventType = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var events = new List<AnalyticsEvent>();
        
        try
        {
            var db = _redis.GetDatabase();
            
            if (eventType.HasValue)
            {
                // Get from specific type list
                var recentKey = BuildRecentKey(tenantId, eventType.Value);
                var entries = await db.SortedSetRangeByRankAsync(recentKey, -limit, -1, Order.Descending);
                
                foreach (var entry in entries)
                {
                    if (entry.HasValue)
                    {
                        var evt = JsonSerializer.Deserialize<AnalyticsEvent>(entry!, JsonOptions);
                        if (evt != null) events.Add(evt);
                    }
                }
            }
            else
            {
                // Get from all types (merge sorted sets)
                foreach (AnalyticsEventType type in Enum.GetValues<AnalyticsEventType>())
                {
                    var recentKey = BuildRecentKey(tenantId, type);
                    var entries = await db.SortedSetRangeByRankAsync(recentKey, -limit, -1, Order.Descending);
                    
                    foreach (var entry in entries)
                    {
                        if (entry.HasValue)
                        {
                            var evt = JsonSerializer.Deserialize<AnalyticsEvent>(entry!, JsonOptions);
                            if (evt != null) events.Add(evt);
                        }
                    }
                }
                
                // Sort by time and take limit
                events = events
                    .OrderByDescending(e => e.OccurredAtUtc)
                    .Take(limit)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics] Failed to get recent events from Redis | TenantId: {TenantId}",
                tenantId);
        }

        return events;
    }

    public async Task<long> IncrementPageViewCounterAsync(
        Guid tenantId,
        string pagePath,
        DateTime timeWindow,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var windowKey = timeWindow.ToString("yyyyMMddHH");
            var key = $"{_instanceName}:analytics:tenant:{tenantId:N}:pageviews:{windowKey}";
            
            var newValue = await db.SortedSetIncrementAsync(key, pagePath, 1);
            await db.KeyExpireAsync(key, CounterTtl);
            
            return (long)newValue;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics] Failed to increment page view counter | TenantId: {TenantId} | Path: {Path}",
                tenantId, pagePath);
            return 0;
        }
    }

    public async Task<long> IncrementProductViewCounterAsync(
        Guid tenantId,
        Guid productId,
        DateTime timeWindow,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var windowKey = timeWindow.ToString("yyyyMMddHH");
            var key = $"{_instanceName}:analytics:tenant:{tenantId:N}:productviews:{windowKey}";
            
            var newValue = await db.SortedSetIncrementAsync(key, productId.ToString(), 1);
            await db.KeyExpireAsync(key, CounterTtl);
            
            return (long)newValue;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics] Failed to increment product view counter | TenantId: {TenantId} | ProductId: {ProductId}",
                tenantId, productId);
            return 0;
        }
    }

    public async Task<long> IncrementAddToCartCounterAsync(
        Guid tenantId,
        Guid productId,
        DateTime timeWindow,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var windowKey = timeWindow.ToString("yyyyMMddHH");
            var key = $"{_instanceName}:analytics:tenant:{tenantId:N}:addtocart:{windowKey}";
            
            var newValue = await db.SortedSetIncrementAsync(key, productId.ToString(), 1);
            await db.KeyExpireAsync(key, CounterTtl);
            
            return (long)newValue;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Analytics] Failed to increment add-to-cart counter | TenantId: {TenantId} | ProductId: {ProductId}",
                tenantId, productId);
            return 0;
        }
    }

    public async Task<List<TopPageStats>> GetTopPagesAsync(
        Guid tenantId,
        int windowMinutes,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        // This is a simplification - ideally we'd aggregate multiple hourly buckets
        // For now, we'll just check the current hour and previous hours within the window
        var results = new Dictionary<string, long>();
        var db = _redis.GetDatabase();
        var now = DateTime.UtcNow;
        var start = now.AddMinutes(-windowMinutes);
        
        try
        {
            var current = now;
            while (current >= start)
            {
                var windowKey = current.ToString("yyyyMMddHH");
                var key = $"{_instanceName}:analytics:tenant:{tenantId:N}:pageviews:{windowKey}";
                
                // Get all entries for this hour (up to limit to avoid huge payload, but ideally we get more)
                var entries = await db.SortedSetRangeByRankWithScoresAsync(key, 0, -1, Order.Descending);
                
                foreach (var entry in entries)
                {
                    var path = entry.Element.ToString();
                    var count = (long)entry.Score;
                    
                    if (results.TryGetValue(path, out var currentCount))
                    {
                        results[path] = currentCount + count;
                    }
                    else
                    {
                        results[path] = count;
                    }
                }
                
                current = current.AddHours(-1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Analytics] Failed to get top pages");
            return new List<TopPageStats>();
        }

        var totalViews = results.Values.Sum();
        
        return results
            .OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv => new TopPageStats
            {
                PagePath = kv.Key,
                ViewCount = kv.Value,
                PercentageOfTotal = totalViews > 0 ? Math.Round((decimal)kv.Value / totalViews * 100, 2) : 0
            })
            .ToList();
    }

    public async Task<List<TopProductStats>> GetTopViewedProductsAsync(
        Guid tenantId,
        int windowMinutes,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        return await GetTopProductsGenericAsync(tenantId, "productviews", windowMinutes, limit, cancellationToken);
    }

    public async Task<List<TopProductStats>> GetTopAddedToCartProductsAsync(
        Guid tenantId,
        int windowMinutes,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        return await GetTopProductsGenericAsync(tenantId, "addtocart", windowMinutes, limit, cancellationToken);
    }

    private async Task<List<TopProductStats>> GetTopProductsGenericAsync(
        Guid tenantId,
        string counterType,
        int windowMinutes,
        int limit,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, long>();
        var db = _redis.GetDatabase();
        var now = DateTime.UtcNow;
        var start = now.AddMinutes(-windowMinutes);
        
        try
        {
            var current = now;
            while (current >= start)
            {
                var windowKey = current.ToString("yyyyMMddHH");
                var key = $"{_instanceName}:analytics:tenant:{tenantId:N}:{counterType}:{windowKey}";
                
                var entries = await db.SortedSetRangeByRankWithScoresAsync(key, 0, -1, Order.Descending);
                
                foreach (var entry in entries)
                {
                    var productId = entry.Element.ToString();
                    var count = (long)entry.Score;
                    
                    if (results.TryGetValue(productId, out var currentCount))
                    {
                        results[productId] = currentCount + count;
                    }
                    else
                    {
                        results[productId] = count;
                    }
                }
                
                current = current.AddHours(-1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"[Analytics] Failed to get top {counterType}");
            return new List<TopProductStats>();
        }

        // Ideally we would resolve product names here from a read model or service
        // For now we will return the ID and let the frontend or API layer resolve it
        
        return results
            .OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv => new TopProductStats
            {
                ProductId = Guid.TryParse(kv.Key, out var id) ? id : Guid.Empty,
                ProductName = "Loading...", // Placeholder - resolved in API/Frontend
                Count = kv.Value
            })
            .Where(p => p.ProductId != Guid.Empty)
            .ToList();
    }

    #region Key Builders

    private string BuildEventKey(Guid tenantId, Guid eventId)
    {
        return $"{_instanceName}:analytics:tenant:{tenantId:N}:event:{eventId:N}";
    }

    private string BuildIdempotencyKey(Guid tenantId, Guid eventId)
    {
        return $"{_instanceName}:analytics:tenant:{tenantId:N}:processed:{eventId:N}";
    }

    private string BuildCounterKey(Guid tenantId, string counterName, string window)
    {
        return $"{_instanceName}:analytics:tenant:{tenantId:N}:counter:{counterName}:{window}";
    }

    private string BuildRecentKey(Guid tenantId, AnalyticsEventType eventType)
    {
        return $"{_instanceName}:analytics:tenant:{tenantId:N}:recent:{eventType}";
    }

    #endregion
}
