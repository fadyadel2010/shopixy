using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Redis.Configuration;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// Redis cache service for promotion ranking configuration.
/// Phase: Shopify-Grade Promotion-Aware Search
/// 
/// Design:
/// - 5-minute TTL for quick config updates
/// - Fallback to defaults if Redis unavailable
/// - Tenant-isolated cache keys
/// </summary>
public class PromotionRankingCacheService : IPromotionRankingCacheService
{
    private readonly RedisConnectionFactory _connectionFactory;
    private readonly RedisSettings _settings;
    private readonly ILogger<PromotionRankingCacheService> _logger;

    /// <summary>
    /// Cache TTL: 5 minutes.
    /// Short TTL ensures config changes reflect quickly while still reducing DB load.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public PromotionRankingCacheService(
        RedisConnectionFactory connectionFactory,
        IOptions<RedisSettings> settings,
        ILogger<PromotionRankingCacheService> logger)
    {
        _connectionFactory = connectionFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PromotionRankingConfig?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            _logger.LogDebug(
                "PromotionRanking Cache SKIP (no connection) | TenantId: {TenantId}",
                tenantId);
            return null;
        }

        var key = BuildCacheKey(tenantId);

        try
        {
            var value = await database.StringGetAsync(key);

            if (value.IsNullOrEmpty)
            {
                _logger.LogDebug(
                    "PromotionRanking Cache MISS | Key: {Key} | TenantId: {TenantId}",
                    key, tenantId);
                return null;
            }

            var config = JsonSerializer.Deserialize<PromotionRankingConfig>(value!);

            _logger.LogDebug(
                "PromotionRanking Cache HIT | Key: {Key} | TenantId: {TenantId} | Enabled: {Enabled}",
                key, tenantId, config?.Enabled);

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PromotionRanking Cache GET failed | Key: {Key} | TenantId: {TenantId}",
                key, tenantId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(Guid tenantId, PromotionRankingConfig config, CancellationToken cancellationToken = default)
    {
        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            _logger.LogDebug(
                "PromotionRanking Cache SKIP (no connection) | TenantId: {TenantId}",
                tenantId);
            return;
        }

        var key = BuildCacheKey(tenantId);

        try
        {
            var json = JsonSerializer.Serialize(config);
            await database.StringSetAsync(key, json, CacheTtl);

            _logger.LogDebug(
                "PromotionRanking Cache SET | Key: {Key} | TenantId: {TenantId} | Enabled: {Enabled} | TTL: {TtlMin}min",
                key, tenantId, config.Enabled, CacheTtl.TotalMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PromotionRanking Cache SET failed | Key: {Key} | TenantId: {TenantId}",
                key, tenantId);
        }
    }

    /// <inheritdoc />
    public async Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var database = _connectionFactory.GetDatabase();
        if (database == null) return;

        var key = BuildCacheKey(tenantId);

        try
        {
            var deleted = await database.KeyDeleteAsync(key);

            _logger.LogInformation(
                "PromotionRanking Cache INVALIDATE | Key: {Key} | TenantId: {TenantId} | Existed: {Existed}",
                key, tenantId, deleted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PromotionRanking Cache INVALIDATE failed | Key: {Key} | TenantId: {TenantId}",
                key, tenantId);
        }
    }

    private string BuildCacheKey(Guid tenantId)
    {
        return $"{_settings.InstanceName}:promotion-ranking:{tenantId:N}";
    }
}
