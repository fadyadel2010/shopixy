namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Background service that periodically cleans up expired storefront revoked tokens.
/// Tokens are kept in the blacklist until their natural expiration time,
/// after which they can be safely removed since they would be invalid anyway.
/// 
/// Also tracks table size metrics for monitoring and alerting.
/// </summary>
public class StorefrontRevokedTokenCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan MetricsInterval = TimeSpan.FromMinutes(5);
    
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StorefrontRevokedTokenCleanupService> _logger;

    public StorefrontRevokedTokenCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<StorefrontRevokedTokenCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[StorefrontTokenCleanup] Service started");

        try
        {
            // Wait a bit before first cleanup to let the app fully start
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            var cleanupTask = RunCleanupLoopAsync(stoppingToken);
            var metricsTask = RunMetricsLoopAsync(stoppingToken);

            await Task.WhenAll(cleanupTask, metricsTask);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown - swallow and proceed to finally
        }
        finally
        {
            _logger.LogInformation("[StorefrontTokenCleanup] Service stopped");
        }
    }

    private async Task RunCleanupLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredTokensAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[StorefrontTokenCleanup] Error during cleanup");
            }

            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunMetricsLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecordMetricsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[StorefrontTokenCleanup] Error recording metrics");
            }

            try
            {
                await Task.Delay(MetricsInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CleanupExpiredTokensAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var blacklistService = scope.ServiceProvider.GetRequiredService<IStorefrontJwtBlacklist>();

        _logger.LogDebug("[StorefrontTokenCleanup] Starting cleanup");
        var deletedCount = await blacklistService.CleanupExpiredTokensAsync(cancellationToken);
        
        if (deletedCount > 0)
        {
            _logger.LogInformation(
                "[StorefrontTokenCleanup] Cleanup completed | DeletedCount: {DeletedCount}",
                deletedCount);
        }
        else
        {
            _logger.LogDebug("[StorefrontTokenCleanup] No expired tokens to clean");
        }
    }

    private async Task RecordMetricsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var blacklistService = scope.ServiceProvider.GetRequiredService<IStorefrontJwtBlacklist>();

        var count = await blacklistService.GetRevokedTokenCountAsync(cancellationToken);

        // Log as a gauge metric
        _logger.LogInformation(
            "Metric: storefront_revoked_tokens_count={Count}",
            count);

        // Alert if table is growing too large
        if (count > 10000)
        {
            _logger.LogWarning(
                "ALERT: storefront_revoked_tokens_count={Count} exceeds threshold of 10000 - investigate cleanup job",
                count);
        }
    }
}
