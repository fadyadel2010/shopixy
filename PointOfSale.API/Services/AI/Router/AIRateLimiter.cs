using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Services.AI.Models;
using PointOfSale.Infrastructure.Redis.Services;
using StackExchange.Redis;

namespace PointOfSale.API.Services.AI.Router;

/// <summary>Atomic fixed-window provider-attempt admission and completed-usage accounting.</summary>
public class AIRateLimiter(RedisConnectionFactory redis, IOptions<CommerceAIOptions> options, ILogger<AIRateLimiter> logger) : IAIRateLimiter
{
    private readonly CommerceAIOptions _options = options.Value;

    public async Task<RateLimitResult> CheckAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.RateLimitingEnabled) return RateLimitResult.CreateAllowed(0, int.MaxValue);
        var db = redis.GetDatabase();
        if (db == null) return RateLimitResult.CreateDenied(0, _options.RateLimitPerMinute, 60);
        try
        {
            // Preserve existing tenant counter keys. Reserve before execution, including failures.
            var keys = new[] { "minute", "hour", "day", "month" }.Select(window => (RedisKey)$"ai:ratelimit:{tenantId}:{window}").ToArray();
            var admission = (RedisResult[])(await db.ScriptEvaluateAsync("""
                for i=1,4 do
                    local count=tonumber(redis.call('GET',KEYS[i]) or '0')
                    if count>=tonumber(ARGV[i]) then return {0,count,tonumber(ARGV[i]),math.max(1,redis.call('TTL',KEYS[i]))} end
                end
                for i=1,4 do
                    local count=redis.call('INCR',KEYS[i])
                    if count==1 then redis.call('EXPIRE',KEYS[i],ARGV[i+4]) end
                end
                return {1,tonumber(redis.call('GET',KEYS[1])),tonumber(ARGV[1]),0}
                """, keys, new RedisValue[] { _options.RateLimitPerMinute, _options.RateLimitPerHour, _options.RateLimitPerDay, _options.RateLimitPerMonth, 60, 3600, 86400, 2592000 }))!;
            return (int)admission[0] == 1
                ? RateLimitResult.CreateAllowed((int)admission[1], (int)admission[2])
                : RateLimitResult.CreateDenied((int)admission[1], (int)admission[2], (int)admission[3]);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI admission unavailable, rejecting request | TenantId: {TenantId}", tenantId);
            return RateLimitResult.CreateDenied(0, _options.RateLimitPerMinute, 60);
        }
    }

    public async Task RecordRequestAsync(Guid tenantId, int tokensUsed, string modelId, CancellationToken cancellationToken = default)
    {
        var db = redis.GetDatabase();
        if (db == null) return;
        try
        {
            // Retain the existing JSON usage contract, but prevent concurrent read/modify/write loss.
            await db.ScriptEvaluateAsync("""
                local raw=redis.call('GET',KEYS[1])
                local u=raw and cjson.decode(raw) or {TotalRequests=0,TotalTokens=0,ModelUsage={}}
                u.TotalRequests=u.TotalRequests+1
                u.TotalTokens=u.TotalTokens+tonumber(ARGV[1])
                u.ModelUsage[ARGV[2]]=(u.ModelUsage[ARGV[2]] or 0)+1
                redis.call('SET',KEYS[1],cjson.encode(u),'EX',604800)
                return 1
                """, new RedisKey[] { $"ai:usage:{tenantId}:{DateTime.UtcNow:yyyy-MM-dd}" }, new RedisValue[] { Math.Max(0, tokensUsed), modelId });
        }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to record AI usage | TenantId: {TenantId}", tenantId); }
    }
}
