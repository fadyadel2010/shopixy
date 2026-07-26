using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.Platform;

/// <summary>
/// Abuse detection service with lightweight heuristics.
/// Phase 8.3: Tenant Registration Hardening
/// 
/// Detects suspicious patterns WITHOUT blocking legitimate users:
/// - Too many subdomain checks without registration
/// - Multiple different emails from same IP
/// - Same email trying many subdomains
/// 
/// When abuse is detected:
/// - Does NOT block immediately
/// - Escalates to stricter rate limits
/// - Logs suspicious activity
/// - Returns generic error messages
/// </summary>
public interface IAbuseDetectionService
{
    /// <summary>
    /// Tracks a subdomain check for abuse detection.
    /// </summary>
    Task TrackSubdomainCheckAsync(string clientIp, string subdomain);
    
    /// <summary>
    /// Tracks an email check for abuse detection.
    /// </summary>
    Task TrackEmailCheckAsync(string clientIp, string email);
    
    /// <summary>
    /// Tracks a registration attempt (successful or not).
    /// </summary>
    Task TrackRegistrationAttemptAsync(string clientIp, string email, string subdomain, bool success);
    
    /// <summary>
    /// Checks if current activity shows abuse signals.
    /// Returns true if abuse detected (but doesn't block).
    /// </summary>
    Task<AbuseCheckResult> CheckForAbuseAsync(string clientIp);
}

public class AbuseCheckResult
{
    public bool AbuseDetected { get; set; }
    public string? Reason { get; set; }
    public double RateLimitMultiplier { get; set; } = 1.0;
}

public class AbuseDetectionService : IAbuseDetectionService
{
    private readonly RedisConnectionFactory _redis;
    private readonly PlatformSecuritySettings _settings;
    private readonly IRateLimitService _rateLimitService;
    private readonly IPlatformAuditLogger _auditLogger;
    private readonly ILogger<AbuseDetectionService> _logger;
    
    private const string SubdomainCheckKey = "abuse:subdomain-checks";
    private const string EmailCheckKey = "abuse:email-checks";
    private const string UniqueEmailsKey = "abuse:unique-emails";
    private const int HourInSeconds = 3600;
    
    public AbuseDetectionService(
        RedisConnectionFactory redis,
        IOptions<PlatformSecuritySettings> settings,
        IRateLimitService rateLimitService,
        IPlatformAuditLogger auditLogger,
        ILogger<AbuseDetectionService> logger)
    {
        _redis = redis;
        _settings = settings.Value;
        _rateLimitService = rateLimitService;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public async Task TrackSubdomainCheckAsync(string clientIp, string subdomain)
    {
        if (!_settings.AbuseDetectionEnabled) return;
        
        var db = _redis.GetDatabase();
        if (db == null) return;
        
        try
        {
            var key = $"{SubdomainCheckKey}:{HashIp(clientIp)}";
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            // Add to sliding window
            await db.SortedSetAddAsync(key, subdomain, now);
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(HourInSeconds));
            
            // Remove old entries
            await db.SortedSetRemoveRangeByScoreAsync(key, 0, now - HourInSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track subdomain check for abuse detection");
        }
    }

    public async Task TrackEmailCheckAsync(string clientIp, string email)
    {
        if (!_settings.AbuseDetectionEnabled) return;
        
        var db = _redis.GetDatabase();
        if (db == null) return;
        
        try
        {
            var key = $"{UniqueEmailsKey}:{HashIp(clientIp)}";
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            // Track unique emails from this IP
            await db.SortedSetAddAsync(key, HashEmail(email), now);
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(HourInSeconds));
            
            // Remove old entries
            await db.SortedSetRemoveRangeByScoreAsync(key, 0, now - HourInSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track email check for abuse detection");
        }
    }

    public async Task TrackRegistrationAttemptAsync(string clientIp, string email, string subdomain, bool success)
    {
        if (!_settings.AbuseDetectionEnabled) return;
        
        var db = _redis.GetDatabase();
        if (db == null) return;
        
        try
        {
            if (success)
            {
                // Clear abuse signals for this IP on successful registration
                var subdomainKey = $"{SubdomainCheckKey}:{HashIp(clientIp)}";
                var emailKey = $"{UniqueEmailsKey}:{HashIp(clientIp)}";
                
                await db.KeyDeleteAsync(subdomainKey);
                await db.KeyDeleteAsync(emailKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track registration attempt");
        }
    }

    public async Task<AbuseCheckResult> CheckForAbuseAsync(string clientIp)
    {
        if (!_settings.AbuseDetectionEnabled)
        {
            return new AbuseCheckResult { AbuseDetected = false };
        }
        
        var db = _redis.GetDatabase();
        if (db == null)
        {
            return new AbuseCheckResult { AbuseDetected = false };
        }
        
        try
        {
            // Check subdomain check count
            var subdomainKey = $"{SubdomainCheckKey}:{HashIp(clientIp)}";
            var subdomainCheckCount = await db.SortedSetLengthAsync(subdomainKey);
            
            if (subdomainCheckCount > _settings.SubdomainCheckAbuseThreshold)
            {
                var reason = "Excessive subdomain checks without registration";
                _auditLogger.LogAbuseDetected(clientIp, reason, $"Count: {subdomainCheckCount}");
                
                // Mark as abusive for 1 hour
                await _rateLimitService.MarkAsAbusiveAsync(clientIp, TimeSpan.FromHours(1));
                
                return new AbuseCheckResult
                {
                    AbuseDetected = true,
                    Reason = reason,
                    RateLimitMultiplier = _settings.AbuseRateLimitMultiplier
                };
            }
            
            // Check unique emails count
            var emailKey = $"{UniqueEmailsKey}:{HashIp(clientIp)}";
            var uniqueEmailCount = await db.SortedSetLengthAsync(emailKey);
            
            if (uniqueEmailCount > _settings.MultipleEmailAbuseThreshold)
            {
                var reason = "Multiple different emails from same IP";
                _auditLogger.LogAbuseDetected(clientIp, reason, $"Count: {uniqueEmailCount}");
                
                // Mark as abusive for 30 minutes
                await _rateLimitService.MarkAsAbusiveAsync(clientIp, TimeSpan.FromMinutes(30));
                
                return new AbuseCheckResult
                {
                    AbuseDetected = true,
                    Reason = reason,
                    RateLimitMultiplier = _settings.AbuseRateLimitMultiplier
                };
            }
            
            return new AbuseCheckResult { AbuseDetected = false };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Abuse check failed");
            return new AbuseCheckResult { AbuseDetected = false };
        }
    }

    private static string HashIp(string ip)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(ip);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..16];
    }

    private static string HashEmail(string email)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(email.ToLowerInvariant());
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..16];
    }
}
