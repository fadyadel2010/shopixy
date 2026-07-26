using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using StackExchange.Redis;

namespace PointOfSale.API.Services.Platform;

/// <summary>
/// Redis-backed rate limiting service for public endpoints.
/// Phase 8.3: Tenant Registration Hardening
/// 
/// Provides sliding window rate limiting based on IP + endpoint.
/// Gracefully degrades if Redis is unavailable (allows requests).
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// Checks if a request should be allowed based on rate limits.
    /// </summary>
    /// <param name="clientIp">Client IP address</param>
    /// <param name="endpoint">Endpoint identifier (e.g., "register", "subdomain-check")</param>
    /// <param name="limit">Maximum requests allowed</param>
    /// <param name="windowSeconds">Time window in seconds</param>
    /// <returns>True if allowed, false if rate limited</returns>
    Task<RateLimitResult> CheckRateLimitAsync(string clientIp, string endpoint, int limit, int windowSeconds);
    
    /// <summary>
    /// Records a rate limit hit for tracking/logging.
    /// </summary>
    Task RecordRateLimitHitAsync(string clientIp, string endpoint);
    
    /// <summary>
    /// Checks if an IP is currently in abuse-triggered stricter mode.
    /// </summary>
    Task<bool> IsInAbuseThrottleAsync(string clientIp);
    
    /// <summary>
    /// Marks an IP as abusive (triggers stricter limits).
    /// </summary>
    Task MarkAsAbusiveAsync(string clientIp, TimeSpan duration);
}

public class RateLimitResult
{
    public bool Allowed { get; set; }
    public int CurrentCount { get; set; }
    public int Limit { get; set; }
    public int RetryAfterSeconds { get; set; }
    public string? Message { get; set; }
}

public class RateLimitService : IRateLimitService
{
    private readonly RedisConnectionFactory _redis;
    private readonly PlatformSecuritySettings _settings;
    private readonly ILogger<RateLimitService> _logger;
    
    private const string KeyPrefix = "ratelimit";
    private const string AbuseKeyPrefix = "abuse";
    
    public RateLimitService(
        RedisConnectionFactory redis,
        IOptions<PlatformSecuritySettings> settings,
        ILogger<RateLimitService> logger)
    {
        _redis = redis;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<RateLimitResult> CheckRateLimitAsync(
        string clientIp, 
        string endpoint, 
        int limit, 
        int windowSeconds)
    {
        // If rate limiting is disabled, always allow
        if (!_settings.RateLimitEnabled)
        {
            return new RateLimitResult { Allowed = true, CurrentCount = 0, Limit = limit };
        }
        
        var db = _redis.GetDatabase();
        
        // Graceful degradation: if Redis unavailable, allow request
        if (db == null)
        {
            _logger.LogWarning(
                "Rate limiting unavailable (Redis down). Allowing request. IP: {IP}, Endpoint: {Endpoint}",
                MaskIp(clientIp), endpoint);
            return new RateLimitResult { Allowed = true, CurrentCount = 0, Limit = limit };
        }
        
        // Check for abuse throttle
        var isAbusive = await IsInAbuseThrottleAsync(clientIp);
        var effectiveLimit = isAbusive 
            ? (int)(limit * _settings.AbuseRateLimitMultiplier) 
            : limit;
        
        var key = GetRateLimitKey(clientIp, endpoint);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowStart = now - windowSeconds;
        
        try
        {
            // Use Redis sorted set for sliding window
            var transaction = db.CreateTransaction();
            
            // Remove old entries outside the window
            _ = transaction.SortedSetRemoveRangeByScoreAsync(key, 0, windowStart);
            
            // Count current entries
            var countTask = transaction.SortedSetLengthAsync(key);
            
            // Add current request
            _ = transaction.SortedSetAddAsync(key, now.ToString(), now);
            
            // Set expiry
            _ = transaction.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds + 10));
            
            await transaction.ExecuteAsync();
            
            var currentCount = (int)await countTask;
            
            if (currentCount >= effectiveLimit)
            {
                _logger.LogWarning(
                    "Rate limit triggered | IP: {IP} | Endpoint: {Endpoint} | Count: {Count}/{Limit} | Abusive: {Abusive}",
                    MaskIp(clientIp), endpoint, currentCount, effectiveLimit, isAbusive);
                
                return new RateLimitResult
                {
                    Allowed = false,
                    CurrentCount = currentCount,
                    Limit = effectiveLimit,
                    RetryAfterSeconds = windowSeconds,
                    Message = "Too many attempts. Please try again later."
                };
            }
            
            return new RateLimitResult
            {
                Allowed = true,
                CurrentCount = currentCount + 1, // Include current request
                Limit = effectiveLimit
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Rate limit check failed. Allowing request. IP: {IP}, Endpoint: {Endpoint}",
                MaskIp(clientIp), endpoint);
            
            // Graceful degradation
            return new RateLimitResult { Allowed = true, CurrentCount = 0, Limit = limit };
        }
    }

    public async Task RecordRateLimitHitAsync(string clientIp, string endpoint)
    {
        var db = _redis.GetDatabase();
        if (db == null) return;
        
        try
        {
            var hitKey = $"{KeyPrefix}:hits:{endpoint}:{GetDayKey()}";
            await db.StringIncrementAsync(hitKey);
            await db.KeyExpireAsync(hitKey, TimeSpan.FromDays(1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record rate limit hit");
        }
    }

    public async Task<bool> IsInAbuseThrottleAsync(string clientIp)
    {
        if (!_settings.AbuseDetectionEnabled)
        {
            return false;
        }
        
        var db = _redis.GetDatabase();
        if (db == null) return false;
        
        try
        {
            var abuseKey = $"{AbuseKeyPrefix}:{HashIp(clientIp)}";
            return await db.KeyExistsAsync(abuseKey);
        }
        catch
        {
            return false;
        }
    }

    public async Task MarkAsAbusiveAsync(string clientIp, TimeSpan duration)
    {
        var db = _redis.GetDatabase();
        if (db == null) return;
        
        try
        {
            var abuseKey = $"{AbuseKeyPrefix}:{HashIp(clientIp)}";
            await db.StringSetAsync(abuseKey, "1", duration);
            
            _logger.LogWarning(
                "IP marked as abusive | IP: {IP} | Duration: {Duration}",
                MaskIp(clientIp), duration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark IP as abusive");
        }
    }

    private static string GetRateLimitKey(string clientIp, string endpoint)
    {
        // Hash IP for privacy
        return $"{KeyPrefix}:{endpoint}:{HashIp(clientIp)}";
    }

    private static string GetDayKey()
    {
        return DateTime.UtcNow.ToString("yyyyMMdd");
    }

    private static string HashIp(string ip)
    {
        // Use SHA256 for privacy-preserving IP storage
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(ip);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..16]; // First 16 chars
    }

    private static string MaskIp(string ip)
    {
        // Mask last octet for logging
        var parts = ip.Split('.');
        if (parts.Length == 4)
        {
            return $"{parts[0]}.{parts[1]}.{parts[2]}.***";
        }
        return "***";
    }
}
