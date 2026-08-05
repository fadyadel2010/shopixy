namespace PointOfSale.Infrastructure.Redis.Configuration;

/// <summary>
/// Configuration settings for Redis cache.
/// Should be populated from appsettings.json "Redis" section.
/// </summary>
public class RedisSettings
{
    /// <summary>
    /// The configuration section name in appsettings.json
    /// </summary>
    public const string SectionName = "Redis";

    /// <summary>
    /// Redis connection string (e.g., "192.168.131.128:6379" or "localhost:6379,password=xxx")
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Instance name prefix for all keys (helps identify source in shared Redis)
    /// </summary>
    public string InstanceName { get; set; } = "pos";

    /// <summary>
    /// Whether Redis caching is enabled. Set to false to disable cache entirely.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default TTL for individual product cache in minutes
    /// </summary>
    public int ProductCacheTtlMinutes { get; set; } = 10;

    /// <summary>
    /// Default TTL for product list cache in minutes
    /// </summary>
    public int ProductListCacheTtlMinutes { get; set; } = 5;

    /// <summary>
    /// Default TTL for homepage caches in minutes.
    /// </summary>
    public int HomepageCacheTtlMinutes { get; set; } = 5;

    /// <summary>
    /// Default TTL for footer caches in minutes.
    /// </summary>
    public int FooterCacheTtlMinutes { get; set; } = 15;

    /// <summary>
    /// Default TTL for marketplace read API caches in minutes.
    /// </summary>
    public int MarketplaceReadCacheTtlMinutes { get; set; } = 5;

    /// <summary>
    /// Default TTL for marketplace translation cache entries in hours.
    /// </summary>
    public int MarketplaceTranslationCacheTtlHours { get; set; } = 24;

    /// <summary>
    /// TTL for short-lived cache refresh locks in seconds.
    /// </summary>
    public int CacheRefreshLockTtlSeconds { get; set; } = 15;

    /// <summary>
    /// How long to wait before re-checking a hot cache key when another node is refreshing it.
    /// </summary>
    public int CacheRefreshWaitMilliseconds { get; set; } = 120;

    /// <summary>
    /// Maximum acceptable lag between the source event timestamp and the read-model projection timestamp.
    /// Cache entries that exceed this lag are bypassed.
    /// </summary>
    public int MaxProjectionLagSeconds { get; set; } = 45;

    /// <summary>
    /// Connection timeout in milliseconds
    /// </summary>
    public int ConnectTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Sync timeout for operations in milliseconds
    /// </summary>
    public int SyncTimeoutMs { get; set; } = 1000;
}
