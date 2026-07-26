using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Infrastructure.Jobs;

/// <summary>
/// Hangfire background job that scans for abandoned checkout sessions.
/// 
/// Phase: Abandoned Checkout / Lost Orders
/// 
/// Runs every 5 minutes. Finds active checkout sessions that have been
/// idle beyond the threshold (30 minutes default) and marks them as abandoned.
/// 
/// ARCHITECTURAL RULES:
/// - Cross-tenant scanning (IgnoreQueryFilters in service layer)
/// - Batch processing with limit to prevent memory pressure
/// - Each abandonment is its own DB + event transaction
/// - Failure on one session does NOT block others
/// </summary>
public class AbandonedCheckoutScannerJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AbandonedCheckoutScannerJob> _logger;

    private const int IdleThresholdMinutes = 30;

    public AbandonedCheckoutScannerJob(
        IServiceScopeFactory scopeFactory,
        ILogger<AbandonedCheckoutScannerJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("[AbandonedCheckoutScanner] Starting scan for idle checkout sessions...");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ICheckoutSessionService>();

            var idleSessions = await service.FindSessionsReadyForAbandonmentAsync(IdleThresholdMinutes, ct);

            if (idleSessions.Count == 0)
            {
                _logger.LogDebug("[AbandonedCheckoutScanner] No idle sessions found");
                return;
            }

            _logger.LogInformation(
                "[AbandonedCheckoutScanner] Found {Count} idle sessions to mark as abandoned",
                idleSessions.Count);

            int markedCount = 0;
            int failedCount = 0;

            foreach (var session in idleSessions)
            {
                try
                {
                    await service.MarkAsAbandonedAsync(session.Id, ct);
                    markedCount++;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogError(ex,
                        "[AbandonedCheckoutScanner] Failed to mark session {SessionId} as abandoned (tenant {TenantId})",
                        session.Id, session.TenantId);
                    // Continue processing other sessions
                }
            }

            _logger.LogInformation(
                "[AbandonedCheckoutScanner] Scan complete | Marked: {Marked} | Failed: {Failed} | Total: {Total}",
                markedCount, failedCount, idleSessions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AbandonedCheckoutScanner] Scanner job failed");
            throw; // Let Hangfire handle retry
        }
    }
}
