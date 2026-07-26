using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.WhatsApp.Services;

public class WhatsAppRateLimiter : IWhatsAppRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly WhatsAppRateLimitSettings _settings;
    private readonly ILogger<WhatsAppRateLimiter> _logger;

    public WhatsAppRateLimiter(
        IConnectionMultiplexer redis,
        IOptions<WhatsAppRateLimitSettings> settings,
        ILogger<WhatsAppRateLimiter> logger)
    {
        _redis = redis;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<bool> CanSendMessageAsync(Guid tenantId, Guid accountId, string recipientPhone, CancellationToken ct = default)
    {
        return await CanSendMessageAsync(tenantId, accountId, recipientPhone, null, ct);
    }
    
    public async Task<bool> CanSendMessageAsync(Guid tenantId, Guid accountId, string recipientPhone, string? messageType, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        
        // 1. Check Tenant Daily Limit
        var tenantKey = $"whatsapp:ratelimit:{tenantId}:daily";
        var tenantCount = await db.StringGetAsync(tenantKey);
        if (tenantCount.HasValue && (int)tenantCount >= _settings.TenantDailyLimit)
        {
            _logger.LogWarning("Tenant {TenantId} exceeded daily WhatsApp limit", tenantId);
            return false;
        }

        // 2. Check Account Hourly Limit
        var accountKey = $"whatsapp:ratelimit:{accountId}:hourly";
        var accountCount = await db.StringGetAsync(accountKey);
        if (accountCount.HasValue && (int)accountCount >= _settings.AccountHourlyLimit)
        {
            _logger.LogWarning("Account {AccountId} exceeded hourly WhatsApp limit", accountId);
            return false;
        }

        // 3. Check Recipient Cooldown (bypass for transactional messages)
        // Transactional messages like ORDER_CONFIRMATION and shipment updates are important
        // and should not be blocked by the recipient cooldown
        var transactionalTypes = new[] { "ORDER_CONFIRMATION", "SHIPMENT_CREATED", "SHIPMENT_IN_TRANSIT", "SHIPMENT_DELIVERED" };
        var isTransactional = messageType != null && transactionalTypes.Any(t => 
            t.Equals(messageType, StringComparison.OrdinalIgnoreCase) ||
            messageType.Replace("_", "").Equals(t.Replace("_", ""), StringComparison.OrdinalIgnoreCase));
        
        if (!isTransactional)
        {
            var recipientKey = $"whatsapp:ratelimit:{tenantId}:{recipientPhone}:cooldown";
            if (await db.KeyExistsAsync(recipientKey))
            {
                _logger.LogInformation("Recipient {Phone} is in cooldown for Tenant {TenantId}", recipientPhone, tenantId);
                return false;
            }
        }
        else
        {
            _logger.LogDebug("[WhatsApp] Bypassing recipient cooldown for transactional message | Type: {MessageType}", messageType);
        }

        return true;
    }

    public async Task RecordMessageSentAsync(Guid tenantId, Guid accountId, string recipientPhone, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();

        // 1. Increment Tenant Daily
        var tenantKey = $"whatsapp:ratelimit:{tenantId}:daily";
        await db.StringIncrementAsync(tenantKey);
        // Ensure TTL is set if new key (simplified, could check first or set absolute TTL based on end of day)
        // Here we just refresh TTL to 24h which is simple but rolling. 
        // For strict daily limits resetting at midnight, we'd calculate time until midnight.
        // For this implementation, rolling 24h window for the KEY (not the count reset) might be issue if we just increment.
        // Better: Set TTL only if key didn't exist? StringIncrement doesn't reset TTL.
        // We'll leave it simple: 24h from first message.
        if (await db.KeyTimeToLiveAsync(tenantKey) == null)
        {
            await db.KeyExpireAsync(tenantKey, TimeSpan.FromHours(24));
        }

        // 2. Increment Account Hourly
        var accountKey = $"whatsapp:ratelimit:{accountId}:hourly";
        await db.StringIncrementAsync(accountKey);
        if (await db.KeyTimeToLiveAsync(accountKey) == null)
        {
            await db.KeyExpireAsync(accountKey, TimeSpan.FromHours(1));
        }

        // 3. Set Recipient Cooldown
        var recipientKey = $"whatsapp:ratelimit:{tenantId}:{recipientPhone}:cooldown";
        await db.StringSetAsync(recipientKey, "1", TimeSpan.FromSeconds(_settings.RecipientCooldownSeconds));
    }

    public TimeSpan GetRandomDelay()
    {
        var ms = Random.Shared.Next(_settings.MinDelayMs, _settings.MaxDelayMs);
        return TimeSpan.FromMilliseconds(ms);
    }
}
