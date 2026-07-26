using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Distributed;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Services;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Redis;

/// <summary>
/// Extension methods for registering Redis services in DI container.
/// </summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>
    /// Adds Redis caching services to the dependency injection container.
    /// 
    /// Registers:
    /// - RedisSettings (bound from configuration)
    /// - RedisConnectionFactory (singleton - connection reuse)
    /// - IDistributedLockService (singleton - distributed lock operations)
    /// - IProductCacheService (singleton - uses shared connection)
    /// - IStoreConfigCacheService (singleton - store config caching)
    /// - IPromotionCacheService (singleton - promotion cache invalidation)
    /// 
    /// Usage in Program.cs:
    /// builder.Services.AddRedisCache(builder.Configuration);
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisSettings = configuration.GetSection(RedisSettings.SectionName).Get<RedisSettings>() ?? new RedisSettings();

        // Bind Redis settings from configuration
        services.Configure<RedisSettings>(
            configuration.GetSection(RedisSettings.SectionName));

        if (redisSettings.Enabled && !string.IsNullOrWhiteSpace(redisSettings.ConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisSettings.ConnectionString;
                options.InstanceName = redisSettings.InstanceName;
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        // Explicitly construct the factory so MEDI never selects the test-only constructor that
        // depends on IConnectionMultiplexer, which is itself resolved from RedisConnectionFactory.
        services.AddSingleton(sp => new RedisConnectionFactory(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisSettings>>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RedisConnectionFactory>>()));

        // Register IConnectionMultiplexer for services that need direct Redis access (e.g., idempotency).
        // This uses factory pattern to get connection from RedisConnectionFactory.
        // IMPORTANT: returns null (not throw) when Redis is unavailable/disabled so that application
        // startup never fails because of a down or unresponsive Redis server. Consumers must handle a
        // null multiplexer gracefully (cache/dedup operations are then skipped).
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var factory = sp.GetRequiredService<RedisConnectionFactory>();
            return factory.Connection!; // null when Redis is disabled or unreachable
        });

        // Register distributed lock service as singleton (consumed by singleton RedisJsonCacheService)
        services.AddSingleton<IDistributedLockService, DistributedLockService>();

        // Register cache service as singleton (uses shared connection)
        services.AddSingleton<IProductCacheService, ProductCacheService>();
        services.AddSingleton<RedisPerformanceTelemetry>();
        services.AddSingleton<IRedisJsonCacheService, RedisJsonCacheService>();

        // Register store config cache service (Phase 7.1)
        services.AddSingleton<IStoreConfigCacheService, StoreConfigCacheService>();

        // Register promotion cache service (Promotions Engine)
        services.AddSingleton<IPromotionCacheService, PromotionCacheService>();

        // Register promotion ranking cache service (Promotion-Aware Search)
        services.AddSingleton<IPromotionRankingCacheService, PromotionRankingCacheService>();

        // AF-27A/28: Register Redis-backed funnel rate limiter (horizontal-scaling safe)
        services.AddSingleton<IFunnelRateLimiter, RedisFunnelRateLimiter>();

        return services;
    }
}
