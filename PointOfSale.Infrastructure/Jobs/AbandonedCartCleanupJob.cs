using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Infrastructure.Jobs;

/// <summary>
/// Background job that expires old triggered carts.
/// Phase: Abandoned Cart Hardening
/// 
/// RUNS: Daily via Hangfire recurring job
/// 
/// ACTIONS:
/// 1. Expire carts that were triggered more than 7 days ago without recovery
/// 2. Clean up old abandoned cart records (retention policy)
/// </summary>
public class AbandonedCartCleanupJob
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AbandonedCartCleanupJob> _logger;
    
    private const int ExpirationDays = 7;

    public AbandonedCartCleanupJob(
        IServiceProvider serviceProvider,
        ILogger<AbandonedCartCleanupJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Main execution method called by Hangfire.
    /// </summary>
    public async Task ExecuteAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var cartService = scope.ServiceProvider.GetRequiredService<IAbandonedCartService>();

        try
        {
            _logger.LogInformation("[Abandoned Cart Cleanup] Starting cleanup job");

            var expiredCount = await cartService.ExpireOldTriggeredCartsAsync(ExpirationDays);

            _logger.LogInformation(
                "[Abandoned Cart Cleanup] Job completed | ExpiredCarts: {ExpiredCount}",
                expiredCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Abandoned Cart Cleanup] Job failed");
        }
    }
}
