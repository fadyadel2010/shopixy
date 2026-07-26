using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.Redis.Configuration;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// Store config cache service implementation using Redis.
/// Phase 7.1: Theme & Store Config System
/// 
/// Key Features:
/// - Tenant-isolated cache keys
/// - 30-minute TTL
/// - Graceful failure handling (cache is optional)
/// - Structured logging for observability
/// - Uses string serialization to avoid DTO dependencies
/// </summary>
public class StoreConfigCacheService : IStoreConfigCacheService
{
    private readonly RedisConnectionFactory _connectionFactory;
    private readonly RedisSettings _settings;
    private readonly ILogger<StoreConfigCacheService> _logger;

    /// <summary>
    /// Default TTL for store config cache: 30 minutes.
    /// </summary>
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    public StoreConfigCacheService(
        RedisConnectionFactory connectionFactory,
        IOptions<RedisSettings> settings,
        ILogger<StoreConfigCacheService> logger)
    {
        _connectionFactory = connectionFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => _connectionFactory.IsEnabled && _connectionFactory.IsConnected;

    /// <inheritdoc />
    public async Task<string?> GetStoreConfigJsonAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            _logger.LogDebug(
                "StoreConfig Cache SKIP (no connection) | TenantId: {TenantId}",
                tenantId);
            return null;
        }

        var key = CacheKeyBuilder.StoreConfigKey(_settings.InstanceName, tenantId);

        try
        {
            var value = await database.StringGetAsync(key);

            if (value.IsNullOrEmpty)
            {
                _logger.LogDebug(
                    "StoreConfig Cache MISS | Key: {Key} | TenantId: {TenantId}",
                    key, tenantId);
                return null;
            }

            _logger.LogDebug(
                "StoreConfig Cache HIT | Key: {Key} | TenantId: {TenantId}",
                key, tenantId);

            return value!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "StoreConfig Cache GET failed | Key: {Key} | TenantId: {TenantId}",
                key, tenantId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetStoreConfigJsonAsync(
        Guid tenantId,
        string configJson,
        string themeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(configJson)) return false;

        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            _logger.LogDebug(
                "StoreConfig Cache SKIP (no connection) | TenantId: {TenantId}",
                tenantId);
            return false;
        }

        var key = CacheKeyBuilder.StoreConfigKey(_settings.InstanceName, tenantId);

        try
        {
            var success = await database.StringSetAsync(key, configJson, DefaultTtl);

            if (success)
            {
                _logger.LogDebug(
                    "StoreConfig Cache SET | Key: {Key} | TenantId: {TenantId} | ThemeId: {ThemeId} | TTL: {TtlMinutes}min",
                    key, tenantId, themeId, DefaultTtl.TotalMinutes);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "StoreConfig Cache SET failed | Key: {Key} | TenantId: {TenantId}",
                key, tenantId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> InvalidateStoreConfigAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            return true; // Nothing to invalidate if no connection
        }

        var key = CacheKeyBuilder.StoreConfigKey(_settings.InstanceName, tenantId);

        try
        {
            var deleted = await database.KeyDeleteAsync(key);

            _logger.LogInformation(
                "StoreConfig Cache INVALIDATE | Key: {Key} | TenantId: {TenantId} | Existed: {Existed}",
                key, tenantId, deleted);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "StoreConfig Cache INVALIDATE failed | Key: {Key} | TenantId: {TenantId}",
                key, tenantId);
            return false;
        }
    }
}
