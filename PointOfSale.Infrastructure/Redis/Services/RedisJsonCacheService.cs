using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Models;
using PointOfSale.Infrastructure.Tenancy;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Redis.Services;

public interface IRedisJsonCacheService
{
    Task<T?> GetAsync<T>(
        string key,
        string cacheType,
        TimeSpan? maxProjectionLag = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<T?> GetOwnedAsync<T>(
        string key,
        string cacheType,
        string invalidationGenerationKey,
        TimeSpan? maxProjectionLag = null,
        CancellationToken cancellationToken = default)
        where T : class => GetAsync<T>(key, cacheType, maxProjectionLag, cancellationToken);

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

    Task<T?> GetOrCreateOwnedAsync<T>(
        string key,
        string cacheType,
        string invalidationGenerationKey,
        TimeSpan ttl,
        Func<CancellationToken, Task<CachePopulationResult<T>>> factory,
        TimeSpan? maxProjectionLag = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<CacheReadResult<T>> GetOrCreateOwnedWithStatusAsync<T>(
        string key,
        string cacheType,
        string invalidationGenerationKey,
        TimeSpan ttl,
        Func<CancellationToken, Task<CachePopulationResult<T>>> factory,
        TimeSpan? maxProjectionLag = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task AdvanceInvalidationGenerationAsync(
        string invalidationGenerationKey,
        CancellationToken cancellationToken = default);

    Task<long?> GetInvalidationGenerationAsync(
        string invalidationGenerationKey,
        string cacheType,
        CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);
}

public sealed class RedisJsonCacheService : IRedisJsonCacheService
{
    private const string ReadOwnedScript = "local g = redis.call('get', KEYS[2]) or '0'; local v = redis.call('get', KEYS[1]); return {g, v or false}";
    private const string SetOwnedScript = "local g = redis.call('get', KEYS[2]) or '0'; if g ~= ARGV[1] then return 0 end; redis.call('psetex', KEYS[1], ARGV[2], ARGV[3]); return 1";
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
        return await GetCoreAsync<T>(
            key,
            cacheType,
            invalidationGenerationKey: null,
            maxProjectionLag,
            cancellationToken);
    }

    public Task<T?> GetOwnedAsync<T>(
        string key,
        string cacheType,
        string invalidationGenerationKey,
        TimeSpan? maxProjectionLag = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invalidationGenerationKey);
        return GetCoreAsync<T>(
            key,
            cacheType,
            invalidationGenerationKey,
            maxProjectionLag,
            cancellationToken);
    }

    private async Task<T?> GetCoreAsync<T>(
        string key,
        string cacheType,
        string? invalidationGenerationKey,
        TimeSpan? maxProjectionLag,
        CancellationToken cancellationToken)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            _telemetry.RecordMiss(cacheType);
            if (_connectionFactory.IsEnabled)
            {
                _telemetry.RecordFailure(cacheType);
            }
            return null;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            long generationAfterRead;
            RedisValue cachedValue;
            if (invalidationGenerationKey == null)
            {
                generationAfterRead = 0L;
                cachedValue = await database.StringGetAsync(key);
            }
            else
            {
                var result = (RedisResult[]?)await database.ScriptEvaluateAsync(
                    ReadOwnedScript,
                    new RedisKey[] { key, invalidationGenerationKey });
                generationAfterRead = result is { Length: > 0 } &&
                                      long.TryParse(result[0].ToString(), out var generation)
                    ? generation
                    : 0L;
                cachedValue = result is { Length: > 1 } && !result[1].IsNull
                    ? result[1].ToString()
                    : RedisValue.Null;
            }
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

            if (envelope.InvalidationGeneration != generationAfterRead)
            {
                _telemetry.RecordMiss(cacheType);
                return null;
            }

            if (ShouldBypassForProjectionLag(
                    key,
                    cacheType,
                    envelope.ProjectionLastUpdatedAtUtc,
                    envelope.SourceEventOccurredAtUtc,
                    envelope.ProjectionVersion,
                    envelope.SourceVersion,
                    maxProjectionLag))
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
            _telemetry.RecordFailure(cacheType, ex);
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
        await SetCoreAsync(
            key,
            cacheType,
            value,
            ttl,
            projectionLastUpdatedAtUtc,
            sourceEventOccurredAtUtc,
            projectionVersion: null,
            sourceVersion: null,
            schemaVersion,
            invalidationGenerationKey: null,
            expectedInvalidationGeneration: 0,
            cancellationToken);
    }

    private async Task SetCoreAsync<T>(
        string key,
        string cacheType,
        T value,
        TimeSpan ttl,
        DateTime? projectionLastUpdatedAtUtc,
        DateTime? sourceEventOccurredAtUtc,
        long? projectionVersion,
        long? sourceVersion,
        int schemaVersion,
        string? invalidationGenerationKey,
        long expectedInvalidationGeneration,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            if (_connectionFactory.IsEnabled)
            {
                _telemetry.RecordFailure(cacheType);
            }
            return;
        }

        try
        {
            _telemetry.RecordWriteOutcome(cacheType, "attempted");
            var envelope = new CacheEnvelope<T>
            {
                Payload = value,
                CachedAtUtc = DateTime.UtcNow,
                ProjectionLastUpdatedAtUtc = projectionLastUpdatedAtUtc,
                SourceEventOccurredAtUtc = sourceEventOccurredAtUtc,
                ProjectionVersion = projectionVersion,
                SourceVersion = sourceVersion,
                InvalidationGeneration = expectedInvalidationGeneration,
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
            if (invalidationGenerationKey == null)
            {
                var written = await database.StringSetAsync(key, payload, ttl);
                if (!written)
                {
                    _telemetry.RecordWriteOutcome(cacheType, "command_false");
                    _telemetry.RecordFailure(cacheType);
                    _logger.LogWarning(
                        "Redis cache write returned false | KeyHash: {KeyHash} | CacheType: {CacheType}",
                        HashKey(key),
                        cacheType);
                    return;
                }
            }
            else
            {
                var written = (int)await database.ScriptEvaluateAsync(
                    SetOwnedScript,
                    new RedisKey[] { key, invalidationGenerationKey },
                    new RedisValue[]
                    {
                        expectedInvalidationGeneration,
                        (long)Math.Ceiling(ttl.TotalMilliseconds),
                        payload
                    });
                _telemetry.RecordWriteOutcome(cacheType, "script_executed");
                if (written != 1)
                {
                    _telemetry.RecordWriteOutcome(cacheType, "fenced");
                    _logger.LogInformation(
                        "Skipped stale Redis cache population after invalidation advanced | KeyHash: {KeyHash} | CacheType: {CacheType}",
                        HashKey(key),
                        cacheType);
                    return;
                }
            }
            sw.Stop();
            _telemetry.RecordWriteOutcome(cacheType, "command_succeeded");
            _telemetry.RecordWriteLatency(cacheType, sw.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _telemetry.RecordWriteOutcome(cacheType, "cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _telemetry.RecordWriteOutcome(cacheType, "failed");
            _telemetry.RecordFailure(cacheType, ex);
            _logger.LogWarning(ex, "Redis cache write failed | KeyHash: {KeyHash} | CacheType: {CacheType}", HashKey(key), cacheType);
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
        return await GetOrCreateCoreAsync(
            key,
            cacheType,
            invalidationGenerationKey: null,
            ttl,
            factory,
            maxProjectionLag,
            cancellationToken);
    }

    public Task<T?> GetOrCreateOwnedAsync<T>(
        string key,
        string cacheType,
        string invalidationGenerationKey,
        TimeSpan ttl,
        Func<CancellationToken, Task<CachePopulationResult<T>>> factory,
        TimeSpan? maxProjectionLag = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invalidationGenerationKey);
        return GetOrCreateCoreAsync(
            key,
            cacheType,
            invalidationGenerationKey,
            ttl,
            factory,
            maxProjectionLag,
            cancellationToken);
    }

    public Task<CacheReadResult<T>> GetOrCreateOwnedWithStatusAsync<T>(
        string key,
        string cacheType,
        string invalidationGenerationKey,
        TimeSpan ttl,
        Func<CancellationToken, Task<CachePopulationResult<T>>> factory,
        TimeSpan? maxProjectionLag = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invalidationGenerationKey);
        return GetOrCreateCoreWithStatusAsync(
            key,
            cacheType,
            invalidationGenerationKey,
            ttl,
            factory,
            maxProjectionLag,
            cancellationToken);
    }

    private async Task<T?> GetOrCreateCoreAsync<T>(
        string key,
        string cacheType,
        string? invalidationGenerationKey,
        TimeSpan ttl,
        Func<CancellationToken, Task<CachePopulationResult<T>>> factory,
        TimeSpan? maxProjectionLag,
        CancellationToken cancellationToken)
        where T : class
    {
        var result = await GetOrCreateCoreWithStatusAsync(
            key,
            cacheType,
            invalidationGenerationKey,
            ttl,
            factory,
            maxProjectionLag,
            cancellationToken).ConfigureAwait(false);
        return result.Value;
    }

    private async Task<CacheReadResult<T>> GetOrCreateCoreWithStatusAsync<T>(
        string key,
        string cacheType,
        string? invalidationGenerationKey,
        TimeSpan ttl,
        Func<CancellationToken, Task<CachePopulationResult<T>>> factory,
        TimeSpan? maxProjectionLag,
        CancellationToken cancellationToken)
        where T : class
    {
        var cached = await GetCoreAsync<T>(
            key,
            cacheType,
            invalidationGenerationKey,
            maxProjectionLag,
            cancellationToken);
        if (cached != null)
        {
            return new CacheReadResult<T>(cached, Refreshed: false);
        }

        return await KeyedSingleFlight.GetOrAddAsync(
            $"redis-refresh:{key}",
            async ct =>
            {
                var rechecked = await GetCoreAsync<T>(
                    key,
                    cacheType,
                    invalidationGenerationKey,
                    maxProjectionLag,
                    ct).ConfigureAwait(false);
                return (rechecked != null, new CacheReadResult<T>(rechecked, Refreshed: false));
            },
            ct => PopulateCacheAsync(
                key,
                cacheType,
                invalidationGenerationKey,
                ttl,
                factory,
                maxProjectionLag,
                ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CacheReadResult<T>> PopulateCacheAsync<T>(
        string key,
        string cacheType,
        string? invalidationGenerationKey,
        TimeSpan ttl,
        Func<CancellationToken, Task<CachePopulationResult<T>>> factory,
        TimeSpan? maxProjectionLag,
        CancellationToken cancellationToken)
        where T : class
    {
        T? cached;
        var lockKey = CacheKeyBuilder.CacheRefreshLockKey(_settings.InstanceName, HashKey(key));
        var lockToken = Guid.NewGuid().ToString("N");
        var lockTtl = TimeSpan.FromSeconds(Math.Max(3, _settings.CacheRefreshLockTtlSeconds));
        var lockAcquired = false;

        try
        {
            while (!lockAcquired)
            {
                var acquireResult = await _lockService.TryAcquireLockAsync(lockKey, lockToken, lockTtl);
                lockAcquired = acquireResult.IsAcquired;
                if (lockAcquired)
                {
                    cached = await GetCoreAsync<T>(
                        key,
                        cacheType,
                        invalidationGenerationKey,
                        maxProjectionLag,
                        cancellationToken);
                    if (cached != null)
                    {
                        return new CacheReadResult<T>(cached, Refreshed: false);
                    }

                    break;
                }

                if (acquireResult.IsInfrastructureFailure)
                {
                    _telemetry.RecordFailure(cacheType);
                    _logger.LogWarning(
                        "Redis cache refresh lock unavailable | Key: {Key} | CacheType: {CacheType}",
                        key,
                        cacheType);
                    break;
                }

                await Task.Delay(Math.Max(25, _settings.CacheRefreshWaitMilliseconds), cancellationToken);

                cached = await GetCoreAsync<T>(
                    key,
                    cacheType,
                    invalidationGenerationKey,
                    maxProjectionLag,
                    cancellationToken);
                if (cached != null)
                {
                    return new CacheReadResult<T>(cached, Refreshed: false);
                }
            }

            var database = _connectionFactory.GetDatabase();
            var expectedInvalidationGeneration = invalidationGenerationKey == null || database == null
                ? 0L
                : await ReadInvalidationGenerationAsync(database, invalidationGenerationKey);

            var stopwatch = Stopwatch.StartNew();
            var population = await factory(cancellationToken);
            stopwatch.Stop();

            _telemetry.RecordRefresh(cacheType, stopwatch.Elapsed);

            if (population.Value == null)
            {
                return new CacheReadResult<T>(null, Refreshed: true);
            }

            var shouldBypassCache = population.SkipCache || ShouldBypassForProjectionLag(
                key,
                cacheType,
                population.ProjectionLastUpdatedAtUtc,
                population.SourceEventOccurredAtUtc,
                population.ProjectionVersion,
                population.SourceVersion,
                maxProjectionLag);

            if (!shouldBypassCache)
            {
                await SetCoreAsync(
                    key,
                    cacheType,
                    population.Value,
                    ttl,
                    population.ProjectionLastUpdatedAtUtc,
                    population.SourceEventOccurredAtUtc,
                    population.ProjectionVersion,
                    population.SourceVersion,
                    population.SchemaVersion,
                    invalidationGenerationKey,
                    expectedInvalidationGeneration,
                    cancellationToken);
            }

            return new CacheReadResult<T>(population.Value, Refreshed: true);
        }
        finally
        {
            if (lockAcquired)
            {
                await _lockService.ReleaseLockAsync(lockKey, lockToken);
            }
        }
    }

    public async Task AdvanceInvalidationGenerationAsync(
        string invalidationGenerationKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invalidationGenerationKey);
        cancellationToken.ThrowIfCancellationRequested();

        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            EnsureInvalidationCanBeSkippedOrThrow(invalidationGenerationKey);
            return;
        }

        try
        {
            await database.StringIncrementAsync(invalidationGenerationKey);
        }
        catch (Exception ex)
        {
            _telemetry.RecordFailure(
                invalidationGenerationKey.Contains(":authority:domain:", StringComparison.Ordinal)
                    ? "domain-resolution"
                    : "other",
                ex);
            _logger.LogError(
                ex,
                "Redis invalidation generation advance failed | GenerationKey: {GenerationKey}",
                invalidationGenerationKey);
            throw new RedisCacheInvalidationException(invalidationGenerationKey, ex);
        }
    }

    public async Task<long?> GetInvalidationGenerationAsync(
        string invalidationGenerationKey,
        string cacheType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invalidationGenerationKey);
        cancellationToken.ThrowIfCancellationRequested();

        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            if (_connectionFactory.IsEnabled)
            {
                _telemetry.RecordFailure(cacheType);
            }

            return null;
        }

        try
        {
            return await ReadInvalidationGenerationAsync(database, invalidationGenerationKey);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _telemetry.RecordFailure(cacheType, ex);
            _logger.LogWarning(
                ex,
                "Redis invalidation generation read failed | GenerationKey: {GenerationKey} | CacheType: {CacheType}",
                invalidationGenerationKey,
                cacheType);
            return null;
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            EnsureInvalidationCanBeSkippedOrThrow(key);
            return;
        }

        try
        {
            await database.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis cache invalidation failed | Key: {Key}", key);
            throw new RedisCacheInvalidationException(key, ex);
        }
    }

    public async Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            EnsureInvalidationCanBeSkippedOrThrow($"bulk:{materializedKeys.Length}");
            return;
        }

        try
        {
            await database.KeyDeleteAsync(materializedKeys);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis cache bulk invalidation failed | KeyCount: {KeyCount}", materializedKeys.Length);
            throw new RedisCacheInvalidationException($"bulk:{materializedKeys.Length}", ex);
        }
    }

    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = _connectionFactory.GetDatabase();
        var server = _connectionFactory.GetServer();
        if (database == null || server == null)
        {
            EnsureInvalidationCanBeSkippedOrThrow(pattern);
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
            _logger.LogError(ex, "Redis cache pattern invalidation failed | Pattern: {Pattern}", pattern);
            throw new RedisCacheInvalidationException(pattern, ex);
        }
    }

    private void EnsureInvalidationCanBeSkippedOrThrow(string keyOrPattern)
    {
        if (!_connectionFactory.IsEnabled)
        {
            return;
        }

        throw new RedisCacheInvalidationException(
            keyOrPattern,
            new InvalidOperationException("Redis is enabled but no connected invalidation endpoint is available."));
    }

    private bool ShouldBypassForProjectionLag(
        string key,
        string cacheType,
        DateTime? projectionLastUpdatedAtUtc,
        DateTime? sourceEventOccurredAtUtc,
        long? projectionVersion,
        long? sourceVersion,
        TimeSpan? maxProjectionLag)
    {
        var shouldBypass = CacheProjectionFreshnessGuard.ShouldBypass(
            projectionLastUpdatedAtUtc,
            sourceEventOccurredAtUtc,
            projectionVersion,
            sourceVersion,
            maxProjectionLag);

        if (!shouldBypass)
        {
            if (!projectionVersion.HasValue && !sourceVersion.HasValue &&
                maxProjectionLag.HasValue && projectionLastUpdatedAtUtc.HasValue && sourceEventOccurredAtUtc.HasValue)
            {
                _telemetry.RecordProjectionLag(cacheType, projectionLastUpdatedAtUtc.Value - sourceEventOccurredAtUtc.Value);
            }

            return false;
        }

        if (projectionVersion.HasValue || sourceVersion.HasValue)
        {
            if (!projectionVersion.HasValue || !sourceVersion.HasValue)
            {
                _logger.LogWarning(
                    "Bypassing cache because projection/source revision proof is incomplete | Key: {Key} | CacheType: {CacheType} | ProjectionVersion: {ProjectionVersion} | SourceVersion: {SourceVersion}",
                    key,
                    cacheType,
                    projectionVersion,
                    sourceVersion);
                return true;
            }

            _logger.LogWarning(
                "Bypassing cache because projection revision is behind source revision | Key: {Key} | CacheType: {CacheType} | ProjectionVersion: {ProjectionVersion} | SourceVersion: {SourceVersion}",
                key,
                cacheType,
                projectionVersion,
                sourceVersion);
            return true;
        }

        if (!maxProjectionLag.HasValue || !projectionLastUpdatedAtUtc.HasValue || !sourceEventOccurredAtUtc.HasValue)
        {
            return false;
        }

        var lag = projectionLastUpdatedAtUtc.Value - sourceEventOccurredAtUtc.Value;
        _telemetry.RecordProjectionLag(cacheType, lag);

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

    private static async Task<long> ReadInvalidationGenerationAsync(
        IDatabase database,
        string invalidationGenerationKey)
    {
        var value = await database.StringGetAsync(invalidationGenerationKey);
        return ParseInvalidationGeneration(value);
    }

    private static long ParseInvalidationGeneration(RedisValue value)
        => value.IsNullOrEmpty || !long.TryParse(value.ToString(), out var generation)
            ? 0L
            : generation;
}

public sealed class RedisCacheInvalidationException : Exception
{
    public RedisCacheInvalidationException(string keyOrPattern, Exception innerException)
        : base($"Required Redis invalidation failed for '{keyOrPattern}'.", innerException)
    {
    }
}
