using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PointOfSale.Infrastructure.Eventing;

public class StockSyncOutboxWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StockSyncOutboxWorker> _logger;
    private readonly StockSyncOutboxOptions _options;

    public StockSyncOutboxWorker(
        IServiceProvider serviceProvider,
        ILogger<StockSyncOutboxWorker> logger,
        IOptions<StockSyncOutboxOptions> options)
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
                var processor = scope.ServiceProvider.GetRequiredService<StockSyncOutboxProcessor>();
                var totalProcessed = 0;

                for (var batchIndex = 0; batchIndex < _options.MaxBatchesPerPoll; batchIndex++)
                {
                    var processed = await processor.ProcessPendingMessagesAsync(
                        _options.BatchSize,
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
                        "Stock sync outbox worker published {Count} messages",
                        totalProcessed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stock sync outbox worker iteration failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
        }
    }
}
