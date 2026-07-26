using System.Security.Cryptography;
using System.Text;
using PointOfSale.Infrastructure.Redis.Services;
using StackExchange.Redis;

namespace PointOfSale.API.Services.Analytics;

public interface IAnalyticsIngestionRateLimiter
{
    Task<AnalyticsIngestionRateLimitResult> CheckAsync(Guid tenantId, string clientIp, int eventCount);
}

public sealed record AnalyticsIngestionRateLimitResult(
    bool Allowed,
    string Reason,
    int RetryAfterSeconds)
{
    public static AnalyticsIngestionRateLimitResult Allow() => new(true, string.Empty, 0);

    public static AnalyticsIngestionRateLimitResult Deny(string reason, int retryAfterSeconds) =>
        new(false, reason, retryAfterSeconds);
}

public sealed class AnalyticsIngestionRateLimiter : IAnalyticsIngestionRateLimiter
{
    private const string IpKeyPrefix = "analytics:ingest:ip";
    private const string TenantKeyPrefix = "analytics:ingest:tenant";
    private const int MaxEventsPerIpPerMinute = 300;
    private const int MaxEventsPerTenantPerMinute = 2000;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly RedisConnectionFactory _redis;
    private readonly ILogger<AnalyticsIngestionRateLimiter> _logger;

    public AnalyticsIngestionRateLimiter(
        RedisConnectionFactory redis,
        ILogger<AnalyticsIngestionRateLimiter> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<AnalyticsIngestionRateLimitResult> CheckAsync(Guid tenantId, string clientIp, int eventCount)
    {
        if (tenantId == Guid.Empty)
        {
            return AnalyticsIngestionRateLimitResult.Deny("tenant_missing", (int)Window.TotalSeconds);
        }

        if (string.IsNullOrWhiteSpace(clientIp))
        {
            return AnalyticsIngestionRateLimitResult.Deny("ip_missing", (int)Window.TotalSeconds);
        }

        if (eventCount <= 0)
        {
            eventCount = 1;
        }

        try
        {
            var db = _redis.GetDatabase();
            if (db == null)
            {
                _logger.LogCritical(
                    "[SECURITY] Analytics ingestion rate limiter unavailable - request denied | TenantId: {TenantId} | IP: {IP}",
                    tenantId,
                    MaskIp(clientIp));

                return AnalyticsIngestionRateLimitResult.Deny("rate_limiter_unavailable", (int)Window.TotalSeconds);
            }

            var ipResult = await CheckPartitionAsync(
                db,
                $"{IpKeyPrefix}:{HashIp(clientIp)}",
                eventCount,
                MaxEventsPerIpPerMinute);

            if (!ipResult.Allowed)
            {
                _logger.LogWarning(
                    "[Analytics] Per-IP rate limit exceeded | TenantId: {TenantId} | IP: {IP} | EventCount: {EventCount} | RetryAfterSeconds: {RetryAfterSeconds}",
                    tenantId,
                    MaskIp(clientIp),
                    eventCount,
                    ipResult.RetryAfterSeconds);

                return AnalyticsIngestionRateLimitResult.Deny("ip_rate_limit_exceeded", ipResult.RetryAfterSeconds);
            }

            var tenantResult = await CheckPartitionAsync(
                db,
                $"{TenantKeyPrefix}:{tenantId:D}",
                eventCount,
                MaxEventsPerTenantPerMinute);

            if (!tenantResult.Allowed)
            {
                _logger.LogWarning(
                    "[Analytics] Per-tenant rate limit exceeded | TenantId: {TenantId} | EventCount: {EventCount} | RetryAfterSeconds: {RetryAfterSeconds}",
                    tenantId,
                    eventCount,
                    tenantResult.RetryAfterSeconds);

                return AnalyticsIngestionRateLimitResult.Deny("tenant_rate_limit_exceeded", tenantResult.RetryAfterSeconds);
            }

            return AnalyticsIngestionRateLimitResult.Allow();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "[SECURITY] Analytics ingestion rate limiter unavailable - request denied | TenantId: {TenantId} | IP: {IP}",
                tenantId,
                MaskIp(clientIp));

            return AnalyticsIngestionRateLimitResult.Deny("rate_limiter_unavailable", (int)Window.TotalSeconds);
        }
    }

    private static async Task<AnalyticsIngestionRateLimitResult> CheckPartitionAsync(
        IDatabase db,
        string key,
        int cost,
        int limit)
    {
        // Lua script for atomic increment + conditional expiry
        // 1. Increment the counter by cost
        // 2. If this is a new key (current == cost), set TTL
        // 3. Return current value and TTL
        const string luaScript = @"
            local current = redis.call('INCRBY', KEYS[1], ARGV[1])
            if current == tonumber(ARGV[1]) then
                redis.call('EXPIRE', KEYS[1], ARGV[2])
            end
            local ttl = redis.call('TTL', KEYS[1])
            return {current, ttl}
        ";

        var result = await db.ScriptEvaluateAsync(
            luaScript,
            new RedisKey[] { key },
            new RedisValue[]
            {
                cost,                           // ARGV[1]: increment amount
                (int)Window.TotalSeconds        // ARGV[2]: TTL in seconds
            });

        var resultArray = (RedisValue[])result!;
        var current = (long)resultArray[0];
        var ttl = (long)resultArray[1];

        if (current > limit)
        {
            return AnalyticsIngestionRateLimitResult.Deny(
                "rate_limit_exceeded",
                ttl > 0 ? Math.Max(1, (int)ttl) : (int)Window.TotalSeconds);
        }

        return AnalyticsIngestionRateLimitResult.Allow();
    }

    private static string HashIp(string value)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..24];
    }

    private static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return "[unknown]";
        }

        var lastDot = ip.LastIndexOf('.');
        if (lastDot > 0)
        {
            return ip[..(lastDot + 1)] + "***";
        }

        return ip[..Math.Min(ip.Length, 3)] + "***";
    }
}
