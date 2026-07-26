using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Redis-backed rate limiter for Storefront authentication.
/// 
/// SECURITY CRITICAL:
/// - Prevents brute force attacks on login
/// - Prevents refresh token abuse
/// - Redis-backed for multi-instance support
/// 
/// RATE LIMITS:
/// - Login: 5 attempts per 5 minutes per tenant/email/IP
/// - Refresh: 10 attempts per 10 minutes per tenant/tokenHash
/// 
/// Phase: 10.3.x - Authentication Hardening
/// </summary>
public interface IStorefrontAuthRateLimiter
{
    /// <summary>
    /// Checks and increments login attempt counter.
    /// </summary>
    /// <returns>True if allowed, false if rate limited.</returns>
    Task<RateLimitResult> CheckLoginAsync(Guid tenantId, string email, string ipAddress);

    /// <summary>
    /// Checks and increments refresh attempt counter.
    /// </summary>
    /// <returns>True if allowed, false if rate limited.</returns>
    Task<RateLimitResult> CheckRefreshAsync(Guid tenantId, string tokenHash);

    /// <summary>
    /// Checks and increments password-reset attempt counter.
    /// </summary>
    Task<RateLimitResult> CheckPasswordResetAsync(Guid tenantId, string email, string ipAddress);

    /// <summary>
    /// Resets login counter on successful login.
    /// </summary>
    Task ResetLoginCounterAsync(Guid tenantId, string email, string ipAddress);
}

public class RateLimitResult
{
    public bool Allowed { get; set; }
    public int CurrentAttempts { get; set; }
    public int MaxAttempts { get; set; }
    public int RetryAfterSeconds { get; set; }

    public static RateLimitResult Allow(int current, int max) => new()
    {
        Allowed = true,
        CurrentAttempts = current,
        MaxAttempts = max
    };

    public static RateLimitResult Deny(int current, int max, int retryAfter) => new()
    {
        Allowed = false,
        CurrentAttempts = current,
        MaxAttempts = max,
        RetryAfterSeconds = retryAfter
    };
}

/// <summary>
/// Redis implementation of authentication rate limiter.
/// 
/// SECURITY BOUNDARY COMMENT:
/// This rate limiter is ONLY for Storefront authentication.
/// POS authentication has separate rate limiting.
/// </summary>
public class StorefrontAuthRateLimiter : IStorefrontAuthRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<StorefrontAuthRateLimiter> _logger;

    // Login: 5 attempts per 5 minutes
    private const int LoginMaxAttempts = 5;
    private static readonly TimeSpan LoginWindow = TimeSpan.FromMinutes(5);
    private const string LoginKeyPrefix = "storefront:auth:login";

    // Refresh: 10 attempts per 10 minutes
    private const int RefreshMaxAttempts = 10;
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(10);
    private const string RefreshKeyPrefix = "storefront:auth:refresh";

    // Password reset: 3 attempts per 15 minutes
    private const int PasswordResetMaxAttempts = 3;
    private static readonly TimeSpan PasswordResetWindow = TimeSpan.FromMinutes(15);
    private const string PasswordResetKeyPrefix = "storefront:auth:password-reset";

    public StorefrontAuthRateLimiter(
        IConnectionMultiplexer redis,
        ILogger<StorefrontAuthRateLimiter> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<RateLimitResult> CheckLoginAsync(Guid tenantId, string email, string ipAddress)
    {
        return await CheckEmailScopedLimitAsync(
            tenantId,
            email,
            ipAddress,
            LoginKeyPrefix,
            LoginMaxAttempts,
            LoginWindow);
    }

    public async Task<RateLimitResult> CheckPasswordResetAsync(Guid tenantId, string email, string ipAddress)
    {
        return await CheckEmailScopedLimitAsync(
            tenantId,
            email,
            ipAddress,
            PasswordResetKeyPrefix,
            PasswordResetMaxAttempts,
            PasswordResetWindow);
    }

    private async Task<RateLimitResult> CheckEmailScopedLimitAsync(
        Guid tenantId,
        string email,
        string ipAddress,
        string keyPrefix,
        int maxAttempts,
        TimeSpan window)
    {
        if (tenantId == Guid.Empty || string.IsNullOrEmpty(email))
        {
            return RateLimitResult.Allow(0, maxAttempts);
        }

        try
        {
            var db = _redis.GetDatabase();

            // Normalize email to lowercase for consistent key
            var normalizedEmail = email.ToLowerInvariant().Trim();
            
            // SECURITY: Use fallback for missing IP to prevent rate-limit bypass
            var ipSegment = string.IsNullOrEmpty(ipAddress) ? "no-ip" : ipAddress;
            var key = $"{keyPrefix}:{tenantId}:{normalizedEmail}:{ipSegment}";

            var current = await db.StringIncrementAsync(key);

            // Set expiry on first attempt
            if (current == 1)
            {
                await db.KeyExpireAsync(key, window);
            }

            if (current > maxAttempts)
            {
                var ttl = await db.KeyTimeToLiveAsync(key);
                var retryAfter = ttl.HasValue ? (int)ttl.Value.TotalSeconds : (int)window.TotalSeconds;

                _logger.LogWarning(
                    "[RateLimiter] {Action} | TenantId: {TenantId} | Email: {Email} | IP: {IP} | Attempts: {Attempts}",
                    "RATE_LIMIT_HIT",
                    tenantId,
                    MaskEmail(normalizedEmail),
                    MaskIp(ipAddress),
                    current);

                return RateLimitResult.Deny((int)current, maxAttempts, retryAfter);
            }

            return RateLimitResult.Allow((int)current, maxAttempts);
        }
        catch (Exception ex)
        {
            // On Redis failure, allow request (availability over strict rate limiting)
            _logger.LogError(ex,
                "[RateLimiter] Redis check failed, allowing request | TenantId: {TenantId}",
                tenantId);
            return RateLimitResult.Allow(0, maxAttempts);
        }
    }

    public async Task<RateLimitResult> CheckRefreshAsync(Guid tenantId, string tokenHash)
    {
        if (tenantId == Guid.Empty || string.IsNullOrEmpty(tokenHash))
        {
            return RateLimitResult.Allow(0, RefreshMaxAttempts);
        }

        try
        {
            var db = _redis.GetDatabase();

            // Use first 16 chars of hash for key (enough for uniqueness)
            var shortHash = tokenHash.Length > 16 ? tokenHash[..16] : tokenHash;
            var key = $"{RefreshKeyPrefix}:{tenantId}:{shortHash}";

            var current = await db.StringIncrementAsync(key);

            // Set expiry on first attempt
            if (current == 1)
            {
                await db.KeyExpireAsync(key, RefreshWindow);
            }

            if (current > RefreshMaxAttempts)
            {
                var ttl = await db.KeyTimeToLiveAsync(key);
                var retryAfter = ttl.HasValue ? (int)ttl.Value.TotalSeconds : (int)RefreshWindow.TotalSeconds;

                _logger.LogWarning(
                    "[RateLimiter] {Action} on refresh | TenantId: {TenantId} | Attempts: {Attempts}",
                    "RATE_LIMIT_HIT",
                    tenantId,
                    current);

                return RateLimitResult.Deny((int)current, RefreshMaxAttempts, retryAfter);
            }

            return RateLimitResult.Allow((int)current, RefreshMaxAttempts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[RateLimiter] Redis check failed, allowing request | TenantId: {TenantId}",
                tenantId);
            return RateLimitResult.Allow(0, RefreshMaxAttempts);
        }
    }

    public async Task ResetLoginCounterAsync(Guid tenantId, string email, string ipAddress)
    {
        if (tenantId == Guid.Empty || string.IsNullOrEmpty(email))
        {
            return;
        }

        try
        {
            var db = _redis.GetDatabase();
            var normalizedEmail = email.ToLowerInvariant().Trim();
            
            // SECURITY: Use same IP normalization as CheckEmailScopedLimitAsync
            var ipSegment = string.IsNullOrEmpty(ipAddress) ? "no-ip" : ipAddress;
            var key = $"{LoginKeyPrefix}:{tenantId}:{normalizedEmail}:{ipSegment}";
            
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RateLimiter] Failed to reset counter");
        }
    }

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "[empty]";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
            return "***@" + (atIndex >= 0 ? email[(atIndex + 1)..] : "***");

        return email[0] + "***" + email[(atIndex - 1)..];
    }

    private static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return "[unknown]";

        var lastDot = ip.LastIndexOf('.');
        if (lastDot > 0)
            return ip[..(lastDot + 1)] + "***";

        return ip[..Math.Min(ip.Length, 3)] + "***";
    }
}
