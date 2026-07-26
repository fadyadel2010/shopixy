using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PointOfSale.Infrastructure.Services.Affiliate;

public sealed class AffiliateAttributionReconciliationHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AffiliateAttributionReconciliationHostedService> _logger;

    public AffiliateAttributionReconciliationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<AffiliateAttributionReconciliationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var affiliateService = scope.ServiceProvider.GetRequiredService<AffiliateService>();
                var repaired = await affiliateService.ReconcileMissingAttributionsAsync(stoppingToken);

                if (repaired > 0)
                {
                    _logger.LogWarning(
                        "Affiliate attribution reconciliation repaired {RepairedCount} missing attribution row(s).",
                        repaired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Affiliate attribution reconciliation sweep failed.");
            }
        }
    }
}
