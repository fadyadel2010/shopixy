using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Implementation of promotion ranking service.
/// Phase: Shopify-Grade Promotion-Aware Search
/// 
/// Reads the current SQL StoreConfig authority and fails closed when unavailable.
/// </summary>
public class PromotionRankingService : IPromotionRankingService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<PromotionRankingService> _logger;

    public PromotionRankingService(
        ApplicationDbContext dbContext,
        ILogger<PromotionRankingService> logger)
    {
        _dbContext = dbContext;
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
