using StackExchange.Redis;

namespace PointOfSale.API.SuperAdmin.Services;

/// <summary>
/// Redis-backed rate limiter for Super Admin login attempts.
/// 
/// SECURITY CRITICAL:
/// - Prevents brute force attacks on the platform owner account
/// - Redis-backed for multi-instance support (survives restarts)
/// - Enforces actual blocking (HTTP 429) when threshold is exceeded
/// 
/// RATE LIMITS:
/// - Login: 5 attempts per 5 minutes per IP
/// - On Redis failure: DENY login (fail-closed — security over availability)
///   SECURITY HARDENING (Audit C-5): SuperAdmin is the highest-privilege target.
/// 
/// Fix for: C-2 (In-memory rate limiting not production safe)
/// Fix for: C-5 (Fail-open on Redis failure)
/// </summary>
public interface ISuperAdminLoginRateLimiter
{
    /// <summary>
    /// Checks if the given IP is currently rate-limited.
    /// Increments the attempt counter on each call.
    /// </summary>
    /// <returns>Result indicating whether the request is allowed or blocked.</returns>
    Task<SuperAdminRateLimitResult> CheckLoginAsync(string ipAddress, string? requestId = null);

    /// <summary>
    /// Resets the login counter for the given IP after a successful login.
    /// </summary>
    Task ResetLoginCounterAsync(string ipAddress);
}

public sealed class SuperAdminRateLimitResult
{
    public bool Allowed { get; init; }
    public int CurrentAttempts { get; init; }
    public int MaxAttempts { get; init; }
    public int RetryAfterSeconds { get; init; }

    public static SuperAdminRateLimitResult Allow(int current, int max) => new()
    {
        Allowed = true,
        CurrentAttempts = current,
        MaxAttempts = max
    };

    public static SuperAdminRateLimitResult Deny(int current, int max, int retryAfter) => new()
    {
        Allowed = false,
        CurrentAttempts = current,
        MaxAttempts = max,
        RetryAfterSeconds = retryAfter
    };
}

public sealed class SuperAdminLoginRateLimiter : ISuperAdminLoginRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SuperAdminLoginRateLimiter> _logger;

    private const int MaxAttempts = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private const string KeyPrefix = "superadmin:login:attempts";

    public SuperAdminLoginRateLimiter(
        IConnectionMultiplexer redis,
        ILogger<SuperAdminLoginRateLimiter> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// SECURITY HARDENING (Audit C-5):
    /// - Empty IP: DENY (not allow) — anonymous callers must be blocked.
    /// - Redis failure: DENY (fail-closed) — SuperAdmin is highest-privilege target.
    ///   Availability trade-off is acceptable: ops team can fix Redis before retrying.
    /// </summary>
    public async Task<SuperAdminRateLimitResult> CheckLoginAsync(string ipAddress, string? requestId = null)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return SuperAdminRateLimitResult.Deny(0, MaxAttempts, (int)Window.TotalSeconds);
        }

        try
        {
            var db = _redis.GetDatabase();
            var key = $"{KeyPrefix}:{ipAddress}";

            var current = await db.StringIncrementAsync(key);

            // Set expiry on first attempt
            if (current == 1)
            {
                await db.KeyExpireAsync(key, Window);
            }

            if (current > MaxAttempts)
            {
                var ttl = await db.KeyTimeToLiveAsync(key);
                var retryAfter = ttl.HasValue ? (int)ttl.Value.TotalSeconds : (int)Window.TotalSeconds;

                _logger.LogWarning(
                    "SuperAdmin login rate limit exceeded | RequestId: {RequestId} | IP: {IP} | Attempts: {Attempts}/{Max} | RetryAfterSeconds: {RetryAfter}",
                    requestId ?? "unknown",
                    MaskIp(ipAddress),
                    current,
                    MaxAttempts,
                    retryAfter);

                return SuperAdminRateLimitResult.Deny((int)current, MaxAttempts, retryAfter);
            }

            return SuperAdminRateLimitResult.Allow((int)current, MaxAttempts);
        }
        catch (Exception ex)
        {
            // SECURITY HARDENING (Audit C-5): FAIL CLOSED for SuperAdmin.
            // SuperAdmin is the highest-privilege account in the system.
            // If Redis is down, we DENY login attempts to prevent brute-force attacks.
            // Ops team must restore Redis before SuperAdmin can log in.
            _logger.LogCritical(ex,
                "[SECURITY] SuperAdmin login rate limiter Redis UNAVAILABLE — login DENIED (fail-closed) | RequestId: {RequestId} | IP: {IP}",
                requestId ?? "unknown",
                MaskIp(ipAddress));

            return SuperAdminRateLimitResult.Deny(0, MaxAttempts, 60);
        }
    }

    public async Task ResetLoginCounterAsync(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return;
        }

        try
        {
            var db = _redis.GetDatabase();
            var key = $"{KeyPrefix}:{ipAddress}";
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SuperAdmin login rate limiter failed to reset counter for IP: {IP}", MaskIp(ipAddress));
        }
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
