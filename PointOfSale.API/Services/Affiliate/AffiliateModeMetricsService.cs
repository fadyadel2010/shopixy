using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.Affiliate;

/// <summary>
/// AF-12: Affiliate Mode Metrics Service Implementation.
/// 
/// Tracks realtime metrics for Affiliate Mode operations using Redis counters.
/// All counters use hourly time windows for aggregation.
/// </summary>
public class AffiliateModeMetricsService : IAffiliateModeMetricsService
{
    private readonly IAnalyticsHotStoreService _hotStore;
    private readonly ILogger<AffiliateModeMetricsService> _logger;

    // Counter names
    private const string AffiliateModeRequestsCounter = "affiliate_mode_requests";
    private const string AffiliateModeBlockedProductsCounter = "affiliate_mode_blocked_products";
    private const string AffiliateModeActivationsCounter = "affiliate_mode_activations";

    public AffiliateModeMetricsService(
        IAnalyticsHotStoreService hotStore,
        ILogger<AffiliateModeMetricsService> logger)
    {
        _hotStore = hotStore;
        _logger = logger;
    }

    public async Task RecordAffiliateModeRequestAsync(
        Guid tenantId,
        Guid affiliateAccountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var timeWindow = DateTime.UtcNow;
            await _hotStore.IncrementCounterAsync(
                tenantId,
                AffiliateModeRequestsCounter,
                timeWindow,
                increment: 1,
                cancellationToken);

            _logger.LogDebug(
                "[AffiliateMode] Request recorded | TenantId: {TenantId} | AffiliateAccountId: {AffiliateAccountId}",
                tenantId, affiliateAccountId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AffiliateMode] Failed to record request metric | TenantId: {TenantId} | AffiliateAccountId: {AffiliateAccountId}",
                tenantId, affiliateAccountId);
        }
    }

    public async Task RecordBlockedProductAccessAsync(
        Guid tenantId,
        Guid affiliateAccountId,
        Guid productId,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var timeWindow = DateTime.UtcNow;
            await _hotStore.IncrementCounterAsync(
                tenantId,
                AffiliateModeBlockedProductsCounter,
                timeWindow,
                increment: 1,
                cancellationToken);

            _logger.LogInformation(
                "[AffiliateMode] ProductBlocked | TenantId: {TenantId} | AffiliateAccountId: {AffiliateAccountId} | ProductId: {ProductId} | Endpoint: {Endpoint}",
                tenantId, affiliateAccountId, productId, endpoint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AffiliateMode] Failed to record blocked product metric | TenantId: {TenantId} | ProductId: {ProductId}",
                tenantId, productId);
        }
    }

    public async Task RecordAffiliateModeActivationAsync(
        Guid tenantId,
        Guid affiliateAccountId,
        string referralCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var timeWindow = DateTime.UtcNow;
            await _hotStore.IncrementCounterAsync(
                tenantId,
                AffiliateModeActivationsCounter,
                timeWindow,
                increment: 1,
                cancellationToken);

            _logger.LogInformation(
                "[AffiliateMode] Activated | TenantId: {TenantId} | AffiliateAccountId: {AffiliateAccountId} | ReferralCode: {ReferralCode}",
                tenantId, affiliateAccountId, referralCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AffiliateMode] Failed to record activation metric | TenantId: {TenantId} | ReferralCode: {ReferralCode}",
                tenantId, referralCode);
        }
    }

    public async Task<long> GetAffiliateModeRequestCountAsync(
        Guid tenantId,
        int windowMinutes = 60,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var hoursToCheck = (int)Math.Ceiling(windowMinutes / 60.0);
            long totalCount = 0;

            for (int i = 0; i < hoursToCheck; i++)
            {
                var timeWindow = now.AddHours(-i);
                var count = await _hotStore.IncrementCounterAsync(
                    tenantId,
                    AffiliateModeRequestsCounter,
                    timeWindow,
                    increment: 0, // Read-only
                    cancellationToken);
                totalCount += count;
            }

            return totalCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AffiliateMode] Failed to get request count | TenantId: {TenantId}",
                tenantId);
            return 0;
        }
    }

    public async Task<long> GetBlockedProductCountAsync(
        Guid tenantId,
        int windowMinutes = 60,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var hoursToCheck = (int)Math.Ceiling(windowMinutes / 60.0);
            long totalCount = 0;

            for (int i = 0; i < hoursToCheck; i++)
            {
                var timeWindow = now.AddHours(-i);
                var count = await _hotStore.IncrementCounterAsync(
                    tenantId,
                    AffiliateModeBlockedProductsCounter,
                    timeWindow,
                    increment: 0, // Read-only
                    cancellationToken);
                totalCount += count;
            }

            return totalCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AffiliateMode] Failed to get blocked product count | TenantId: {TenantId}",
                tenantId);
            return 0;
        }
    }
}
