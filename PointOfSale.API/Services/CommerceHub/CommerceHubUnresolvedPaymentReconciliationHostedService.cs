namespace PointOfSale.API.Services.CommerceHub;

public sealed class CommerceHubUnresolvedPaymentReconciliationHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private const int MaxSweepPassesPerTick = 4;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CommerceHubUnresolvedPaymentReconciliationHostedService> _logger;

    public CommerceHubUnresolvedPaymentReconciliationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<CommerceHubUnresolvedPaymentReconciliationHostedService> logger)
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
                var job = scope.ServiceProvider.GetRequiredService<CommerceHubFinanceReconciliationJob>();
                var resolved = 0;

                for (var pass = 0; pass < MaxSweepPassesPerTick; pass++)
                {
                    var passResolved = await job.ResolveLinkedUnresolvedCapturedPaymentsAsync(stoppingToken);
                    if (passResolved == 0)
                    {
                        break;
                    }

                    resolved += passResolved;
                }

                if (resolved > 0)
                {
                    _logger.LogWarning(
                        "CommerceHub unresolved payment reconciliation matched {ResolvedCount} captured provider webhook record(s).",
                        resolved);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommerceHub unresolved payment reconciliation sweep failed.");
            }
        }
    }
}
