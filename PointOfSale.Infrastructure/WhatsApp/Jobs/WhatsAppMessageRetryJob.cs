using Microsoft.Extensions.Logging;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;

namespace PointOfSale.Infrastructure.WhatsApp.Jobs;

/// <summary>
/// Hangfire job that retries failed WhatsApp messages.
/// Runs every 5 minutes to process messages with status = "QUEUED" or "FAILED" with retry count < 3.
/// </summary>
public class WhatsAppMessageRetryJob
{
    private readonly IWhatsAppMessageLogService _logService;
    private readonly IWhatsAppMessageDispatcher _dispatcher;
    private readonly ILogger<WhatsAppMessageRetryJob> _logger;
    
    private const int MaxRetries = 3;

    public WhatsAppMessageRetryJob(
        IWhatsAppMessageLogService logService,
        IWhatsAppMessageDispatcher dispatcher,
        ILogger<WhatsAppMessageRetryJob> logger)
    {
        _logService = logService;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <summary>
    /// Main job execution method - called by Hangfire.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[WhatsApp Retry] Starting message retry job...");

        // Get pending/failed messages that can be retried
        var messages = await _logService.GetPendingMessagesAsync(MaxRetries, 50, ct);

        _logger.LogInformation("[WhatsApp Retry] Found {Count} messages to process", messages.Count);

        foreach (var message in messages)
        {
            try
            {
                _logger.LogDebug(
                    "[WhatsApp Retry] Processing message | LogId: {LogId} | Attempts: {Attempts}",
                    message.Id, message.SendAttempts);

                // Dispatch claim increments the attempt counter exactly once.
                await _dispatcher.DispatchAsync(message.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[WhatsApp Retry] Error processing message | LogId: {LogId}",
                    message.Id);

                // Update status to failed if max retries exceeded
                if (message.SendAttempts >= MaxRetries)
                {
                    await _logService.UpdateStatusAsync(message.Id, "FAILED", "Max retries exceeded");
                }
            }
        }

        _logger.LogInformation("[WhatsApp Retry] Message retry job completed");
    }
}
