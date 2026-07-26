using Microsoft.Extensions.Logging;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Infrastructure.Services;

/// <summary>
/// AF-27A/28: Redis-backed sliding window rate limiter for funnel endpoints.
///
/// Provides horizontal-scaling-safe rate limiting using Redis sorted sets
/// (ZRANGEBYSCORE pattern). Falls back to allowing requests if Redis is unavailable
/// (fail-open — funnel must never hard-block due to infrastructure).
///
/// KEY FORMAT: funnel:ratelimit:{clientIp}
/// VALUE: Sorted set of Unix timestamps (score = timestamp, member = unique request ID)
/// TTL: Auto-expires 2× window (cleanup safety margin)
///
/// THREAD SAFETY: Redis is inherently thread-safe via StackExchange.Redis multiplexer.
/// SCALE: Works across all instances pointing at same Redis cluster.
/// </summary>
public interface IFunnelRateLimiter
{
    /// <summary>
    /// Checks if the given client IP can make another funnel request.
    /// Returns true if under limit, false if rate limit exceeded.
    /// </summary>
    Task<bool> CheckRateLimitAsync(string clientIp);
}

public class RedisFunnelRateLimiter : IFunnelRateLimiter
{
    private readonly RedisConnectionFactory _redisFactory;
    private readonly ILogger<RedisFunnelRateLimiter> _logger;

    private const int MaxCallsPerWindow = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const string KeyPrefix = "funnel:ratelimit:";

    public RedisFunnelRateLimiter(
        RedisConnectionFactory redisFactory,
        ILogger<RedisFunnelRateLimiter> logger)
    {
        _redisFactory = redisFactory;
        _logger = logger;
    }

    public async Task<bool> CheckRateLimitAsync(string clientIp)
    {
        var db = _redisFactory.GetDatabase();
        if (db == null)
        {
            // Redis unavailable — fail open (allow request)
            _logger.LogWarning(
                "[AF-27A:RateLimiter] Redis unavailable — allowing request (fail-open) | IP: {ClientIp}", clientIp);
            return true;
        }

        try
        {
            var key = $"{KeyPrefix}{clientIp}";
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var windowStart = now - (long)Window.TotalMilliseconds;

            // Lua script for atomic sliding window check + cleanup
            // 1. Remove expired entries
            // 2. Count remaining entries
            // 3. If under limit, add new entry
            // 4. Set TTL for auto-cleanup
            const string luaScript = @"
                redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])
                local count = redis.call('ZCARD', KEYS[1])
                if count < tonumber(ARGV[2]) then
                    redis.call('ZADD', KEYS[1], ARGV[3], ARGV[4])
                    redis.call('PEXPIRE', KEYS[1], ARGV[5])
                    return 1
                end
                return 0
            ";

            var result = await db.ScriptEvaluateAsync(
                luaScript,
                new StackExchange.Redis.RedisKey[] { key },
                new StackExchange.Redis.RedisValue[]
                {
                    windowStart,                          // ARGV[1]: cutoff timestamp
                    MaxCallsPerWindow,                    // ARGV[2]: max calls
                    now,                                  // ARGV[3]: current timestamp (score)
                    $"{now}:{Guid.NewGuid():N}",          // ARGV[4]: unique member
                    (long)(Window.TotalMilliseconds * 2)  // ARGV[5]: TTL (2× window)
                });

            return (int)result == 1;
        }
        catch (Exception ex)
        {
            // Redis error — fail open
            _logger.LogWarning(ex,
                "[AF-27A:RateLimiter] Redis rate limit check failed — allowing request (fail-open) | IP: {ClientIp}",
                clientIp);
            return true;
        }
    }
}
