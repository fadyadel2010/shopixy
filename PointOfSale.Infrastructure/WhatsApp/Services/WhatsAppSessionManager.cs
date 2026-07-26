using Microsoft.AspNetCore.DataProtection;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.WhatsApp.Services;

public class WhatsAppSessionManager : IWhatsAppSessionManager
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDataProtector _protector;
    
    // Constant for data protection purpose string
    private const string ProtectionPurpose = "WhatsAppSessionProtection";
    private const string RedisKeyPrefix = "whatsapp:session:";
    private static readonly TimeSpan SessionTtl = TimeSpan.FromDays(30);

    public WhatsAppSessionManager(
        IConnectionMultiplexer redis,
        IDataProtectionProvider protectionProvider)
    {
        _redis = redis;
        _protector = protectionProvider.CreateProtector(ProtectionPurpose);
    }
    
    public async Task SaveSessionAsync(Guid accountId, string sessionData, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sessionData))
        {
            throw new ArgumentException("Session data cannot be empty", nameof(sessionData));
        }

        var encrypted = _protector.Protect(sessionData);
        var key = $"{RedisKeyPrefix}{accountId}";
        var db = _redis.GetDatabase();
        
        await db.StringSetAsync(key, encrypted, SessionTtl);
    }
    
    public async Task<string?> GetSessionAsync(Guid accountId, CancellationToken ct = default)
    {
        var key = $"{RedisKeyPrefix}{accountId}";
        var db = _redis.GetDatabase();
        var encrypted = await db.StringGetAsync(key);
        
        if (!encrypted.HasValue || encrypted.IsNullOrEmpty) 
            return null;
            
        try
        {
            return _protector.Unprotect(encrypted.ToString());
        }
        catch
        {
            // If decryption fails (key rotation, corruption), treat as lost session
            return null;
        }
    }

    public async Task DeleteSessionAsync(Guid accountId, CancellationToken ct = default)
    {
        var key = $"{RedisKeyPrefix}{accountId}";
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(key);
    }

    public async Task TouchSessionAsync(Guid accountId, CancellationToken ct = default)
    {
        var key = $"{RedisKeyPrefix}{accountId}";
        var db = _redis.GetDatabase();
        await db.KeyExpireAsync(key, SessionTtl);
    }
}
