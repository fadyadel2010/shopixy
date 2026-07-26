using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PointOfSale.Infrastructure.Elasticsearch.Services;

namespace PointOfSale.Infrastructure.Elasticsearch;

/// <summary>
/// Background service that ensures Elasticsearch indices exist at startup.
/// 
/// Behavior:
/// - Runs once at application startup
/// - Creates all indices (products, categories, cmspages) with explicit mappings
/// - Does NOT block application startup on failure
/// - Logs status for observability
/// </summary>
public class ElasticsearchIndexInitializer : IHostedService
{
    private readonly IUnifiedSearchIndexService _indexService;
    private readonly ILogger<ElasticsearchIndexInitializer> _logger;

    public ElasticsearchIndexInitializer(
        IUnifiedSearchIndexService indexService,
        ILogger<ElasticsearchIndexInitializer> logger)
    {
        _indexService = indexService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => RunInitializationAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunInitializationAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Elasticsearch index initialization starting...");

        try
        {
            var success = await _indexService.EnsureIndicesExistAsync(cancellationToken);

            if (success)
            {
                _logger.LogInformation(
                    "Elasticsearch index initialization completed successfully | Indices: products, categories, cmspages");
            }
            else
            {
                _logger.LogWarning(
                    "Elasticsearch index initialization completed with issues. " +
                    "Search functionality may be degraded until resolved.");
            }
        }
        catch (Exception ex)
        {
            // Log but don't throw - Elasticsearch failure should not prevent app startup
            _logger.LogError(ex,
                "Elasticsearch index initialization failed. " +
                "Search functionality will be unavailable until Elasticsearch is accessible.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
