using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Tenancy;

namespace PointOfSale.Infrastructure.MongoDB.Services;

public interface IProductLookupValidationCache
{
    Task<ProductDocument?> GetOrAddByIdAsync(
        Guid tenantId,
        Guid productId,
        Func<CancellationToken, Task<ProductDocument?>> loader,
        CancellationToken cancellationToken = default);

    Task<ProductDocument?> GetOrAddBySlugAsync(
        Guid tenantId,
        string slug,
        bool onlineVisibleOnly,
        Func<CancellationToken, Task<ProductDocument?>> loader,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Bounded per-replica cache for Mongo product identity/existence lookups.
/// Every hit is fenced by the distributed Storefront product generation, so
/// invalidation performed by any replica is observed before a local entry is served.
/// Mutable product authority is deliberately not cached.
/// </summary>
public sealed class ProductLookupValidationCache : IProductLookupValidationCache, IDisposable
{
    private sealed record ProductIdentity(bool Found, Guid TenantId, Guid ProductId);
    private sealed record CacheEntry(ProductIdentity Identity, long Generation);

    private const string CacheType = "mongo-product-lookup";
    private readonly MemoryCache _cache;
    private readonly IRedisJsonCacheService _distributedCache;
    private readonly string _instanceName;
    private readonly TimeSpan _positiveTtl;
    private readonly TimeSpan _negativeTtl;
    private readonly ILogger<ProductLookupValidationCache> _logger;

    public ProductLookupValidationCache(
        IRedisJsonCacheService distributedCache,
        IOptions<RedisSettings> redisSettings,
        IOptions<ProductLookupValidationCacheOptions> options,
        ILogger<ProductLookupValidationCache> logger)
    {
        _distributedCache = distributedCache;
        _instanceName = redisSettings.Value.InstanceName;
        _logger = logger;

        var settings = options.Value;
        _positiveTtl = TimeSpan.FromSeconds(settings.PositiveTtlSeconds);
        _negativeTtl = TimeSpan.FromSeconds(settings.NegativeTtlSeconds);
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = settings.MaxEntries });
    }

    public Task<ProductDocument?> GetOrAddByIdAsync(
        Guid tenantId,
        Guid productId,
        Func<CancellationToken, Task<ProductDocument?>> loader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);
        if (tenantId == Guid.Empty || productId == Guid.Empty)
        {
            return loader(cancellationToken);
        }

        return GetOrAddAsync(
            BuildIdKey(tenantId, productId),
            tenantId,
            productId,
            requestedSlug: null,
            loader,
            cancellationToken);
    }

    public Task<ProductDocument?> GetOrAddBySlugAsync(
        Guid tenantId,
        string slug,
        bool onlineVisibleOnly,
        Func<CancellationToken, Task<ProductDocument?>> loader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);
        if (tenantId == Guid.Empty || string.IsNullOrEmpty(slug))
        {
            return loader(cancellationToken);
        }

        return GetOrAddAsync(
            BuildSlugKey(tenantId, slug, onlineVisibleOnly),
            tenantId,
            expectedProductId: null,
            requestedSlug: slug,
            loader,
            cancellationToken);
    }

    public void Dispose() => _cache.Dispose();

    private Task<ProductDocument?> GetOrAddAsync(
        string key,
        Guid tenantId,
        Guid? expectedProductId,
        string? requestedSlug,
        Func<CancellationToken, Task<ProductDocument?>> loader,
        CancellationToken cancellationToken)
    {
        return KeyedSingleFlight.GetOrAddAsync<ProductDocument?>(
            $"{CacheType}:{key}",
            ct => TryReadAsync(key, tenantId, requestedSlug, ct),
            ct => LoadAndStoreAsync(
                key,
                tenantId,
                expectedProductId,
                requestedSlug,
                loader,
                ct),
            cancellationToken);
    }

    private async Task<(bool Hit, ProductDocument? Value)> TryReadAsync(
        string key,
        Guid tenantId,
        string? requestedSlug,
        CancellationToken cancellationToken)
    {
        try
        {
            var generation = await TryReadGenerationAsync(tenantId, cancellationToken).ConfigureAwait(false);
            if (!generation.HasValue ||
                !_cache.TryGetValue(key, out CacheEntry? entry) ||
                entry is null ||
                entry.Generation != generation.Value)
            {
                return (false, null);
            }

            return (true, ToDocument(entry.Identity, requestedSlug));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Product lookup cache read failed; falling back to Mongo | TenantId: {TenantId}",
                tenantId);
            return (false, null);
        }
    }

    private async Task<ProductDocument?> LoadAndStoreAsync(
        string key,
        Guid tenantId,
        Guid? expectedProductId,
        string? requestedSlug,
        Func<CancellationToken, Task<ProductDocument?>> loader,
        CancellationToken cancellationToken)
    {
        var generationBeforeLoad = await TryReadGenerationAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var loaded = await loader(cancellationToken).ConfigureAwait(false);

        if (!generationBeforeLoad.HasValue)
        {
            return loaded;
        }

        ProductIdentity identity;
        if (loaded is null)
        {
            identity = new ProductIdentity(false, tenantId, Guid.Empty);
        }
        else
        {
            if (loaded.TenantId != tenantId ||
                (expectedProductId.HasValue && loaded.ProductId != expectedProductId.Value) ||
                loaded.ProductId == Guid.Empty)
            {
                return loaded;
            }

            identity = new ProductIdentity(true, tenantId, loaded.ProductId);
        }

        try
        {
            var generationAfterLoad = await TryReadGenerationAsync(tenantId, cancellationToken).ConfigureAwait(false);
            if (generationAfterLoad == generationBeforeLoad)
            {
                _cache.Set(
                    key,
                    new CacheEntry(identity, generationBeforeLoad.Value),
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = identity.Found ? _positiveTtl : _negativeTtl,
                        Priority = CacheItemPriority.Low,
                        Size = 1
                    });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Product lookup cache write failed; continuing uncached | TenantId: {TenantId}",
                tenantId);
        }

        return ToDocument(identity, requestedSlug);
    }

    private async Task<long?> TryReadGenerationAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _distributedCache.GetInvalidationGenerationAsync(
                CacheKeyBuilder.StorefrontProductsInvalidationGenerationKey(_instanceName, tenantId),
                CacheType,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Product lookup generation fence unavailable; bypassing cache | TenantId: {TenantId}",
                tenantId);
            return null;
        }
    }

    private static ProductDocument? ToDocument(ProductIdentity identity, string? requestedSlug)
    {
        if (!identity.Found)
        {
            return null;
        }

        return new ProductDocument
        {
            TenantId = identity.TenantId,
            ProductId = identity.ProductId,
            Slug = requestedSlug
        };
    }

    internal static string BuildIdKey(Guid tenantId, Guid productId) =>
        $"t:{tenantId:N}:id:{productId:N}";

    internal static string BuildSlugKey(Guid tenantId, string slug, bool onlineVisibleOnly)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slug))).ToLowerInvariant();
        return $"t:{tenantId:N}:slug:{(onlineVisibleOnly ? "visible" : "all")}:{hash}";
    }
}
