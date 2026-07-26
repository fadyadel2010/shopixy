using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nest;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.HealthChecks;

/// <summary>
/// Global Marketplace Health Check.
/// Validates connectivity and operational integrity (drift detection).
/// </summary>
public class MarketplaceHealthCheck : IHealthCheck
{
    private static readonly TimeSpan DependencyTimeout = TimeSpan.FromSeconds(5);

    private readonly MarketplaceTelemetry _telemetry;
    private readonly IElasticClient _elasticClient;

    public MarketplaceHealthCheck(
        MarketplaceTelemetry telemetry,
        IElasticClient elasticClient)
    {
        _telemetry = telemetry;
        _elasticClient = elasticClient;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. Operational Integrity (Drift)
        // Degraded status allows service to function but alerts ops team
        if (_telemetry.TrustDriftCount > 0)
        {
            return HealthCheckResult.Degraded(
                $"TRUST DRIFT DETECTED: {_telemetry.TrustDriftCount} records were corrupted and auto-reconciled since start.");
        }

        if (_telemetry.ElasticsearchDriftCount > 0)
        {
            return HealthCheckResult.Degraded(
                $"ELASTICSEARCH DRIFT DETECTED: {_telemetry.ElasticsearchDriftCount} documents were out of sync and auto-reconciled since start.");
        }

        // 2. Infrastructure Connectivity
        // Elasticsearch check
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DependencyTimeout);

        var ping = await _elasticClient.PingAsync(c => c, timeoutCts.Token);
        if (!ping.IsValid)
        {
            return HealthCheckResult.Unhealthy("Elasticsearch unreachable");
        }

        // DB & Mongo checks are typically covered by their own health checks registered via AddDbContextCheck etc.
        // Marketplace specific check is for the higher-level invariants.

        return HealthCheckResult.Healthy("Marketplace services operational: Zero drift, all dependencies healthy.");
    }
}
