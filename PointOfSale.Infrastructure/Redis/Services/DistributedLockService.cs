using StackExchange.Redis;
using Microsoft.Extensions.Logging;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// Distributed Lock Service using Redis for concurrent operation protection.
/// Phase: Storefront Admin Hardening - P3
/// 
/// Uses Redis SET NX (set if not exists) with TTL for safe distributed locking.
/// </summary>
public enum DistributedLockResultStatus
{
    Acquired = 0,
    AlreadyHeld = 1,
    InfrastructureFailure = 2
}

public readonly record struct DistributedLockResult(DistributedLockResultStatus Status)
{
    public bool IsAcquired => Status == DistributedLockResultStatus.Acquired;

    public bool IsAlreadyHeld => Status == DistributedLockResultStatus.AlreadyHeld;

    public bool IsInfrastructureFailure => Status == DistributedLockResultStatus.InfrastructureFailure;

    public static DistributedLockResult Acquired() => new(DistributedLockResultStatus.Acquired);

    public static DistributedLockResult AlreadyHeld() => new(DistributedLockResultStatus.AlreadyHeld);

    public static DistributedLockResult InfrastructureFailure() => new(DistributedLockResultStatus.InfrastructureFailure);
}

public interface IDistributedLockService
{
    /// <summary>
    /// Attempts to acquire a distributed lock.
    /// </summary>
    /// <param name="lockKey">The unique key for the lock</param>
    /// <param name="lockValue">A unique value to identify this lock holder (typically a GUID)</param>
    /// <param name="ttl">Time-to-live for the lock (auto-release)</param>
    /// <returns>
    /// Structured acquisition result distinguishing normal contention from infrastructure failure.
    /// </returns>
    Task<DistributedLockResult> TryAcquireLockAsync(string lockKey, string lockValue, TimeSpan ttl);

    /// <summary>
    /// Refreshes a previously acquired lock if the caller still owns it.
    /// </summary>
    /// <param name="lockKey">The unique key for the lock</param>
    /// <param name="lockValue">The value used when acquiring the lock</param>
    /// <param name="ttl">The new time-to-live for the lock</param>
    /// <returns>
    /// Structured refresh result distinguishing normal ownership loss from infrastructure failure.
    /// </returns>
    Task<DistributedLockResult> RefreshLockAsync(string lockKey, string lockValue, TimeSpan ttl);

    /// <summary>
    /// Releases a previously acquired lock.
    /// Only releases if the lock value matches (prevents releasing someone else's lock).
    /// </summary>
    /// <param name="lockKey">The unique key for the lock</param>
    /// <param name="lockValue">The value used when acquiring the lock</param>
    /// <returns>True if lock was released, false if not held or value mismatch</returns>
    Task<bool> ReleaseLockAsync(string lockKey, string lockValue);

    /// <summary>
    /// Checks if a lock is currently held.
    /// </summary>
    /// <param name="lockKey">The unique key for the lock</param>
    /// <returns>True if the lock is currently held</returns>
    Task<bool> IsLockedAsync(string lockKey);
}

/// <summary>
/// Redis-based implementation of distributed locking.
/// </summary>
public class DistributedLockService : IDistributedLockService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<DistributedLockService> _logger;

    // Lua script for atomic release - only release if we own the lock
    private const string ReleaseLockScript = @"
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end
    ";

    // Lua script for atomic TTL refresh - only extend if we still own the lock
    private const string RefreshLockScript = @"
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('pexpire', KEYS[1], ARGV[2])
        else
            return 0
        end
    ";

    public DistributedLockService(
        IConnectionMultiplexer redis,
        ILogger<DistributedLockService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<DistributedLockResult> TryAcquireLockAsync(string lockKey, string lockValue, TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be greater than zero");
        }

        try
        {
            var db = _redis.GetDatabase();
            
            // SET NX with TTL - atomic operation
            var acquired = await db.StringSetAsync(
                lockKey, 
                lockValue, 
                ttl, 
                When.NotExists,
                CommandFlags.None);

            if (acquired)
            {
                _logger.LogDebug("Lock acquired | Key: {LockKey} | TTL: {TTL}s", lockKey, ttl.TotalSeconds);
                return DistributedLockResult.Acquired();
            }

            _logger.LogDebug("Lock already held | Key: {LockKey}", lockKey);
            return DistributedLockResult.AlreadyHeld();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire lock | Key: {LockKey}", lockKey);
            return DistributedLockResult.InfrastructureFailure();
        }
    }

    public async Task<DistributedLockResult> RefreshLockAsync(string lockKey, string lockValue, TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be greater than zero");
        }

        try
        {
            var db = _redis.GetDatabase();
            var result = await db.ScriptEvaluateAsync(
                RefreshLockScript,
                new RedisKey[] { lockKey },
                new RedisValue[] { lockValue, (long)Math.Ceiling(ttl.TotalMilliseconds) });

            var refreshed = (int)result == 1;

            if (refreshed)
            {
                _logger.LogDebug(
                    "Lock refreshed | Key: {LockKey} | TTL: {TTL}s",
                    lockKey,
                    ttl.TotalSeconds);
                return DistributedLockResult.Acquired();
            }

            _logger.LogWarning(
                "Lock refresh failed (not owner or expired) | Key: {LockKey}",
                lockKey);
            return DistributedLockResult.AlreadyHeld();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh lock | Key: {LockKey}", lockKey);
            return DistributedLockResult.InfrastructureFailure();
        }
    }

    public async Task<bool> ReleaseLockAsync(string lockKey, string lockValue)
    {
        try
        {
            var db = _redis.GetDatabase();
            
            // Use Lua script for atomic check-and-delete
            var result = await db.ScriptEvaluateAsync(
                ReleaseLockScript,
                new RedisKey[] { lockKey },
                new RedisValue[] { lockValue });

            var released = (int)result == 1;

            if (released)
            {
                _logger.LogDebug("Lock released | Key: {LockKey}", lockKey);
            }
            else
            {
                _logger.LogWarning("Lock release failed (not owner or expired) | Key: {LockKey}", lockKey);
            }

            return released;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release lock | Key: {LockKey}", lockKey);
            return false;
        }
    }

    public async Task<bool> IsLockedAsync(string lockKey)
    {
        try
        {
            var db = _redis.GetDatabase();
            return await db.KeyExistsAsync(lockKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check lock status | Key: {LockKey}", lockKey);
            return false;
        }
    }
}
