namespace PointOfSale.API.Services.CommerceHub;

/// <summary>
/// Background service that periodically cleans up expired revoked tokens.
/// Tokens are kept in the blacklist until their natural expiration time,
/// after which they can be safely removed since they would be invalid anyway.
/// </summary>
public class RevokedTokenCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RevokedTokenCleanupService> _logger;

    public RevokedTokenCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<RevokedTokenCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RevokedTokenCleanupService started");

        // Wait a bit before first cleanup to let the app fully start
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredTokensAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during revoked token cleanup");
            }

            await Task.Delay(CleanupInterval, stoppingToken);
        }

        _logger.LogInformation("RevokedTokenCleanupService stopped");
    }

    private async Task CleanupExpiredTokensAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var revocationService = scope.ServiceProvider.GetRequiredService<ICustomerTokenRevocationService>();

        _logger.LogDebug("Starting revoked token cleanup");
        await revocationService.CleanupExpiredTokensAsync(cancellationToken);
        _logger.LogDebug("Revoked token cleanup completed");
    }
}
