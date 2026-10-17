using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PointOfSale.Infrastructure.Redis.Services;
using StackExchange.Redis;

namespace PointOfSale.API.Services.Storefront;

public record SocialLoginChallenge(string State, string Nonce);
public interface ISocialLoginReplayStore
{
    Task<bool> ConsumeAsync(string key, TimeSpan lifetime);
}
public sealed class SocialLoginReplayStore(RedisConnectionFactory redis) : ISocialLoginReplayStore
{
    public async Task<bool> ConsumeAsync(string key, TimeSpan lifetime)
    {
        var db = redis.GetDatabase() ?? throw new InvalidOperationException("Login replay protection unavailable");
        return await db.StringSetAsync(key, "used", lifetime, When.NotExists);
    }
}

// SDK credential completion, not an authorization-code redirect flow. Protected correlation
// cookie + posted state bind the result to this browser, tenant and provider. Google also
// signs the challenge nonce into its ID token. Redis consumption is atomic and fail-closed.
public sealed class SocialLoginChallengeService(IDataProtectionProvider protection, ISocialLoginReplayStore replay)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Storefront.SocialLogin.Challenge.v1");
    private sealed record Payload(Guid Tenant, string Provider, string State, string Nonce, DateTime Expires);
    private static string Cookie(string provider) => "sf_social_" + provider;
    private static CookieOptions Options(HttpContext context) => new() { HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax, Path = "/api/storefront/auth", MaxAge = TimeSpan.FromMinutes(5), IsEssential = true };

    public SocialLoginChallenge Issue(HttpContext context, Guid tenant, string provider)
    {
        var challenge = new SocialLoginChallenge(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        var payload = new Payload(tenant, provider, challenge.State, challenge.Nonce, DateTime.UtcNow.AddMinutes(5));
        context.Response.Cookies.Append(Cookie(provider), _protector.Protect(JsonSerializer.Serialize(payload)), Options(context));
        context.Response.Headers.CacheControl = "no-store";
        return challenge;
    }

    public async Task<string?> ConsumeAsync(HttpContext context, Guid tenant, string provider, string? state)
    {
        if (string.IsNullOrEmpty(state) || state.Length != 64 || !context.Request.Cookies.TryGetValue(Cookie(provider), out var cookie)) return null;
        Payload? payload;
        try { payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(cookie)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { return null; }
        if (payload == null || payload.Tenant != tenant || payload.Provider != provider || payload.Expires <= DateTime.UtcNow ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(payload.State), Encoding.UTF8.GetBytes(state))) return null;
        var key = "sf:social:used:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cookie)));
        if (!await replay.ConsumeAsync(key, TimeSpan.FromMinutes(6))) return null;
        context.Response.Cookies.Delete(Cookie(provider), Options(context));
        return payload.Nonce;
    }
}
