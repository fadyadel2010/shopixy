using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using StackExchange.Redis;

namespace PointOfSale.API.Services.Platform;

/// <summary>
/// Idempotency service for registration requests.
/// Phase 8.3: Tenant Registration Hardening
/// 
/// Prevents duplicate tenant creation from:
/// - Double-click submit
/// - Network retries
/// - Browser refresh during submit
/// 
/// Uses X-Request-Id header to track requests.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Checks if a request ID has already been processed.
    /// If processed, returns the cached response.
    /// </summary>
    Task<IdempotencyResult> CheckRequestAsync(string requestId);
    
    /// <summary>
    /// Stores a successful registration result for idempotency.
    /// </summary>
    Task StoreResultAsync(string requestId, object result);
    
    /// <summary>
    /// Acquires a lock for processing a request.
    /// Returns false if another request with same ID is in progress.
    /// </summary>
    Task<bool> TryAcquireLockAsync(string requestId);
    
    /// <summary>
    /// Releases the processing lock.
    /// </summary>
    Task ReleaseLockAsync(string requestId);
}

public class IdempotencyResult
{
    /// <summary>
    /// Whether a cached result exists
    /// </summary>
    public bool HasCachedResult { get; set; }
    
    /// <summary>
    /// The cached result JSON (if exists)
    /// </summary>
    public string? CachedResultJson { get; set; }
    
    /// <summary>
    /// Whether another request with same ID is currently being processed
    /// </summary>
    public bool IsBeingProcessed { get; set; }
}

public class IdempotencyService : IIdempotencyService
{
    private readonly RedisConnectionFactory _redis;
    private readonly PlatformSecuritySettings _settings;
    private readonly ILogger<IdempotencyService> _logger;
    
    private const string KeyPrefix = "idempotency";
    private const string LockPrefix = "idempotency:lock";
    
    public IdempotencyService(
        RedisConnectionFactory redis,
        IOptions<PlatformSecuritySettings> settings,
        ILogger<IdempotencyService> logger)
    {
        _redis = redis;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IdempotencyResult> CheckRequestAsync(string requestId)
    {
        if (!_settings.IdempotencyEnabled || string.IsNullOrWhiteSpace(requestId))
        {
            return new IdempotencyResult { HasCachedResult = false };
        }
        
        var db = _redis.GetDatabase();
        if (db == null)
        {
            // Graceful degradation
            return new IdempotencyResult { HasCachedResult = false };
        }
        
        try
        {
            var resultKey = GetResultKey(requestId);
            var lockKey = GetLockKey(requestId);
            
            // Check for cached result first
            var cachedResult = await db.StringGetAsync(resultKey);
            if (cachedResult.HasValue)
            {
                _logger.LogInformation(
                    "Idempotency hit - returning cached result | RequestId: {RequestId}",
                    MaskRequestId(requestId));
                
                return new IdempotencyResult
                {
                    HasCachedResult = true,
                    CachedResultJson = cachedResult.ToString()
                };
            }
            
            // Check if currently being processed
            var isLocked = await db.KeyExistsAsync(lockKey);
            if (isLocked)
            {
                _logger.LogWarning(
                    "Request already in progress | RequestId: {RequestId}",
                    MaskRequestId(requestId));
                
                return new IdempotencyResult
                {
                    HasCachedResult = false,
                    IsBeingProcessed = true
                };
            }
            
            return new IdempotencyResult { HasCachedResult = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Idempotency check failed for request {RequestId}", MaskRequestId(requestId));
            return new IdempotencyResult { HasCachedResult = false };
        }
    }

    public async Task StoreResultAsync(string requestId, object result)
    {
        if (!_settings.IdempotencyEnabled || string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }
        
        var db = _redis.GetDatabase();
        if (db == null) return;
        
        try
        {
            var resultKey = GetResultKey(requestId);
            var ttl = TimeSpan.FromMinutes(_settings.IdempotencyKeyTtlMinutes);
            
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            await db.StringSetAsync(resultKey, json, ttl);
            
            _logger.LogDebug(
                "Stored idempotency result | RequestId: {RequestId} | TTL: {TTL}",
                MaskRequestId(requestId), ttl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store idempotency result for {RequestId}", MaskRequestId(requestId));
        }
    }

    public async Task<bool> TryAcquireLockAsync(string requestId)
    {
        if (!_settings.IdempotencyEnabled || string.IsNullOrWhiteSpace(requestId))
        {
            return true; // Allow processing if disabled
        }
        
        var db = _redis.GetDatabase();
        if (db == null) return true;
        
        try
        {
            var lockKey = GetLockKey(requestId);
            // Lock for 30 seconds - enough for registration to complete
            var acquired = await db.StringSetAsync(lockKey, "1", TimeSpan.FromSeconds(30), When.NotExists);
            
            if (!acquired)
            {
                _logger.LogWarning(
                    "Failed to acquire lock - request in progress | RequestId: {RequestId}",
                    MaskRequestId(requestId));
            }
            
            return acquired;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire lock for {RequestId}", MaskRequestId(requestId));
            return true; // Allow on error
        }
    }

    public async Task ReleaseLockAsync(string requestId)
    {
        if (!_settings.IdempotencyEnabled || string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }
        
        var db = _redis.GetDatabase();
        if (db == null) return;
        
        try
        {
            var lockKey = GetLockKey(requestId);
            await db.KeyDeleteAsync(lockKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release lock for {RequestId}", MaskRequestId(requestId));
        }
    }

    private static string GetResultKey(string requestId)
    {
        return $"{KeyPrefix}:result:{HashRequestId(requestId)}";
    }

    private static string GetLockKey(string requestId)
    {
        return $"{LockPrefix}:{HashRequestId(requestId)}";
    }

    private static string HashRequestId(string requestId)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(requestId);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..32];
    }

    private static string MaskRequestId(string requestId)
    {
        if (requestId.Length <= 8) return "***";
        return $"{requestId[..4]}...{requestId[^4..]}";
    }
}
