using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Services.BackgroundTasks;

/// <summary>
/// Hosted service that processes RedirectUrl recovery work items from a background queue.
/// Ensures graceful shutdown and proper lifecycle management for background recovery tasks.
/// </summary>
public sealed class RedirectUrlRecoveryHostedService : BackgroundService
{
    private const int MaxRetryAttempts = 5;
    private const int BaseDelayMs = 1000; // 1 second base delay for exponential backoff
    
    private readonly IBackgroundTaskQueue<RedirectUrlRecoveryWorkItem> _queue;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IPaymentIntentClientSecretProtector _clientSecretProtector;
    private readonly ILogger<RedirectUrlRecoveryHostedService> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly CommerceReliabilityTelemetry? _reliabilityTelemetry;

    public RedirectUrlRecoveryHostedService(
        IBackgroundTaskQueue<RedirectUrlRecoveryWorkItem> queue,
        IServiceScopeFactory serviceScopeFactory,
        IPaymentIntentClientSecretProtector clientSecretProtector,
        ILogger<RedirectUrlRecoveryHostedService> logger,
        IHostApplicationLifetime applicationLifetime,
        CommerceReliabilityTelemetry? reliabilityTelemetry = null)
    {
        _queue = queue;
        _serviceScopeFactory = serviceScopeFactory;
        _clientSecretProtector = clientSecretProtector;
        _logger = logger;
        _applicationLifetime = applicationLifetime;
        _reliabilityTelemetry = reliabilityTelemetry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RedirectUrlRecoveryHostedService started");

        try
        {
            await ProcessQueueAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("RedirectUrlRecoveryHostedService is stopping due to cancellation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RedirectUrlRecoveryHostedService encountered an unexpected error");
        }
        finally
        {
            _logger.LogInformation("RedirectUrlRecoveryHostedService stopped");
        }
    }

    private async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var workItem = await _queue.DequeueAsync(stoppingToken);
                
                _logger.LogInformation(
                    "Processing RedirectUrl recovery work item | TenantId: {TenantId} | OrderId: {OrderId} | CorrelationId: {CorrelationId} | Attempt: {Attempt}/{MaxAttempts} | QueueTime: {QueueTimeMs}ms",
                    workItem.TenantId,
                    workItem.OrderId,
                    workItem.CorrelationId,
                    workItem.AttemptCount + 1,
                    MaxRetryAttempts,
                    (DateTime.UtcNow - workItem.EnqueuedAt).TotalMilliseconds);

                var result = await ProcessWorkItemAsync(workItem, stoppingToken);
                
                // Handle retry logic based on result
                if (!result.Success && !result.IsRetryable)
                {
                    // Non-retryable failure - move to dead letter immediately
                    _logger.LogError(
                        "RedirectUrl recovery: Non-retryable failure, moving to dead letter | OrderId: {OrderId} | Attempt: {Attempt}/{MaxAttempts} | Error: {Error} | CorrelationId: {CorrelationId}",
                        workItem.OrderId,
                        workItem.AttemptCount + 1,
                        MaxRetryAttempts,
                        result.ErrorMessage,
                        workItem.CorrelationId);

                    _reliabilityTelemetry?.RecordBackgroundJobExecution("redirect_url_recovery", "dead_lettered");
                    await LogDeadLetterAsync(workItem, result.ErrorMessage);
                }
                else if (!result.Success && result.IsRetryable && workItem.AttemptCount + 1 < MaxRetryAttempts)
                {
                    // Calculate exponential backoff delay
                    var delayMs = BaseDelayMs * Math.Pow(2, workItem.AttemptCount);
                    
                    _logger.LogWarning(
                        "RedirectUrl recovery: Transient failure, will retry | OrderId: {OrderId} | Attempt: {Attempt}/{MaxAttempts} | RetryDelayMs: {DelayMs} | Error: {Error} | CorrelationId: {CorrelationId}",
                        workItem.OrderId,
                        workItem.AttemptCount + 1,
                        MaxRetryAttempts,
                        delayMs,
                        result.ErrorMessage,
                        workItem.CorrelationId);

                    // Apply exponential backoff before requeue
                    await Task.Delay(TimeSpan.FromMilliseconds(delayMs), stoppingToken);
                    
                    // Requeue with incremented attempt count
                    var retryWorkItem = new RedirectUrlRecoveryWorkItem
                    {
                        TenantId = workItem.TenantId,
                        OrderId = workItem.OrderId,
                        CorrelationId = workItem.CorrelationId,
                        EnqueuedAt = workItem.EnqueuedAt,
                        AttemptCount = workItem.AttemptCount + 1,
                        LastAttemptAt = DateTime.UtcNow
                    };
                    
                    var requeued = await _queue.QueueAsync(retryWorkItem, stoppingToken);
                    
                    if (requeued)
                    {
                        _reliabilityTelemetry?.RecordBackgroundJobExecution("redirect_url_recovery", "retry");
                    }
                    else
                    {
                        _logger.LogError(
                            "RedirectUrl recovery: Failed to requeue work item after transient failure | OrderId: {OrderId} | Attempt: {Attempt} | CorrelationId: {CorrelationId}",
                            workItem.OrderId,
                            workItem.AttemptCount + 1,
                            workItem.CorrelationId);

                        _reliabilityTelemetry?.RecordBackgroundJobExecution("redirect_url_recovery", "dead_lettered");
                        // Move to dead letter (log with full context for manual recovery)
                        await LogDeadLetterAsync(workItem, result.ErrorMessage);
                    }
                }
                else if (!result.Success && workItem.AttemptCount + 1 >= MaxRetryAttempts)
                {
                    // Max attempts reached - move to dead letter
                    _logger.LogError(
                        "RedirectUrl recovery: Max retry attempts reached, moving to dead letter | OrderId: {OrderId} | Attempts: {Attempts} | Error: {Error} | CorrelationId: {CorrelationId}",
                        workItem.OrderId,
                        workItem.AttemptCount + 1,
                        result.ErrorMessage,
                        workItem.CorrelationId);

                    _reliabilityTelemetry?.RecordBackgroundJobExecution("redirect_url_recovery", "dead_lettered");
                    await LogDeadLetterAsync(workItem, result.ErrorMessage);
                }
                else if (result.Success)
                {
                    _reliabilityTelemetry?.RecordBackgroundJobExecution("redirect_url_recovery", "success");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing RedirectUrl recovery work item");
                _reliabilityTelemetry?.RecordBackgroundJobExecution("redirect_url_recovery", "failed");
                // Continue processing other items
            }
        }
    }

    private async Task<ProcessingResult> ProcessWorkItemAsync(RedirectUrlRecoveryWorkItem workItem, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stripeRuntimeApplicationService = scope.ServiceProvider.GetRequiredService<IStripeRuntimeApplicationService>();
            var storefrontUrlResolver = scope.ServiceProvider.GetRequiredService<StorefrontUrlResolver>();
            
            // Find the payment intent for this order
            var intent = await dbContext.PaymentIntents
                .Include(pi => pi.Order)
                .FirstOrDefaultAsync(pi => 
                    pi.TenantId == workItem.TenantId && 
                    pi.OrderId == workItem.OrderId &&
                    pi.Provider == PaymentProvider.Stripe &&
                    !string.IsNullOrWhiteSpace(pi.ProviderIntentId),
                    cancellationToken);

            if (intent == null)
            {
                _logger.LogWarning(
                    "RedirectUrl recovery: Payment intent not found | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                    workItem.OrderId, workItem.CorrelationId);
                return ProcessingResult.NonRetryableFailure("Payment intent not found");
            }

            if (intent.Order == null)
            {
                _logger.LogWarning(
                    "RedirectUrl recovery: Order not found | PaymentIntentId: {PaymentIntentId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                    intent.Id, workItem.OrderId, workItem.CorrelationId);
                return ProcessingResult.NonRetryableFailure("Order not found");
            }

            // Check if ClientSecret is already present
            if (!string.IsNullOrWhiteSpace(intent.ClientSecretEncrypted))
            {
                _logger.LogInformation(
                    "RedirectUrl recovery: Client secret already recovered | PaymentIntentId: {PaymentIntentId} | CorrelationId: {CorrelationId}",
                    intent.Id, workItem.CorrelationId);
                return ProcessingResult.Succeeded();
            }

            _logger.LogInformation(
                "RedirectUrl recovery: Attempting recovery from Stripe | PaymentIntentId: {PaymentIntentId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                intent.Id, intent.ProviderIntentId, workItem.CorrelationId);

            var tenant = await dbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == workItem.TenantId && t.DeletedAtUtc == null, cancellationToken);
            if (tenant == null)
            {
                return ProcessingResult.NonRetryableFailure("Tenant not found");
            }

            var idempotencyKey = string.IsNullOrWhiteSpace(intent.IdempotencyKey)
                ? $"stripe:intent:{workItem.TenantId:N}:{workItem.OrderId:N}"
                : intent.IdempotencyKey;
            var recoveredIntent = await stripeRuntimeApplicationService.CreatePaymentIntentAsync(
                new StripeRuntimePaymentIntentApplicationRequest(
                    workItem.TenantId,
                    intent.OrderId,
                    intent.Id,
                    intent.Order.OrderNumber,
                    intent.Amount,
                    intent.Currency,
                    storefrontUrlResolver.Resolve(tenant),
                    intent.Order.OnlineCustomerId,
                    intent.PaymentReference,
                    idempotencyKey,
                    workItem.CorrelationId,
                    intent.CreatedAt == default ? DateTime.UtcNow : intent.CreatedAt,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            if (!string.Equals(recoveredIntent.StripePaymentIntentId, intent.ProviderIntentId, StringComparison.Ordinal))
            {
                _logger.LogError(
                    "RedirectUrl recovery: Canonical Stripe runtime application path returned mismatched provider authority | PaymentIntentId: {PaymentIntentId} | PersistedStripeIntentId: {PersistedStripeIntentId} | RecoveredStripeIntentId: {RecoveredStripeIntentId} | CorrelationId: {CorrelationId}",
                    intent.Id,
                    intent.ProviderIntentId,
                    recoveredIntent.StripePaymentIntentId,
                    workItem.CorrelationId);

                return ProcessingResult.NonRetryableFailure("Recovered Stripe PaymentIntent does not match persisted provider intent");
            }

            if (string.IsNullOrWhiteSpace(recoveredIntent.ClientSecret))
            {
                _logger.LogWarning(
                    "RedirectUrl recovery: Failed - intent or client secret not found | PaymentIntentId: {PaymentIntentId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId}",
                    intent.Id, intent.ProviderIntentId, workItem.CorrelationId);

                return ProcessingResult.NonRetryableFailure("Intent or client secret not found in Stripe");
            }

            // Store client secret securely using encryption.
            intent.ClientSecretEncrypted = _clientSecretProtector.Protect(recoveredIntent.ClientSecret);
            intent.RedirectUrl = null; // Clear RedirectUrl to avoid confusion
            await dbContext.SaveChangesAsync(cancellationToken);

            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation(
                "RedirectUrl recovery: Successfully recovered through canonical Stripe runtime application path | PaymentIntentId: {PaymentIntentId} | StripeIntentId: {StripeIntentId} | CorrelationId: {CorrelationId} | Duration: {DurationMs}ms",
                intent.Id, intent.ProviderIntentId, workItem.CorrelationId, duration);

            return ProcessingResult.Succeeded();
        }
        catch (StripeApiException ex)
        {
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogWarning(ex,
                "RedirectUrl recovery: Stripe runtime application error | OrderId: {OrderId} | CorrelationId: {CorrelationId} | Duration: {DurationMs}ms",
                workItem.OrderId, workItem.CorrelationId, duration);

            var isRetryable = IsRetryableStripeError(ex);
            return isRetryable
                ? ProcessingResult.RetryableFailure($"Stripe API error: {ex.Message}")
                : ProcessingResult.NonRetryableFailure($"Stripe API error: {ex.Message}");
        }
        catch (DbUpdateException ex)
        {
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogWarning(ex,
                "RedirectUrl recovery: Database error (retryable) | OrderId: {OrderId} | CorrelationId: {CorrelationId} | Duration: {DurationMs}ms",
                workItem.OrderId, workItem.CorrelationId, duration);
            
            return ProcessingResult.RetryableFailure($"Database error: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogWarning(ex,
                "RedirectUrl recovery: Timeout error (retryable) | OrderId: {OrderId} | CorrelationId: {CorrelationId} | Duration: {DurationMs}ms",
                workItem.OrderId, workItem.CorrelationId, duration);
            
            return ProcessingResult.RetryableFailure($"Timeout: {ex.Message}");
        }
        catch (Exception ex)
        {
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex,
                "RedirectUrl recovery: Unexpected error | OrderId: {OrderId} | CorrelationId: {CorrelationId} | Duration: {DurationMs}ms",
                workItem.OrderId, workItem.CorrelationId, duration);
            
            // Treat unexpected errors as retryable to be safe
            return ProcessingResult.RetryableFailure($"Unexpected error: {ex.Message}");
        }
    }

    private bool IsRetryableStripeError(StripeApiException ex)
    {
        return ex.StripeErrorType switch
        {
            "rate_limit_error" => true,
            "api_connection_error" => true,
            "api_error" => true,
            _ => string.Equals(ex.StripeErrorCode, "rate_limit", StringComparison.OrdinalIgnoreCase)
        };
    }

    private async Task LogDeadLetterAsync(RedirectUrlRecoveryWorkItem workItem, string errorMessage)
    {
        // Log to dead letter with full context for manual recovery
        // In a production system, this could write to a durable dead-letter table or external storage
        _logger.LogError(
            "DEAD_LETTER: RedirectUrl recovery permanently failed | " +
            "TenantId: {TenantId} | OrderId: {OrderId} | CorrelationId: {CorrelationId} | " +
            "Attempts: {Attempts} | EnqueuedAt: {EnqueuedAt} | LastAttemptAt: {LastAttemptAt} | " +
            "Error: {Error} | " +
            "MANUAL_RECOVERY_REQUIRED",
            workItem.TenantId,
            workItem.OrderId,
            workItem.CorrelationId,
            workItem.AttemptCount + 1,
            workItem.EnqueuedAt,
            workItem.LastAttemptAt ?? DateTime.UtcNow,
            errorMessage);
        
        // TODO: Consider writing to a durable dead-letter table for automated recovery attempts
        // or integration with external monitoring/alerting systems
        
        await Task.CompletedTask;
    }

    private record ProcessingResult(bool Success, bool IsRetryable, string? ErrorMessage)
    {
        public static ProcessingResult Succeeded() => new(true, false, null);
        public static ProcessingResult RetryableFailure(string error) => new(false, true, error);
        public static ProcessingResult NonRetryableFailure(string error) => new(false, false, error);
    }
}

