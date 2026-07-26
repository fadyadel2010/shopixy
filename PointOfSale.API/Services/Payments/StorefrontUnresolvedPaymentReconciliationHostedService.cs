namespace PointOfSale.API.Services.Payments;

public sealed class StorefrontUnresolvedPaymentReconciliationHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private const int MaxPassesPerCycle = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StorefrontUnresolvedPaymentReconciliationHostedService> _logger;

    public StorefrontUnresolvedPaymentReconciliationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<StorefrontUnresolvedPaymentReconciliationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[StorefrontPaymentRecovery] Storefront unresolved payment reconciliation service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunRecoveryCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[StorefrontPaymentRecovery] Reconciliation cycle failed unexpectedly");
            }
            finally
            {
                // Always delay after each cycle (success or failure) to avoid hammering dependencies
                try
                {
                    await Task.Delay(Interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation during delay is expected during shutdown
                }
            }
        }

        _logger.LogInformation("[StorefrontPaymentRecovery] Storefront unresolved payment reconciliation service stopped");
    }

    private async Task RunRecoveryCycleAsync(CancellationToken cancellationToken)
    {
        var totalReconciled = 0;

        for (var pass = 0; pass < MaxPassesPerCycle; pass++)
        {
            using var scope = _scopeFactory.CreateScope();
            var job = scope.ServiceProvider.GetRequiredService<StorefrontUnresolvedPaymentReconciliationJob>();
            var reconciled = await job.ExecuteAsync(cancellationToken);

            totalReconciled += reconciled;

            if (reconciled == 0)
            {
                break;
            }
        }

        if (totalReconciled > 0)
        {
            _logger.LogInformation(
                "[StorefrontPaymentRecovery] Reconciled {Count} suspense-routed storefront payment webhook(s)",
                totalReconciled);
        }
    }
}
