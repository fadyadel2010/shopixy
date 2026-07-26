using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Implementation of promotion ranking service.
/// Phase: Shopify-Grade Promotion-Aware Search
/// 
/// Cache Flow:
/// 1. Check Redis cache
/// 2. On miss → Query MongoDB StoreConfig
/// 3. Populate cache on successful DB hit
/// 4. On all failures → Return default config
/// 
/// Performance:
/// - Redis cache hit: ~1ms
/// - Cache miss + DB: ~10-50ms
/// - Default fallback: 0ms (no I/O)
/// </summary>
public class PromotionRankingService : IPromotionRankingService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPromotionRankingCacheService _cacheService;
    private readonly ILogger<PromotionRankingService> _logger;

    public PromotionRankingService(
        ApplicationDbContext dbContext,
        IPromotionRankingCacheService cacheService,
        ILogger<PromotionRankingService> logger)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PromotionRankingConfig> GetConfigAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await _dbContext.StoreConfigs
                .AsNoTracking()
                .Where(storeConfig => storeConfig.TenantId == tenantId)
                .Select(storeConfig => storeConfig.PromotionRanking)
                .SingleOrDefaultAsync(cancellationToken);

            if (config != null)
            {
                await _cacheService.SetAsync(tenantId, config, cancellationToken);

                _logger.LogDebug(
                    "PromotionRanking config from SQL authority | TenantId: {TenantId} | Enabled: {Enabled} | BaseBoost: {BaseBoost}",
                    tenantId, config.Enabled, config.BaseBoost);

                return config;
            }

            _logger.LogWarning(
                "PromotionRanking SQL authority missing; feature fails closed | TenantId: {TenantId}",
                tenantId);
            return new PromotionRankingConfig { Enabled = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to fetch PromotionRanking config from SQL authority | TenantId: {TenantId}. Feature fails closed.",
                tenantId);
            return new PromotionRankingConfig { Enabled = false };
        }
    }
}
