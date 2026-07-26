using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Infrastructure.Data;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.WhatsApp.Jobs;

/// <summary>
/// Hangfire job that cleans up stale WhatsApp sessions from Redis.
/// Runs daily to remove orphaned session data.
/// </summary>
public class WhatsAppSessionCleanupJob
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<WhatsAppSessionCleanupJob> _logger;

    private const string SessionKeyPattern = "whatsapp:session:*";

    public WhatsAppSessionCleanupJob(
        IConnectionMultiplexer redis,
        ApplicationDbContext dbContext,
        ILogger<WhatsAppSessionCleanupJob> logger)
    {
        _redis = redis;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Main job execution method - called by Hangfire.
    /// Cleans up orphaned sessions and rate limit counters.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[WhatsApp Cleanup] Starting session cleanup job...");

        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints().First());

        int cleanedSessions = 0;
        int cleanedRateLimits = 0;

        try
        {
            await foreach (var key in server.KeysAsync(pattern: SessionKeyPattern))
            {
                var suffix = key.ToString().Substring("whatsapp:session:".Length);
                if (!Guid.TryParse(suffix, out var ownerId))
                {
                    await db.KeyDeleteAsync(key);
                    cleanedSessions++;
                    continue;
                }

                var accountExists = await _dbContext.WhatsAppAccounts
                    .IgnoreQueryFilters()
                    .AnyAsync(account => account.Id == ownerId, ct);
                var tenantExists = await _dbContext.Tenants
                    .IgnoreQueryFilters()
                    .AnyAsync(tenant => tenant.Id == ownerId, ct);

                if (!accountExists && !tenantExists)
                {
                    await db.KeyDeleteAsync(key);
                    cleanedSessions++;
                    continue;
                }

                if (await db.KeyTimeToLiveAsync(key) == null)
                {
                    await db.KeyExpireAsync(key, TimeSpan.FromDays(30));
                }
            }

            // Clean up old rate limit keys (older than 24 hours)
            var rateLimitPattern = "whatsapp:ratelimit:*";
            await foreach (var key in server.KeysAsync(pattern: rateLimitPattern))
            {
                var ttl = await db.KeyTimeToLiveAsync(key);
                if (!ttl.HasValue)
                {
                    // Key has no TTL, set a 24-hour expiry
                    await db.KeyExpireAsync(key, TimeSpan.FromHours(24));
                    cleanedRateLimits++;
                }
            }

            // Clean up old OTP keys (should be auto-expired, but just in case)
            var otpPattern = "whatsapp:otp:*";
            await foreach (var key in server.KeysAsync(pattern: otpPattern))
            {
                var ttl = await db.KeyTimeToLiveAsync(key);
                if (!ttl.HasValue)
                {
                    // Expired OTP without TTL - delete
                    await db.KeyDeleteAsync(key);
                    cleanedSessions++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WhatsApp Cleanup] Error during cleanup");
        }

        _logger.LogInformation(
            "[WhatsApp Cleanup] Cleanup completed | SessionsCleaned: {Sessions} | RateLimitsCleaned: {RateLimits}",
            cleanedSessions, cleanedRateLimits);
    }
}
