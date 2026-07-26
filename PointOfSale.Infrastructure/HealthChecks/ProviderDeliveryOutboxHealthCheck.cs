using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.HealthChecks;

public sealed class ProviderDeliveryOutboxHealthCheck : IHealthCheck
{
    private static readonly TimeSpan WarningLag = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CriticalLag = TimeSpan.FromMinutes(15);
    private const int RepeatedFailureAttemptThreshold = 3;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ProviderDeliveryOutboxHealthCheck> _logger;

    public ProviderDeliveryOutboxHealthCheck(
        ApplicationDbContext dbContext,
        ILogger<ProviderDeliveryOutboxHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        var warningCutoffUtc = nowUtc.Subtract(WarningLag);
        var criticalCutoffUtc = nowUtc.Subtract(CriticalLag);

        var metrics = await _dbContext.Set<ProviderDeliveryOutboxMessage>()
            .IgnoreQueryFilters()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                PendingCount = group.Count(m => m.DeliveredAtUtc == null && !m.IsDeadLettered),
                DeadLetterCount = group.Count(m => m.IsDeadLettered),
                RepeatedFailureCount = group.Count(m =>
                    m.DeliveredAtUtc == null &&
                    !m.IsDeadLettered &&
                    m.AttemptCount >= RepeatedFailureAttemptThreshold),
                WarningLagCount = group.Count(m =>
                    m.DeliveredAtUtc == null &&
                    !m.IsDeadLettered &&
                    m.OccurredAtUtc <= warningCutoffUtc),
                CriticalLagCount = group.Count(m =>
                    m.DeliveredAtUtc == null &&
                    !m.IsDeadLettered &&
                    m.OccurredAtUtc <= criticalCutoffUtc)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var data = new Dictionary<string, object>
        {
            ["PendingCount"] = metrics?.PendingCount ?? 0,
            ["DeadLetterCount"] = metrics?.DeadLetterCount ?? 0,
            ["RepeatedFailureCount"] = metrics?.RepeatedFailureCount ?? 0,
            ["WarningLagCount"] = metrics?.WarningLagCount ?? 0,
            ["CriticalLagCount"] = metrics?.CriticalLagCount ?? 0
        };

        if ((metrics?.CriticalLagCount ?? 0) > 0 || (metrics?.RepeatedFailureCount ?? 0) > 0)
        {
            _logger.LogError(
                "Provider delivery outbox readiness failed | Pending: {Pending} | DeadLetter: {DeadLetter} | RepeatedFailures: {RepeatedFailures} | CriticalLag: {CriticalLag}",
                data["PendingCount"],
                data["DeadLetterCount"],
                data["RepeatedFailureCount"],
                data["CriticalLagCount"]);

            return HealthCheckResult.Unhealthy("Provider delivery outbox has critical lag or repeated failures.", data: data);
        }

        if ((metrics?.WarningLagCount ?? 0) > 0 || (metrics?.DeadLetterCount ?? 0) > 0)
        {
            return HealthCheckResult.Degraded("Provider delivery outbox has warning lag or dead-lettered messages.", data: data);
        }

        return HealthCheckResult.Healthy("Provider delivery outbox is healthy.", data);
    }
}
