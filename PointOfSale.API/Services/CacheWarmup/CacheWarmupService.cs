using Microsoft.Extensions.Options;
using PointOfSale.API.Services.MarketplaceCms;
using PointOfSale.API.Services.MarketplaceFooter;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.CacheWarmup;

/// <summary>
/// Background service that warms critical cache entries on application startup.
/// 
/// PHASE: Redis Hardening — Cold Start Fix
/// 
/// ARCHITECTURAL RULES:
/// - Runs ONCE on startup after DI is fully built
/// - Non-blocking (does not delay request serving)
/// - Best-effort (failures are logged and swallowed)
/// - Does NOT warm tenant-specific caches (would require iterating all tenants)
/// - Warms only global/structural cache entries
/// 
/// This service reduces cold-start latency for the first requests after deployment.
/// </summary>
public sealed class CacheWarmupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RedisSettings _settings;
    private readonly ILogger<CacheWarmupService> _logger;

    /// <summary>
    /// Delay before warmup starts (allows other services to initialize).
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum time allowed for the entire warmup phase.
    /// </summary>
    private static readonly TimeSpan WarmupTimeout = TimeSpan.FromSeconds(60);

    public CacheWarmupService(
        IServiceScopeFactory scopeFactory,
        IOptions<RedisSettings> settings,
        ILogger<CacheWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("[CacheWarmup] Redis is disabled — skipping cache warmup");
            return;
        }

        try
        {
            // Wait for the application to fully start before warming caches.
            await Task.Delay(StartupDelay, stoppingToken);

            _logger.LogInformation("[CacheWarmup] Starting cache warmup...");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(WarmupTimeout);

            var warmedCount = 0;
            var failedCount = 0;

            var tasks = new List<(string Name, Func<CancellationToken, Task> Action)>
            {
                ("RedisConnection", WarmRedisConnectionAsync),
                ("MarketplaceFooter", WarmMarketplaceFooterAsync),
                ("MarketplaceCmsPages", WarmMarketplaceCmsPagesAsync)
            };

            foreach (var (name, action) in tasks)
            {
                try
                {
                    await action(timeoutCts.Token);
                    warmedCount++;
                    _logger.LogDebug("[CacheWarmup] Warmed: {CacheName}", name);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning("[CacheWarmup] Cancelled during {CacheName} warmup", name);
                    return;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogWarning(ex, "[CacheWarmup] Failed to warm: {CacheName}", name);
                }
            }

            _logger.LogInformation(
                "[CacheWarmup] Cache warmup completed | Warmed: {Warmed} | Failed: {Failed} | Total: {Total}",
                warmedCount, failedCount, tasks.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("[CacheWarmup] Cache warmup cancelled due to application shutdown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CacheWarmup] Cache warmup failed unexpectedly");
        }
    }

    private async Task WarmRedisConnectionAsync(CancellationToken cancellationToken)
    {
        // Resolve the connection factory to ensure the Redis connection is established.
        // This prevents the first real request from paying the connection setup cost.
        using var scope = _scopeFactory.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<RedisConnectionFactory>();
        var database = connectionFactory.GetDatabase();

        if (database != null)
        {
            await database.PingAsync();
        }
    }

    private async Task WarmMarketplaceFooterAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var cacheService = scope.ServiceProvider.GetService<IMarketplaceFooterCacheService>();
        if (cacheService != null)
        {
            await cacheService.GetAsync(cancellationToken);
        }
    }

    private async Task WarmMarketplaceCmsPagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var cacheService = scope.ServiceProvider.GetService<IMarketplaceCmsPageCacheService>();
        if (cacheService != null)
        {
            await cacheService.GetPublishedPagesAsync(cancellationToken);
        }
    }
}
