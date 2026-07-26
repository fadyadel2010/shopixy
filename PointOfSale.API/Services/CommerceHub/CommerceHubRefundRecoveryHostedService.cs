namespace PointOfSale.API.Services.CommerceHub;

public sealed class CommerceHubRefundRecoveryHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CommerceHubRefundRecoveryHostedService> _logger;

    public CommerceHubRefundRecoveryHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<CommerceHubRefundRecoveryHostedService> logger)
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
                var refundService = scope.ServiceProvider.GetRequiredService<CommerceHubRefundService>();
                var refundExecutionRecovery = scope.ServiceProvider.GetRequiredService<IRefundExecutionRecoveryService>();
                var recovered = await refundService.ReconcileProcessingRefundsAsync(stoppingToken);
                var reconciled = await refundExecutionRecovery.RecoverReconciliationRequiredRefundsAsync(stoppingToken);

                if (recovered > 0)
                {
                    _logger.LogWarning(
                        "CommerceHub refund recovery reconciled {RecoveredCount} stuck processing refund(s).",
                        recovered);
                }

                if (reconciled > 0)
                {
                    _logger.LogWarning(
                        "CommerceHub refund recovery completed local recognition for {RecoveredCount} reconciliation-required manual refund(s).",
                        reconciled);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommerceHub refund recovery sweep failed.");
            }
        }
    }
}
