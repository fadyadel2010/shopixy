using Microsoft.Extensions.Logging;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Infrastructure.Jobs;

public sealed class AbandonedCartRecoveryDispatchJob
{
    private readonly IAbandonedCartRecoveryScheduler _scheduler;
    private readonly ILogger<AbandonedCartRecoveryDispatchJob> _logger;

    public AbandonedCartRecoveryDispatchJob(
        IAbandonedCartRecoveryScheduler scheduler,
        ILogger<AbandonedCartRecoveryDispatchJob> logger)
    {
        _scheduler = scheduler;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var processed = await _scheduler.DispatchDueAsync(ct);
        if (processed > 0)
        {
            _logger.LogInformation("[Native Recovery] Processed {Count} due message claims", processed);
        }
    }
}
