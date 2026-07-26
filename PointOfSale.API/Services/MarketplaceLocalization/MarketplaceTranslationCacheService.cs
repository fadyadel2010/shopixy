using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Marketplace;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.MarketplaceTranslations;

public sealed class MarketplaceTranslatedTextCacheEntry
{
    public string Value { get; set; } = string.Empty;
    public bool IsAutoTranslated { get; set; }
}

public sealed class MarketplaceTranslatedStringListCacheEntry
{
    public List<string> Items { get; set; } = new();
    public bool IsAutoTranslated { get; set; }
}

public sealed class MarketplaceTranslatedFaqListCacheEntry
{
    public List<MarketplaceProductFaqReadDto> Items { get; set; } = new();
    public bool IsAutoTranslated { get; set; }
}

public sealed class MarketplaceCategoryTranslationCacheEntry
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public bool IsAutoTranslated { get; set; }
}

public interface IMarketplaceTranslationCacheService
{
    Task<MarketplaceCategoryTranslationCacheEntry?> GetCategoryAsync(
        Guid categoryId,
        string language,
        CancellationToken cancellationToken = default);

    Task SetCategoryAsync(
        Guid categoryId,
        string language,
        MarketplaceCategoryTranslationCacheEntry entry,
        CancellationToken cancellationToken = default);

    Task<MarketplaceTranslatedTextCacheEntry?> GetProductTextAsync(
        Guid productId,
        string field,
        string language,
        CancellationToken cancellationToken = default);

    Task SetProductTextAsync(
        Guid productId,
        string field,
        string language,
        MarketplaceTranslatedTextCacheEntry entry,
        CancellationToken cancellationToken = default);

    Task<MarketplaceTranslatedStringListCacheEntry?> GetProductStringListAsync(
        Guid productId,
        string field,
        string language,
        CancellationToken cancellationToken = default);

    Task SetProductStringListAsync(
        Guid productId,
        string field,
        string language,
        MarketplaceTranslatedStringListCacheEntry entry,
        CancellationToken cancellationToken = default);

    Task<MarketplaceTranslatedFaqListCacheEntry?> GetProductFaqsAsync(
        Guid productId,
        string language,
        CancellationToken cancellationToken = default);

    Task SetProductFaqsAsync(
        Guid productId,
        string language,
        MarketplaceTranslatedFaqListCacheEntry entry,
        CancellationToken cancellationToken = default);
}

public sealed class MarketplaceTranslationCacheService : IMarketplaceTranslationCacheService
{
    private readonly IRedisJsonCacheService _cache;
    private readonly RedisSettings _redisSettings;
    private readonly ILogger<MarketplaceTranslationCacheService> _logger;
    private readonly TimeSpan _ttl;

    public MarketplaceTranslationCacheService(
        IRedisJsonCacheService cache,
        IOptions<RedisSettings> redisSettings,
        ILogger<MarketplaceTranslationCacheService> logger)
    {
        _cache = cache;
        _redisSettings = redisSettings.Value;
        _logger = logger;
        _ttl = TimeSpan.FromHours(Math.Max(1, _redisSettings.MarketplaceTranslationCacheTtlHours));
    }

    public Task<MarketplaceCategoryTranslationCacheEntry?> GetCategoryAsync(
        Guid categoryId,
        string language,
        CancellationToken cancellationToken = default)
    {
        return GetAsync<MarketplaceCategoryTranslationCacheEntry>(
            CacheKeyBuilder.MarketplaceCategoryTranslationKey(_redisSettings.InstanceName, categoryId, language),
            "marketplace-category-translation",
            cancellationToken);
    }

    public Task SetCategoryAsync(
        Guid categoryId,
        string language,
        MarketplaceCategoryTranslationCacheEntry entry,
        CancellationToken cancellationToken = default)
    {
        return SetAsync(
            CacheKeyBuilder.MarketplaceCategoryTranslationKey(_redisSettings.InstanceName, categoryId, language),
            "marketplace-category-translation",
            entry,
            cancellationToken);
    }

    public Task<MarketplaceTranslatedTextCacheEntry?> GetProductTextAsync(
        Guid productId,
        string field,
        string language,
        CancellationToken cancellationToken = default)
    {
        return GetAsync<MarketplaceTranslatedTextCacheEntry>(
            CacheKeyBuilder.MarketplaceProductFieldTranslationKey(_redisSettings.InstanceName, productId, field, language),
            "marketplace-product-translation",
            cancellationToken);
    }

    public Task SetProductTextAsync(
        Guid productId,
        string field,
        string language,
        MarketplaceTranslatedTextCacheEntry entry,
        CancellationToken cancellationToken = default)
    {
        return SetAsync(
            CacheKeyBuilder.MarketplaceProductFieldTranslationKey(_redisSettings.InstanceName, productId, field, language),
            "marketplace-product-translation",
            entry,
            cancellationToken);
    }

    public Task<MarketplaceTranslatedStringListCacheEntry?> GetProductStringListAsync(
        Guid productId,
        string field,
        string language,
        CancellationToken cancellationToken = default)
    {
        return GetAsync<MarketplaceTranslatedStringListCacheEntry>(
            CacheKeyBuilder.MarketplaceProductFieldTranslationKey(_redisSettings.InstanceName, productId, field, language),
            "marketplace-product-translation",
            cancellationToken);
    }

    public Task SetProductStringListAsync(
        Guid productId,
        string field,
        string language,
        MarketplaceTranslatedStringListCacheEntry entry,
        CancellationToken cancellationToken = default)
    {
        return SetAsync(
            CacheKeyBuilder.MarketplaceProductFieldTranslationKey(_redisSettings.InstanceName, productId, field, language),
            "marketplace-product-translation",
            entry,
            cancellationToken);
    }

    public Task<MarketplaceTranslatedFaqListCacheEntry?> GetProductFaqsAsync(
        Guid productId,
        string language,
        CancellationToken cancellationToken = default)
    {
        return GetAsync<MarketplaceTranslatedFaqListCacheEntry>(
            CacheKeyBuilder.MarketplaceProductFieldTranslationKey(_redisSettings.InstanceName, productId, "faqs", language),
            "marketplace-product-translation",
            cancellationToken);
    }

    public Task SetProductFaqsAsync(
        Guid productId,
        string language,
        MarketplaceTranslatedFaqListCacheEntry entry,
        CancellationToken cancellationToken = default)
    {
        return SetAsync(
            CacheKeyBuilder.MarketplaceProductFieldTranslationKey(_redisSettings.InstanceName, productId, "faqs", language),
            "marketplace-product-translation",
            entry,
            cancellationToken);
    }

    private async Task<T?> GetAsync<T>(
        string cacheKey,
        string cacheType,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await _cache.GetAsync<T>(cacheKey, cacheType, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Marketplace translation cache read failed | CacheKey: {CacheKey}", cacheKey);
            return null;
        }
    }

    private async Task SetAsync<T>(
        string cacheKey,
        string cacheType,
        T entry,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            await _cache.SetAsync(cacheKey, cacheType, entry, _ttl, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Marketplace translation cache write failed | CacheKey: {CacheKey}", cacheKey);
        }
    }
}
