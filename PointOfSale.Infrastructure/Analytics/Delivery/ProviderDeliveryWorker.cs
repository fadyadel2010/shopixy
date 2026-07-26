using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public sealed class ProviderDeliveryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ProviderDeliveryOptions _options;
    private readonly ILogger<ProviderDeliveryWorker> _logger;

    public ProviderDeliveryWorker(
        IServiceProvider serviceProvider,
        IOptions<ProviderDeliveryOptions> options,
        ILogger<ProviderDeliveryWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Provider Delivery Worker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IProviderDeliveryProcessor>();

                var totalProcessed = 0;
                for (var batch = 0; batch < _options.MaxBatchesPerPoll; batch++)
                {
                    var processed = await processor.ProcessPendingMessagesAsync(_options.BatchSize, stoppingToken);
                    if (processed == 0)
                    {
                        break;
                    }

                    totalProcessed += processed;
                    if (batch + 1 < _options.MaxBatchesPerPoll &&
                        _options.InterBatchDelayMilliseconds > 0)
                    {
                        await Task.Delay(_options.InterBatchDelayMilliseconds, stoppingToken);
                    }
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(totalProcessed > 0
                        ? _options.PollIntervalSeconds
                        : _options.IdleIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider Delivery Worker error");
                await Task.Delay(TimeSpan.FromSeconds(_options.ErrorDelaySeconds), stoppingToken);
            }
        }

        _logger.LogInformation("Provider Delivery Worker stopped");
    }
}
