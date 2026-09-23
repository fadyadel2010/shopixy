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
    /// - IRedisJsonCacheService (singleton - shared cache transport)
    /// - IStorefrontProductCacheInvalidator (singleton - product keyspace invalidation owner)
    /// - IShippingMethodCacheService (singleton - shipping cache owner)
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

        // Fail at host startup, before any authority/lock operation can silently
        // run without its configured shared Redis endpoint.
        services.AddOptions<RedisSettings>()
            .Bind(configuration.GetSection(RedisSettings.SectionName))
            .Validate(
                settings => !settings.Enabled || !string.IsNullOrWhiteSpace(settings.ConnectionString),
                "Redis:ConnectionString is required when Redis:Enabled=true.")
            .ValidateOnStart();

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
        services.AddSingleton<IDistributedLockService>(sp => new DistributedLockService(
            sp.GetRequiredService<RedisConnectionFactory>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DistributedLockService>>()));

        services.AddSingleton<RedisPerformanceTelemetry>();
        services.AddSingleton<IRedisJsonCacheService, RedisJsonCacheService>();
        services.AddSingleton<IStorefrontProductCacheInvalidator, StorefrontProductCacheInvalidator>();
        services.AddSingleton<IShippingMethodCacheService, ShippingMethodCacheService>();
        services.AddSingleton<IStorefrontWishlistCacheInvalidator, StorefrontWishlistCacheInvalidator>();

        // AF-27A/28: Register Redis-backed funnel rate limiter (horizontal-scaling safe)
        services.AddSingleton<IFunnelRateLimiter, RedisFunnelRateLimiter>();
        services.AddSingleton<IFunnelFraudSignalStore, FunnelFraudSignalStore>();

        return services;
    }
}
