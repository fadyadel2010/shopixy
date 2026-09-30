using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Interfaces.WhatsApp;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.WhatsApp.Services;

/// <summary>
/// WhatsApp OTP Service implementation using Redis for storage.
/// Generates cryptographically secure OTP codes and manages validation attempts.
/// </summary>
public class WhatsAppOtpService : IWhatsAppOtpService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<WhatsAppOtpService> _logger;
    
    private const string KeyPrefix = "whatsapp:otp:";
    private const int MaxAttempts = 3;
    private const int OtpLength = 6;
    private const int OtpHashIterations = 100_000;

    public WhatsAppOtpService(
        IConnectionMultiplexer redis,
        ILogger<WhatsAppOtpService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<string> GenerateOtpAsync(
        Guid tenantId, 
        string phone, 
        string purpose, 
        int expiryMinutes = 5,
        CancellationToken ct = default)
    {
        // Generate cryptographically secure 6-digit OTP
        var code = GenerateSecureOtp();
        
        var key = BuildKey(tenantId, phone, purpose);
        var data = new OtpData
        {
            Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            Attempts = 0,
            MaxAttempts = MaxAttempts,
            CreatedAtUtc = DateTime.UtcNow
        };
        data.CodeHash = HashOtp(tenantId, phone, purpose, code, data.Salt);
        
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(data);
        
        await db.StringSetAsync(key, json, TimeSpan.FromMinutes(expiryMinutes));
        
        _logger.LogInformation(
            "OTP generated | TenantId: {TenantId} | Phone: {Phone} | Purpose: {Purpose} | ExpiryMinutes: {ExpiryMinutes}",
            tenantId, MaskPhone(phone), purpose, expiryMinutes);
        
        return code;
    }

    public async Task<OtpValidationResult> ValidateOtpAsync(
        Guid tenantId, 
        string phone, 
        string purpose, 
        string code,
        CancellationToken ct = default)
    {
        var key = BuildKey(tenantId, phone, purpose);
        var db = _redis.GetDatabase();
        
        var json = await db.StringGetAsync(key);
        if (!json.HasValue || json.IsNullOrEmpty)
        {
            _logger.LogWarning(
                "OTP validation failed - not found or expired | TenantId: {TenantId} | Phone: {Phone} | Purpose: {Purpose}",
                tenantId, MaskPhone(phone), purpose);
            return new OtpValidationResult(false, "OTP expired or not found", 0);
        }
        
        var data = JsonSerializer.Deserialize<OtpData>(json.ToString());
        if (data == null)
        {
            return new OtpValidationResult(false, "Invalid OTP data", 0);
        }
        
        // Check max attempts
        if (data.Attempts >= data.MaxAttempts)
        {
            await db.KeyDeleteAsync(key);
            _logger.LogWarning(
                "OTP validation failed - max attempts exceeded | TenantId: {TenantId} | Phone: {Phone} | Purpose: {Purpose}",
                tenantId, MaskPhone(phone), purpose);
            return new OtpValidationResult(false, "Max attempts exceeded", 0);
        }
        
        bool hashMatches;
        try
        {
            var submittedHash = HashOtp(tenantId, phone, purpose, code, data.Salt);
            hashMatches = !string.IsNullOrWhiteSpace(data.CodeHash) &&
                CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(submittedHash),
                    Convert.FromBase64String(data.CodeHash));
        }
        catch (FormatException)
        {
            await db.KeyDeleteAsync(key);
            return new OtpValidationResult(false, "Invalid OTP data", 0);
        }

        if (!hashMatches)
        {
            // Increment attempts
            data.Attempts++;
            var updatedJson = JsonSerializer.Serialize(data);
            
            // Get remaining TTL and preserve it
            var ttl = await db.KeyTimeToLiveAsync(key);
            if (ttl.HasValue && ttl.Value.TotalSeconds > 0)
            {
                await db.StringSetAsync(key, updatedJson, ttl);
            }
            
            var remaining = data.MaxAttempts - data.Attempts;
            _logger.LogInformation(
                "OTP validation failed - wrong code | TenantId: {TenantId} | Phone: {Phone} | Purpose: {Purpose} | AttemptsRemaining: {AttemptsRemaining}",
                tenantId, MaskPhone(phone), purpose, remaining);
            
            return new OtpValidationResult(false, "Invalid code", remaining);
        }
        
        // Valid - delete key
        await db.KeyDeleteAsync(key);
        
        _logger.LogInformation(
            "OTP validated successfully | TenantId: {TenantId} | Phone: {Phone} | Purpose: {Purpose}",
            tenantId, MaskPhone(phone), purpose);
        
        return new OtpValidationResult(true, null, 0);
    }

    public async Task InvalidateOtpAsync(
        Guid tenantId, 
        string phone, 
        string purpose,
        CancellationToken ct = default)
    {
        var key = BuildKey(tenantId, phone, purpose);
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(key);
        
        _logger.LogInformation(
            "OTP invalidated | TenantId: {TenantId} | Phone: {Phone} | Purpose: {Purpose}",
            tenantId, MaskPhone(phone), purpose);
    }

    private static string BuildKey(Guid tenantId, string phone, string purpose)
    {
        // Normalize phone (remove spaces, +, etc.)
        var cleanPhone = phone.Replace("+", "").Replace(" ", "").Trim();
        return $"{KeyPrefix}{tenantId:N}:{cleanPhone}:{purpose}";
    }

    private static string GenerateSecureOtp()
    {
        // Generate cryptographically secure 6-digit code
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[4];
        rng.GetBytes(bytes);
        var value = BitConverter.ToUInt32(bytes, 0) % 1000000;
        return value.ToString("D6");
    }

    private static string HashOtp(Guid tenantId, string phone, string purpose, string code, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var value = System.Text.Encoding.UTF8.GetBytes($"{tenantId:N}\n{phone}\n{purpose}\n{code}");
        return Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(
            value,
            saltBytes,
            OtpHashIterations,
            HashAlgorithmName.SHA256,
            32));
    }

    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 7)
            return "***";
        
        return phone.Substring(0, 4) + "****" + phone.Substring(phone.Length - 3);
    }

    private class OtpData
    {
        public string CodeHash { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public int MaxAttempts { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
