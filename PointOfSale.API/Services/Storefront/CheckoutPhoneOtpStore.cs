using System.Text.Json;
using StackExchange.Redis;

namespace PointOfSale.API.Services.Storefront;

public static class CheckoutPhoneOtpStates
{
    public const string Sent = "Sent";
    public const string Verified = "Verified";
}

public sealed class CheckoutPhoneOtpRecord
{
    public string CodeHash { get; set; } = string.Empty;
    public string CodeSalt { get; set; } = string.Empty;
    public string State { get; set; } = CheckoutPhoneOtpStates.Sent;
    public string DeliveryChannel { get; set; } = "whatsapp";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime LastRequestedAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public int DispatchCount { get; set; }
    public int VerificationAttempts { get; set; }
    public int MaxVerificationAttempts { get; set; } = 5;

    public bool IsExpired(DateTime utcNow) => ExpiresAtUtc <= utcNow;

    public bool IsVerificationBlocked => VerificationAttempts >= MaxVerificationAttempts;
}

public enum CheckoutOtpRateLimitScope
{
    PhoneNumber,
    SessionId,
    IpAddress
}

public readonly record struct CheckoutOtpRateLimitKey(
    CheckoutOtpRateLimitScope Scope,
    string Value);

public interface ICheckoutPhoneOtpStore
{
    Task<CheckoutPhoneOtpRecord?> GetOtpAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        CancellationToken ct = default);

    Task<bool> SaveOtpAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        CheckoutPhoneOtpRecord record,
        TimeSpan ttl,
        CancellationToken ct = default);

    Task<bool> DeleteOtpAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        CancellationToken ct = default);

    Task<bool> IsVerifiedAsync(
        Guid tenantId,
        string scopeId,
        string phone,
        CancellationToken ct = default);

    Task<bool> SetVerifiedAsync(
        Guid tenantId,
        string scopeId,
        string phone,
        TimeSpan ttl,
        CancellationToken ct = default);

    Task<bool> DeleteVerifiedAsync(
        Guid tenantId,
        string scopeId,
        string phone,
        CancellationToken ct = default);

    Task<int> GetRateLimitCountAsync(
        Guid tenantId,
        CheckoutOtpRateLimitKey key,
        CancellationToken ct = default);

    Task<TimeSpan?> GetRateLimitTimeToLiveAsync(
        Guid tenantId,
        CheckoutOtpRateLimitKey key,
        CancellationToken ct = default);

    Task IncrementRateLimitAsync(
        Guid tenantId,
        CheckoutOtpRateLimitKey key,
        TimeSpan ttl,
        CancellationToken ct = default);

    Task<bool> ClearRateLimitAsync(
        Guid tenantId,
        CheckoutOtpRateLimitKey key,
        CancellationToken ct = default);

    Task<bool> TryAcquireLockAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        string ownerToken,
        TimeSpan ttl,
        CancellationToken ct = default);

    Task ReleaseLockAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        string ownerToken,
        CancellationToken ct = default);
}

public sealed class CheckoutPhoneOtpStore : ICheckoutPhoneOtpStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _redis;

    public CheckoutPhoneOtpStore(IConnectionMultiplexer redis)
    {
        _redis = redis.GetDatabase();
    }

    public async Task<CheckoutPhoneOtpRecord?> GetOtpAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        CancellationToken ct = default)
    {
        var value = await _redis.StringGetAsync(BuildOtpKey(tenantId, sessionId, phone));
        if (!value.HasValue)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CheckoutPhoneOtpRecord>(value!, SerializerOptions);
        }
        catch (JsonException)
        {
            await _redis.KeyDeleteAsync(BuildOtpKey(tenantId, sessionId, phone));
            return null;
        }
    }

    public Task<bool> SaveOtpAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        CheckoutPhoneOtpRecord record,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        if (ttl <= TimeSpan.Zero)
        {
            return Task.FromResult(false);
        }

        var payload = JsonSerializer.Serialize(record, SerializerOptions);
        return _redis.StringSetAsync(BuildOtpKey(tenantId, sessionId, phone), payload, ttl);
    }

    public Task<bool> DeleteOtpAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        CancellationToken ct = default)
    {
        return _redis.KeyDeleteAsync(BuildOtpKey(tenantId, sessionId, phone));
    }

    public Task<bool> IsVerifiedAsync(
        Guid tenantId,
        string scopeId,
        string phone,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            return Task.FromResult(false);
        }

        return _redis.KeyExistsAsync(BuildVerifiedKey(tenantId, scopeId, phone));
    }

    public Task<bool> SetVerifiedAsync(
        Guid tenantId,
        string scopeId,
        string phone,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scopeId) || ttl <= TimeSpan.Zero)
        {
            return Task.FromResult(false);
        }

        return _redis.StringSetAsync(BuildVerifiedKey(tenantId, scopeId, phone), "1", ttl);
    }

    public Task<bool> DeleteVerifiedAsync(
        Guid tenantId,
        string scopeId,
        string phone,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            return Task.FromResult(false);
        }

        return _redis.KeyDeleteAsync(BuildVerifiedKey(tenantId, scopeId, phone));
    }

    public async Task<int> GetRateLimitCountAsync(
        Guid tenantId,
        CheckoutOtpRateLimitKey key,
        CancellationToken ct = default)
    {
        var value = await _redis.StringGetAsync(BuildRateLimitKey(tenantId, key));
        return value.HasValue && int.TryParse(value!, out var count)
            ? count
            : 0;
    }

    public Task<TimeSpan?> GetRateLimitTimeToLiveAsync(
        Guid tenantId,
        CheckoutOtpRateLimitKey key,
        CancellationToken ct = default)
    {
        return _redis.KeyTimeToLiveAsync(BuildRateLimitKey(tenantId, key));
    }

    public async Task IncrementRateLimitAsync(
        Guid tenantId,
        CheckoutOtpRateLimitKey key,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        var redisKey = BuildRateLimitKey(tenantId, key);
        
        // Atomic INCR + EXPIRE using Lua script to prevent race conditions
        // If key doesn't exist, INCR creates it with value 1 and sets TTL
        // If key exists, INCR increments it and preserves existing TTL (doesn't reset)
        const string luaScript = @"
            local current = redis.call('INCR', KEYS[1])
            if current == 1 then
                redis.call('EXPIRE', KEYS[1], ARGV[1])
            end
            return current
        ";
        
        await _redis.ScriptEvaluateAsync(
            luaScript,
            new RedisKey[] { redisKey },
            new RedisValue[] { (long)ttl.TotalSeconds });
    }

    public Task<bool> ClearRateLimitAsync(
        Guid tenantId,
        CheckoutOtpRateLimitKey key,
        CancellationToken ct = default)
    {
        return _redis.KeyDeleteAsync(BuildRateLimitKey(tenantId, key));
    }

    public Task<bool> TryAcquireLockAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        string ownerToken,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerToken) || ttl <= TimeSpan.Zero)
        {
            return Task.FromResult(false);
        }

        return _redis.StringSetAsync(
            BuildLockKey(tenantId, sessionId, phone),
            ownerToken,
            ttl,
            When.NotExists);
    }

    public async Task ReleaseLockAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        string ownerToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerToken))
        {
            return;
        }

        var lockKey = BuildLockKey(tenantId, sessionId, phone);
        
        // Atomic check-and-delete using Lua script to prevent TOCTOU race condition
        // Only delete the lock if the current owner matches the provided ownerToken
        const string luaScript = @"
            if redis.call('GET', KEYS[1]) == ARGV[1] then
                return redis.call('DEL', KEYS[1])
            else
                return 0
            end
        ";
        
        await _redis.ScriptEvaluateAsync(
            luaScript,
            new RedisKey[] { lockKey },
            new RedisValue[] { ownerToken });
    }

    private static string BuildOtpKey(Guid tenantId, string sessionId, string phone) =>
        $"checkout_otp:{tenantId}:{sessionId}:{phone}";

    private static string BuildVerifiedKey(Guid tenantId, string scopeId, string phone) =>
        $"checkout_phone_verified:{tenantId}:{scopeId}:{phone}";

    private static string BuildRateLimitKey(Guid tenantId, CheckoutOtpRateLimitKey key)
    {
        var normalizedValue = key.Value.Trim();
        if (key.Scope == CheckoutOtpRateLimitScope.PhoneNumber)
        {
            normalizedValue = normalizedValue.Replace("+", "").Replace(" ", "").Trim();
        }

        return $"checkout_otp_ratelimit:{tenantId}:{key.Scope}:{normalizedValue}";
    }

    private static string BuildLockKey(Guid tenantId, string sessionId, string phone) =>
        $"checkout_otp_lock:{tenantId}:{sessionId}:{phone}";
}
