using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Models;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// Single owner for the Storefront shipping-method Redis entry. PostgreSQL remains
/// business truth and MongoDB remains the projection source used to populate it.
/// </summary>
public interface IShippingMethodCacheService
{
    Task<IReadOnlyList<ShippingMethodDocument>> GetOrCreateAsync(
        Guid tenantId,
        Func<CancellationToken, Task<IReadOnlyCollection<ShippingMethodDocument>>> factory,
        CancellationToken cancellationToken = default);

    Task InvalidateAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

public sealed class ShippingMethodCacheService : IShippingMethodCacheService
{
    private const string CacheType = "storefront-shipping-methods";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    private readonly IRedisJsonCacheService _cache;
    private readonly RedisSettings _settings;

    public ShippingMethodCacheService(
        IRedisJsonCacheService cache,
        IOptions<RedisSettings> settings)
    {
        _cache = cache;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<ShippingMethodDocument>> GetOrCreateAsync(
        Guid tenantId,
        Func<CancellationToken, Task<IReadOnlyCollection<ShippingMethodDocument>>> factory,
        CancellationToken cancellationToken = default)
    {
        var value = await _cache.GetOrCreateOwnedAsync(
            GetKey(tenantId),
            CacheType,
            GetGenerationKey(tenantId),
            CacheTtl,
            async ct =>
            {
                var documents = await factory(ct);
                if (documents.Count == 0)
                {
                    return CachePopulationResult<List<ShippingMethodDocument>>.FromValue(
                        [],
                        skipCache: true);
                }

                var latest = documents
                    .OrderByDescending(document => document.SyncedAtUtc)
                    .First();

                return CachePopulationResult<List<ShippingMethodDocument>>.FromValue(
                    documents.ToList(),
                    latest.SyncedAtUtc,
                    latest.LastEventOccurredAtUtc,
                    skipCache: !latest.LastEventOccurredAtUtc.HasValue);
            },
            TimeSpan.FromSeconds(_settings.MaxProjectionLagSeconds),
            cancellationToken: cancellationToken);

        return value ?? [];
    }

    public Task InvalidateAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => InvalidateCoreAsync(tenantId, cancellationToken);

    private async Task InvalidateCoreAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await _cache.AdvanceInvalidationGenerationAsync(GetGenerationKey(tenantId), cancellationToken);
        await _cache.RemoveAsync(GetKey(tenantId), cancellationToken);
    }

    private string GetKey(Guid tenantId)
        => CacheKeyBuilder.ShippingMethodsKey(_settings.InstanceName, tenantId);

    private string GetGenerationKey(Guid tenantId)
        => CacheKeyBuilder.ShippingMethodsInvalidationGenerationKey(_settings.InstanceName, tenantId);
}
