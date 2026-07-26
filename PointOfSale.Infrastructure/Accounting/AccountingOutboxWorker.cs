using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Interfaces.Accounting;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>
/// Background worker that processes the accounting outbox.
/// 
/// PHASE 3.5: Guaranteed delivery via outbox pattern.
/// 
/// Runs continuously, processing pending messages in batches.
/// Uses exponential backoff for retries.
/// </summary>
public class AccountingOutboxWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AccountingOutboxWorker> _logger;
    private readonly AccountingOutboxOptions _options;
    
    public AccountingOutboxWorker(
        IServiceProvider serviceProvider,
        ILogger<AccountingOutboxWorker> logger,
        IOptions<AccountingOutboxOptions>? options = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options?.Value ?? new AccountingOutboxOptions();
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Accounting Outbox Worker starting");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IAccountingOutboxProcessor>();

                var totalProcessed = 0;
                for (var batchIndex = 0; batchIndex < _options.MaxBatchesPerRun; batchIndex++)
                {
                    var processed = await processor.ProcessPendingMessagesAsync(_options.BatchSize, stoppingToken);
                    if (processed <= 0)
                    {
                        break;
                    }

                    totalProcessed += processed;

                    if (batchIndex + 1 < _options.MaxBatchesPerRun &&
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
                        "Accounting Outbox Worker processed {Count} messages",
                        totalProcessed);

                    await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.IdleIntervalSeconds), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Accounting Outbox Worker error");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.ErrorDelaySeconds), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        
        _logger.LogInformation("Accounting Outbox Worker stopped");
    }
}
