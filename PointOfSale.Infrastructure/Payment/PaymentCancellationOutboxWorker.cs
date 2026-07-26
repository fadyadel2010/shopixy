using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PointOfSale.Infrastructure.Payment;

/// <summary>
/// Background worker that processes the payment cancellation outbox.
/// 
/// PHASE: Payment Cancellation Hardening
/// 
/// Runs continuously, processing pending messages in batches.
/// Uses exponential backoff for retries.
/// </summary>
public class PaymentCancellationOutboxWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentCancellationOutboxWorker> _logger;
    private readonly PaymentCancellationOutboxOptions _options;
    
    public PaymentCancellationOutboxWorker(
        IServiceProvider serviceProvider,
        ILogger<PaymentCancellationOutboxWorker> logger,
        IOptions<PaymentCancellationOutboxOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PaymentCancellationOutbox | Worker starting");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<PaymentCancellationOutboxProcessor>();

                var totalProcessed = 0;
                for (var batchIndex = 0; batchIndex < _options.MaxBatchesPerPoll; batchIndex++)
                {
                    var processed = await processor.ProcessPendingMessagesAsync(stoppingToken);
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
                        "PaymentCancellationOutbox | Processed {Count} messages",
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
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PaymentCancellationOutbox | Worker error");
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
        
        _logger.LogInformation("PaymentCancellationOutbox | Worker stopped");
    }
}
