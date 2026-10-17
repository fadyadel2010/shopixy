using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace PointOfSale.Infrastructure.Shipping;

public sealed record BostaPickupVerification(BostaPickupLocationOption Location, DateTime VerifiedAtUtc, DateTime ValidUntilUtc);

// Readiness evidence only, never shipment/origin authority. No provider HTTP or
// persistent configuration writes occur here, including inside create claims.
// Restart/eviction/expiry fail closed; explicit authenticated discovery refreshes.
public sealed class BostaPickupVerificationCache : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 2048 });
    private readonly TimeProvider _clock;
    public BostaPickupVerificationCache(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    private static string Key(Guid tenant, Guid configuration, Guid account, Guid? credentialReference, CarrierCredentials credentials) =>
        $"bosta-pickup-v1:{tenant:N}:{configuration:N}:{account:N}:{credentialReference:N}:" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CarrierOriginalAccountIdentity.Serialize(credentials))));

    public static bool SameConfiguration(CarrierCredentialsResolution before, CarrierCredentialsResolution after) =>
        before.Configuration != null && after.Succeeded && after.Configuration != null && before.Credentials != null && after.Credentials != null &&
        before.CarrierCode == after.CarrierCode && before.Configuration.Id == after.Configuration.Id &&
        Key(Guid.Empty, before.Configuration.Id, Guid.Empty, null, before.Credentials) == Key(Guid.Empty, after.Configuration.Id, Guid.Empty, null, after.Credentials);

    public BostaPickupVerification? Read(Guid tenant, Guid configuration, Guid account, Guid? credentialReference, CarrierCredentials credentials)
    {
        var key = Key(tenant, configuration, account, credentialReference, credentials);
        var evidence = _cache.Get<BostaPickupVerification>(key);
        var now = _clock.GetUtcNow().UtcDateTime;
        return evidence != null && evidence.VerifiedAtUtc <= now && evidence.ValidUntilUtc > now ? evidence : null;
    }

    public BostaPickupVerification? Observe(Guid tenant, Guid configuration, Guid account, Guid? credentialReference,
        CarrierCredentials credentials, BostaPickupLocationResult result)
    {
        var key = Key(tenant, configuration, account, credentialReference, credentials);
        var selection = BostaPickupSelections.Resolve(credentials, result);
        if (selection.Location == null) { _cache.Remove(key); return null; }
        var now = _clock.GetUtcNow().UtcDateTime;
        var evidence = new BostaPickupVerification(selection.Location, now, now.AddMinutes(10));
        _cache.Set(key, evidence, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = 1 });
        return evidence;
    }

    public void Dispose() => _cache.Dispose();
}
