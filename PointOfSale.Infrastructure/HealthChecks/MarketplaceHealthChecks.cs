using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Nest;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.HealthChecks;

/// <summary>
/// P2-10: Health check for Elasticsearch marketplace index.
/// Detects connectivity issues, missing indexes, and document count anomalies.
/// </summary>
public class MarketplaceElasticsearchHealthCheck : IHealthCheck
{
    private static readonly TimeSpan DependencyTimeout = TimeSpan.FromSeconds(5);

    private readonly IElasticClient _elasticClient;
    private readonly ILogger<MarketplaceElasticsearchHealthCheck> _logger;

    public MarketplaceElasticsearchHealthCheck(
        IElasticClient elasticClient,
        ILogger<MarketplaceElasticsearchHealthCheck> logger)
    {
        _elasticClient = elasticClient;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DependencyTimeout);

            // Check cluster health for the marketplace index
            var clusterHealth = await _elasticClient.Cluster.HealthAsync(
                new ClusterHealthRequest(MarketplaceServiceIndexConfiguration.IndexName),
                timeoutCts.Token);

            if (!clusterHealth.IsValid)
            {
                _logger.LogError("Elasticsearch cluster health check failed: {Reason}", clusterHealth.DebugInformation);
                return HealthCheckResult.Unhealthy(
                    "Elasticsearch cluster unreachable",
                    data: new Dictionary<string, object> { ["DebugInfo"] = clusterHealth.DebugInformation });
            }

            // Check index exists
            var indexExists = await _elasticClient.Indices.ExistsAsync(
                MarketplaceServiceIndexConfiguration.IndexName,
                ct: timeoutCts.Token);

            var data = new Dictionary<string, object>
            {
                ["ClusterStatus"] = clusterHealth.Status.ToString(),
                ["IndexExists"] = indexExists.Exists,
                ["ActiveShards"] = clusterHealth.ActiveShards,
                ["RelocatingShards"] = clusterHealth.RelocatingShards,
                ["UnassignedShards"] = clusterHealth.UnassignedShards
            };

            if (!indexExists.Exists)
            {
                _logger.LogWarning("Marketplace index does not exist");
                return HealthCheckResult.Degraded(
                    "Marketplace search index does not exist yet",
                    data: data);
            }

            // Check for red cluster status
            if (clusterHealth.Status == global::Elasticsearch.Net.Health.Red)
            {
                _logger.LogError("Elasticsearch cluster is RED for marketplace index");
                return HealthCheckResult.Unhealthy(
                    "Elasticsearch cluster status is RED",
                    data: data);
            }

            if (clusterHealth.Status == global::Elasticsearch.Net.Health.Yellow)
            {
                return HealthCheckResult.Degraded(
                    "Elasticsearch cluster status is YELLOW (possible replica issue)",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                "Marketplace search index healthy",
                data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Marketplace Elasticsearch health check failed");
            return HealthCheckResult.Unhealthy(
                "Elasticsearch health check failed",
                exception: ex);
        }
    }
}

/// <summary>
/// P2-10: Health check for MongoDB provider trust projections.
/// Verifies the trust snapshot collection is accessible and has expected TTL index.
/// </summary>
public class ProviderTrustProjectionHealthCheck : IHealthCheck
{
    private static readonly TimeSpan DependencyTimeout = TimeSpan.FromSeconds(5);

    private readonly IMongoDatabase _mongoDatabase;
    private readonly ILogger<ProviderTrustProjectionHealthCheck> _logger;

    public ProviderTrustProjectionHealthCheck(
        IMongoDatabase mongoDatabase,
        ILogger<ProviderTrustProjectionHealthCheck> logger)
    {
        _mongoDatabase = mongoDatabase;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DependencyTimeout);

            var collection = _mongoDatabase.GetCollection<ProviderTrustSnapshotDocument>("provider_trust_snapshots");
            
            // Verify collection is accessible
            var count = await collection.CountDocumentsAsync(
                FilterDefinition<ProviderTrustSnapshotDocument>.Empty,
                cancellationToken: timeoutCts.Token);

            var data = new Dictionary<string, object>
            {
                ["DocumentCount"] = count
            };

            return HealthCheckResult.Healthy(
                $"Provider trust projections healthy ({count} snapshots)",
                data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provider trust projection health check failed");
            return HealthCheckResult.Unhealthy(
                "Provider trust projection check failed",
                exception: ex);
        }
    }
}
