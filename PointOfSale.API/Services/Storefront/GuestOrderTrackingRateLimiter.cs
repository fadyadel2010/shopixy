using StackExchange.Redis;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Redis-backed rate limiter for guest order tracking.
/// 
/// SECURITY HARDENING (Audit C-2):
/// - Prevents brute-force enumeration of order numbers + emails
/// - Protects customer PII (names, addresses, phone numbers, order contents)
/// - Redis-backed for multi-instance support (survives restarts)
/// 
/// RATE LIMITS:
/// - 10 requests per 60 seconds per IP address
/// - On Redis failure: DENY (fail-closed) — prevents bypass via Redis exhaustion
/// </summary>
public interface IGuestOrderTrackingRateLimiter
{
    /// <summary>
    /// Checks if the given IP is currently rate-limited for guest order tracking.
    /// Increments the attempt counter on each call.
    /// </summary>
    /// <returns>Result indicating whether the request is allowed or blocked.</returns>
    Task<GuestTrackingRateLimitResult> CheckAsync(string ipAddress);
}

public sealed class GuestTrackingRateLimitResult
{
    public bool Allowed { get; init; }
    public int CurrentAttempts { get; init; }
    public int MaxAttempts { get; init; }
    public int RetryAfterSeconds { get; init; }

    public static GuestTrackingRateLimitResult Allow(int current, int max) => new()
    {
        Allowed = true,
        CurrentAttempts = current,
        MaxAttempts = max
    };

    public static GuestTrackingRateLimitResult Deny(int current, int max, int retryAfter) => new()
    {
        Allowed = false,
        CurrentAttempts = current,
        MaxAttempts = max,
        RetryAfterSeconds = retryAfter
    };
}

public sealed class GuestOrderTrackingRateLimiter : IGuestOrderTrackingRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<GuestOrderTrackingRateLimiter> _logger;

    private const int MaxAttempts = 10;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    private const string KeyPrefix = "guest:order:track";

    public GuestOrderTrackingRateLimiter(
        IConnectionMultiplexer redis,
        ILogger<GuestOrderTrackingRateLimiter> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<GuestTrackingRateLimitResult> CheckAsync(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            // No IP = deny (fail-closed)
            return GuestTrackingRateLimitResult.Deny(0, MaxAttempts, (int)Window.TotalSeconds);
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
                    "[SECURITY] Guest order tracking rate limit exceeded | IP: {IP} | Attempts: {Attempts}/{Max} | RetryAfterSeconds: {RetryAfter}",
                    MaskIp(ipAddress),
                    current,
                    MaxAttempts,
                    retryAfter);

                return GuestTrackingRateLimitResult.Deny((int)current, MaxAttempts, retryAfter);
            }

            return GuestTrackingRateLimitResult.Allow((int)current, MaxAttempts);
        }
        catch (Exception ex)
        {
            // SECURITY: Fail-closed — deny on Redis failure to prevent
            // brute-force enumeration when Redis is unavailable.
            _logger.LogCritical(ex,
                "[SECURITY] Guest order tracking rate limiter Redis UNAVAILABLE — request DENIED (fail-closed) | IP: {IP}",
                MaskIp(ipAddress));

            return GuestTrackingRateLimitResult.Deny(0, MaxAttempts, 60);
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
