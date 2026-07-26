using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Services.AI.Models;
using PointOfSale.Infrastructure.Redis.Services;
using StackExchange.Redis;

namespace PointOfSale.API.Services.AI.Router;

/// <summary>
/// Redis-based rate limiter for AI requests.
/// Uses sliding window algorithm for smooth rate limiting.
/// PHASE 6.5: Extended with daily/monthly cost ceiling protection.
/// </summary>
public class AIRateLimiter : IAIRateLimiter
{
    private readonly RedisConnectionFactory _redis;
    private readonly CommerceAIOptions _options;
    private readonly ILogger<AIRateLimiter> _logger;
    
    private const string RateLimitKeyPrefix = "ai:ratelimit:";
    private const string UsageKeyPrefix = "ai:usage:";
    
    public AIRateLimiter(
        RedisConnectionFactory redis,
        IOptions<CommerceAIOptions> options,
        ILogger<AIRateLimiter> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }
    
    /// <inheritdoc />
    public async Task<RateLimitResult> CheckAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (!_options.RateLimitingEnabled)
        {
            return RateLimitResult.CreateAllowed(0, int.MaxValue);
        }
        
        var db = _redis.GetDatabase();
        if (db == null)
        {
            // Redis unavailable - fail open
            return RateLimitResult.CreateAllowed(0, _options.RateLimitPerMinute);
        }
        
        try
        {
            // PHASE 6.5: Check minute, hour, day, and month limits
            var minuteKey = $"{RateLimitKeyPrefix}{tenantId}:minute";
            var hourKey = $"{RateLimitKeyPrefix}{tenantId}:hour";
            var dayKey = $"{RateLimitKeyPrefix}{tenantId}:day";
            var monthKey = $"{RateLimitKeyPrefix}{tenantId}:month";
            
            // Get all counts in parallel
            var minuteTask = db.StringGetAsync(minuteKey);
            var hourTask = db.StringGetAsync(hourKey);
            var dayTask = db.StringGetAsync(dayKey);
            var monthTask = db.StringGetAsync(monthKey);
            
            await Task.WhenAll(minuteTask, hourTask, dayTask, monthTask);
            
            var minuteCount = minuteTask.Result.HasValue ? (int)minuteTask.Result : 0;
            var hourCount = hourTask.Result.HasValue ? (int)hourTask.Result : 0;
            var dayCount = dayTask.Result.HasValue ? (int)dayTask.Result : 0;
            var monthCount = monthTask.Result.HasValue ? (int)monthTask.Result : 0;
            
            // Check minute limit
            if (minuteCount >= _options.RateLimitPerMinute)
            {
                var ttl = await db.KeyTimeToLiveAsync(minuteKey);
                var retryAfter = (int)(ttl?.TotalSeconds ?? 30);
                
                _logger.LogWarning(
                    "AI rate limit exceeded (minute) | TenantId: {TenantId} | Count: {Count} | Limit: {Limit}",
                    tenantId, minuteCount, _options.RateLimitPerMinute);
                
                return RateLimitResult.CreateDenied(minuteCount, _options.RateLimitPerMinute, retryAfter);
            }
            
            // Check hour limit
            if (hourCount >= _options.RateLimitPerHour)
            {
                var ttl = await db.KeyTimeToLiveAsync(hourKey);
                var retryAfter = (int)(ttl?.TotalSeconds ?? 1800);
                
                _logger.LogWarning(
                    "AI rate limit exceeded (hour) | TenantId: {TenantId} | Count: {Count} | Limit: {Limit}",
                    tenantId, hourCount, _options.RateLimitPerHour);
                
                return RateLimitResult.CreateDenied(hourCount, _options.RateLimitPerHour, retryAfter);
            }
            
            // PHASE 6.5: Check day limit (cost ceiling)
            if (dayCount >= _options.RateLimitPerDay)
            {
                var ttl = await db.KeyTimeToLiveAsync(dayKey);
                var retryAfter = (int)(ttl?.TotalSeconds ?? 43200);
                
                _logger.LogError(
                    "AI DAILY COST CEILING EXCEEDED | TenantId: {TenantId} | Count: {Count} | Limit: {Limit}",
                    tenantId, dayCount, _options.RateLimitPerDay);
                
                return RateLimitResult.CreateDenied(dayCount, _options.RateLimitPerDay, retryAfter);
            }
            
            // PHASE 6.5: Check month limit (cost ceiling)
            if (monthCount >= _options.RateLimitPerMonth)
            {
                var ttl = await db.KeyTimeToLiveAsync(monthKey);
                var retryAfter = (int)(ttl?.TotalSeconds ?? 86400);
                
                _logger.LogError(
                    "AI MONTHLY COST CEILING EXCEEDED | TenantId: {TenantId} | Count: {Count} | Limit: {Limit}",
                    tenantId, monthCount, _options.RateLimitPerMonth);
                
                return RateLimitResult.CreateDenied(monthCount, _options.RateLimitPerMonth, retryAfter);
            }
            
            return RateLimitResult.CreateAllowed(minuteCount, _options.RateLimitPerMinute);
        }
        catch (Exception ex)
        {
            // On Redis failure, allow the request (fail open)
            _logger.LogWarning(ex, "Rate limit check failed, allowing request | TenantId: {TenantId}", tenantId);
            return RateLimitResult.CreateAllowed(0, _options.RateLimitPerMinute);
        }
    }
    
    /// <inheritdoc />
    public async Task RecordRequestAsync(
        Guid tenantId, 
        int tokensUsed, 
        string modelId,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        if (db == null)
        {
            return; // Redis unavailable, skip recording
        }
        
        try
        {
            // PHASE 6.5: Increment all time windows atomically
            var minuteKey = $"{RateLimitKeyPrefix}{tenantId}:minute";
            var hourKey = $"{RateLimitKeyPrefix}{tenantId}:hour";
            var dayKey = $"{RateLimitKeyPrefix}{tenantId}:day";
            var monthKey = $"{RateLimitKeyPrefix}{tenantId}:month";
            
            // Increment all counters in parallel
            var tasks = new[]
            {
                IncrementWithTtlAsync(db, minuteKey, TimeSpan.FromSeconds(60)),
                IncrementWithTtlAsync(db, hourKey, TimeSpan.FromHours(1)),
                IncrementWithTtlAsync(db, dayKey, TimeSpan.FromDays(1)),
                IncrementWithTtlAsync(db, monthKey, TimeSpan.FromDays(30))
            };
            
            await Task.WhenAll(tasks);
            
            // Track daily usage for analytics
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var usageKey = $"{UsageKeyPrefix}{tenantId}:{today}";
            
            // Store usage data (tokens, model, count) - simple increment for now
            var usageData = await db.StringGetAsync(usageKey);
            var usage = ParseUsage(usageData);
            
            usage.TotalRequests++;
            usage.TotalTokens += tokensUsed;
            if (!usage.ModelUsage.ContainsKey(modelId))
                usage.ModelUsage[modelId] = 0;
            usage.ModelUsage[modelId]++;
            
            await db.StringSetAsync(usageKey, SerializeUsage(usage), TimeSpan.FromDays(7));
            
            _logger.LogDebug(
                "AI request recorded | TenantId: {TenantId} | Model: {Model} | Tokens: {Tokens}",
                tenantId, modelId, tokensUsed);
        }
        catch (Exception ex)
        {
            // Don't fail the request if tracking fails
            _logger.LogWarning(ex, "Failed to record AI request | TenantId: {TenantId}", tenantId);
        }
    }
    
    /// <summary>
    /// Increment a Redis key with TTL (only set TTL if key doesn't exist).
    /// PHASE 6.5: Helper for atomic increment with expiry.
    /// </summary>
    private async Task IncrementWithTtlAsync(IDatabase db, string key, TimeSpan ttl)
    {
        await db.StringIncrementAsync(key);
        await db.KeyExpireAsync(key, ttl, ExpireWhen.HasNoExpiry);
    }
    
    private UsageData ParseUsage(RedisValue json)
    {
        if (json.IsNullOrEmpty)
            return new UsageData();
        
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<UsageData>(json.ToString()) ?? new UsageData();
        }
        catch
        {
            return new UsageData();
        }
    }
    
    private string SerializeUsage(UsageData usage)
    {
        return System.Text.Json.JsonSerializer.Serialize(usage);
    }
    
    private class UsageData
    {
        public int TotalRequests { get; set; }
        public int TotalTokens { get; set; }
        public Dictionary<string, int> ModelUsage { get; set; } = new();
    }
}

