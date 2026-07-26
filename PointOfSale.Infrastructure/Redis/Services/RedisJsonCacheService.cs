using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Models;

namespace PointOfSale.Infrastructure.Redis.Services;

public interface IRedisJsonCacheService
{
    Task<T?> GetAsync<T>(
        string key,
        string cacheType,
        TimeSpan? maxProjectionLag = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task SetAsync<T>(
        string key,
        string cacheType,
        T value,
        TimeSpan ttl,
        DateTime? projectionLastUpdatedAtUtc = null,
        DateTime? sourceEventOccurredAtUtc = null,
        int schemaVersion = 1,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<T?> GetOrCreateAsync<T>(
        string key,
        string cacheType,
        TimeSpan ttl,
        Func<CancellationToken, Task<CachePopulationResult<T>>> factory,
        TimeSpan? maxProjectionLag = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);
}

public sealed class RedisJsonCacheService : IRedisJsonCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly RedisConnectionFactory _connectionFactory;
    private readonly IDistributedLockService _lockService;
    private readonly RedisSettings _settings;
    private readonly RedisPerformanceTelemetry _telemetry;
    private readonly ILogger<RedisJsonCacheService> _logger;

    public RedisJsonCacheService(
        RedisConnectionFactory connectionFactory,
        IDistributedLockService lockService,
        IOptions<RedisSettings> settings,
        RedisPerformanceTelemetry telemetry,
        ILogger<RedisJsonCacheService> logger)
    {
        _connectionFactory = connectionFactory;
        _lockService = lockService;
        _settings = settings.Value;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(
        string key,
        string cacheType,
        TimeSpan? maxProjectionLag = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            _telemetry.RecordMiss(cacheType);
            return null;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var cachedValue = await database.StringGetAsync(key);
            sw.Stop();
            _telemetry.RecordReadLatency(cacheType, sw.Elapsed);

            if (cachedValue.IsNullOrEmpty)
            {
                _telemetry.RecordMiss(cacheType);
                return null;
            }

            var envelope = JsonSerializer.Deserialize<CacheEnvelope<T>>(cachedValue!, JsonOptions);
            if (envelope?.Payload == null)
            {
                _telemetry.RecordCorruption(cacheType);
                _telemetry.RecordMiss(cacheType);
                await database.KeyDeleteAsync(key);
                return null;
            }

            if (ShouldBypassForProjectionLag(key, cacheType, envelope.ProjectionLastUpdatedAtUtc, envelope.SourceEventOccurredAtUtc, maxProjectionLag))
            {
                _telemetry.RecordMiss(cacheType);
                await database.KeyDeleteAsync(key);
                return null;
            }

            _telemetry.RecordHit(cacheType);
            return envelope.Payload;
        }
        catch (JsonException ex)
        {
            sw.Stop();
            _telemetry.RecordCorruption(cacheType);
            _telemetry.RecordMiss(cacheType);
            _logger.LogWarning(ex, "Redis cache payload was corrupt | Key: {Key} | CacheType: {CacheType}", key, cacheType);

            try
            {
                await database.KeyDeleteAsync(key);
            }
            catch (Exception deleteEx)
            {
                _logger.LogDebug(deleteEx, "Failed to delete corrupt cache entry | Key: {Key}", key);
            }

            return null;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _telemetry.RecordMiss(cacheType);
            _logger.LogWarning(ex, "Redis cache read failed | Key: {Key} | CacheType: {CacheType}", key, cacheType);
            return null;
        }
    }

    public async Task SetAsync<T>(
        string key,
        string cacheType,
        T value,
        TimeSpan ttl,
        DateTime? projectionLastUpdatedAtUtc = null,
        DateTime? sourceEventOccurredAtUtc = null,
        int schemaVersion = 1,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);

        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            return;
        }

        try
        {
            var envelope = new CacheEnvelope<T>
            {
                Payload = value,
                CachedAtUtc = DateTime.UtcNow,
                ProjectionLastUpdatedAtUtc = projectionLastUpdatedAtUtc,
                SourceEventOccurredAtUtc = sourceEventOccurredAtUtc,
                SchemaVersion = schemaVersion
            };

            if (sourceEventOccurredAtUtc.HasValue && projectionLastUpdatedAtUtc.HasValue)
            {
                _telemetry.RecordProjectionLag(
                    cacheType,
                    projectionLastUpdatedAtUtc.Value - sourceEventOccurredAtUtc.Value);
            }

            var payload = JsonSerializer.Serialize(envelope, JsonOptions);

            var sw = Stopwatch.StartNew();
            await database.StringSetAsync(key, payload, ttl);
            sw.Stop();
            _telemetry.RecordWriteLatency(cacheType, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache write failed | Key: {Key} | CacheType: {CacheType}", key, cacheType);
        }
    }

    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        string cacheType,
        TimeSpan ttl,
        Func<CancellationToken, Task<CachePopulationResult<T>>> factory,
        TimeSpan? maxProjectionLag = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var cached = await GetAsync<T>(key, cacheType, maxProjectionLag, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        var lockKey = CacheKeyBuilder.CacheRefreshLockKey(_settings.InstanceName, HashKey(key));
        var lockToken = Guid.NewGuid().ToString("N");
        var lockTtl = TimeSpan.FromSeconds(Math.Max(3, _settings.CacheRefreshLockTtlSeconds));
        var lockAcquired = false;

        try
        {
            var acquireResult = await _lockService.TryAcquireLockAsync(lockKey, lockToken, lockTtl);
            lockAcquired = acquireResult.IsAcquired;

            if (acquireResult.IsInfrastructureFailure)
            {
                _logger.LogWarning(
                    "Redis cache refresh lock unavailable | Key: {Key} | CacheType: {CacheType}",
                    key,
                    cacheType);
            }
            else if (!lockAcquired)
            {
                await Task.Delay(Math.Max(25, _settings.CacheRefreshWaitMilliseconds), cancellationToken);

                cached = await GetAsync<T>(key, cacheType, maxProjectionLag, cancellationToken);
                if (cached != null)
                {
                    return cached;
                }
            }

            var stopwatch = Stopwatch.StartNew();
            var population = await factory(cancellationToken);
            stopwatch.Stop();

            _telemetry.RecordRefresh(cacheType, stopwatch.Elapsed);

            if (population.Value == null)
            {
                return null;
            }

            var shouldBypassCache = population.SkipCache || ShouldBypassForProjectionLag(
                key,
                cacheType,
                population.ProjectionLastUpdatedAtUtc,
                population.SourceEventOccurredAtUtc,
                maxProjectionLag);

            if (!shouldBypassCache)
            {
                await SetAsync(
                    key,
                    cacheType,
                    population.Value,
                    ttl,
                    population.ProjectionLastUpdatedAtUtc,
                    population.SourceEventOccurredAtUtc,
                    population.SchemaVersion,
                    cancellationToken);
            }

            return population.Value;
        }
        finally
        {
            if (lockAcquired)
            {
                await _lockService.ReleaseLockAsync(lockKey, lockToken);
            }
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            return;
        }

        try
        {
            await database.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache invalidation failed | Key: {Key}", key);
        }
    }

    public async Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var materializedKeys = keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => (StackExchange.Redis.RedisKey)key)
            .ToArray();

        if (materializedKeys.Length == 0)
        {
            return;
        }

        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            return;
        }

        try
        {
            await database.KeyDeleteAsync(materializedKeys);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache bulk invalidation failed | KeyCount: {KeyCount}", materializedKeys.Length);
        }
    }

    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var database = _connectionFactory.GetDatabase();
        var server = _connectionFactory.GetServer();
        if (database == null || server == null)
        {
            return;
        }

        try
        {
            var keys = new List<StackExchange.Redis.RedisKey>();
            await foreach (var key in server.KeysAsync(database.Database, pattern))
            {
                keys.Add(key);
            }

            if (keys.Count > 0)
            {
                await database.KeyDeleteAsync(keys.ToArray());
            }

            _telemetry.RecordPatternInvalidation(pattern);

            _logger.LogDebug(
                "Redis pattern invalidation completed | Pattern: {Pattern} | KeysMatched: {KeysMatched}",
                pattern,
                keys.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache pattern invalidation failed | Pattern: {Pattern}", pattern);
        }
    }

    private bool ShouldBypassForProjectionLag(
        string key,
        string cacheType,
        DateTime? projectionLastUpdatedAtUtc,
        DateTime? sourceEventOccurredAtUtc,
        TimeSpan? maxProjectionLag)
    {
        if (!maxProjectionLag.HasValue || !projectionLastUpdatedAtUtc.HasValue || !sourceEventOccurredAtUtc.HasValue)
        {
            return false;
        }

        var lag = projectionLastUpdatedAtUtc.Value - sourceEventOccurredAtUtc.Value;
        _telemetry.RecordProjectionLag(cacheType, lag);

        if (lag <= maxProjectionLag.Value)
        {
            return false;
        }

        _logger.LogWarning(
            "Bypassing cache because projection lag exceeded threshold | Key: {Key} | CacheType: {CacheType} | ProjectionUpdatedAtUtc: {ProjectionUpdatedAtUtc} | SourceEventOccurredAtUtc: {SourceEventOccurredAtUtc} | MaxProjectionLagSeconds: {MaxProjectionLagSeconds}",
            key,
            cacheType,
            projectionLastUpdatedAtUtc,
            sourceEventOccurredAtUtc,
            maxProjectionLag.Value.TotalSeconds);

        return true;
    }

    private static string HashKey(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }
}
