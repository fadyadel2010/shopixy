using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Interfaces.Marketplace;

namespace PointOfSale.Infrastructure.Eventing;

public class MarketplaceEventOutboxWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MarketplaceEventOutboxWorker> _logger;
    private readonly MarketplaceEventBackboneOptions _options;

    public MarketplaceEventOutboxWorker(
        IServiceProvider serviceProvider,
        ILogger<MarketplaceEventOutboxWorker> logger,
        IOptions<MarketplaceEventBackboneOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IMarketplaceEventOutboxProcessor>();
                var totalProcessed = 0;

                for (var batchIndex = 0; batchIndex < _options.MaxBatchesPerPoll; batchIndex++)
                {
                    var processed = await processor.ProcessPendingMessagesAsync(
                        _options.EventOutboxBatchSize,
                        stoppingToken);

                    if (processed <= 0)
                    {
                        break;
                    }

                    totalProcessed += processed;

                    if (batchIndex + 1 < _options.MaxBatchesPerPoll &&
                        _options.InterBatchDelayMilliseconds > 0)
                    {
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(_options.InterBatchDelayMilliseconds),
                            stoppingToken);
                    }
                }

                if (totalProcessed > 0)
                {
                    _logger.LogInformation(
                        "Marketplace event outbox worker published {Count} messages",
                        totalProcessed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Marketplace event outbox worker iteration failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.OutboxPollIntervalSeconds), stoppingToken);
        }
    }
}
