using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Payment;

/// <summary>
/// Processor for payment cancellation outbox messages.
/// 
/// PHASE: Payment Cancellation Hardening
/// 
/// Features:
/// - Batch processing for efficiency
/// - Exponential backoff on failures
/// - Retry logic with max attempts
/// - Durable payment cancellation
/// </summary>
public class PaymentCancellationOutboxProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentCancellationOutboxProcessor> _logger;
    private readonly PaymentCancellationOutboxOptions _options;

    private sealed record PendingCancellationMessage(
        Guid Id,
        Guid TenantId,
        Guid OrderId,
        Guid? PaymentIntentId,
        bool SkipPaymentIntentResolution);
    
    public PaymentCancellationOutboxProcessor(
        IServiceProvider serviceProvider,
        ILogger<PaymentCancellationOutboxProcessor> logger,
        IOptions<PaymentCancellationOutboxOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }
    
    public async Task<int> ProcessPendingMessagesAsync(CancellationToken ct = default)
    {
        using var batchScope = _serviceProvider.CreateScope();
        var batchDb = batchScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var nowUtc = DateTime.UtcNow;

        var pendingMessages = await batchDb.PaymentCancellationOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.ProcessedAtUtc == null && 
                        !m.IsFailed &&
                        (m.NextRetryAtUtc == null || m.NextRetryAtUtc <= nowUtc) &&
                        (m.ClaimedUntilUtc == null || m.ClaimedUntilUtc <= nowUtc))
            .OrderBy(m => m.CreatedAt)
            .Take(_options.BatchSize)
            .Select(m => new PendingCancellationMessage(
                m.Id,
                m.TenantId,
                m.OrderId,
                m.PaymentIntentId,
                m.SkipPaymentIntentResolution))
            .ToListAsync(ct);
        
        if (pendingMessages.Count == 0)
        {
            return 0;
        }

        _logger.LogInformation(
            "PaymentCancellationOutbox | Processing {Count} pending messages",
            pendingMessages.Count);

        var processedCount = 0;
        
        foreach (var pendingMessage in pendingMessages)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            
            var message = await db.PaymentCancellationOutboxMessages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(entry => entry.Id == pendingMessage.Id, ct);

            if (message == null ||
                message.ProcessedAtUtc != null ||
                message.IsFailed ||
                (message.NextRetryAtUtc != null && message.NextRetryAtUtc > DateTime.UtcNow) ||
                (message.ClaimedUntilUtc != null && message.ClaimedUntilUtc > DateTime.UtcNow))
            {
                continue;
            }

            try
            {
                var claimResult = await TryClaimMessageAsync(db, message.Id, ct);
                if (claimResult != PaymentCancellationClaimOutcome.Claimed)
                {
                    if (claimResult is PaymentCancellationClaimOutcome.AlreadyClaimed or PaymentCancellationClaimOutcome.ConcurrencyConflict)
                    {
                        RecordActiveOverlapTelemetry(message.TenantId);
                    }

                    continue;
                }

                db.ChangeTracker.Clear();
                message = await db.PaymentCancellationOutboxMessages
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entry => entry.Id == pendingMessage.Id, ct);

                if (message == null)
                {
                    _logger.LogWarning(
                        "PaymentCancellationOutbox | Message {MessageId} disappeared after claim",
                        pendingMessage.Id);
                    continue;
                }

                await ProcessMessageAsync(scope.ServiceProvider, db, message, ct);
                processedCount++;
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogDebug(
                    "PaymentCancellationOutbox | Concurrency conflict processing message {MessageId}",
                    pendingMessage.Id);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "PaymentCancellationOutbox | Unexpected error processing message {MessageId}",
                    pendingMessage.Id);
            }
        }
        
        return processedCount;
    }

    private async Task<PaymentCancellationClaimOutcome> TryClaimMessageAsync(
        ApplicationDbContext db,
        Guid messageId,
        CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            var message = await db.PaymentCancellationOutboxMessages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(entry => entry.Id == messageId, ct);

            if (message == null ||
                message.ProcessedAtUtc != null ||
                message.IsFailed ||
                (message.NextRetryAtUtc != null && message.NextRetryAtUtc > DateTime.UtcNow))
            {
                return PaymentCancellationClaimOutcome.MessageNoLongerEligible;
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                if (IsPostgresProvider(db))
                {
                    var lockId = ComputeStableAdvisoryLockKey(message.TenantId, message.OrderId);
                    await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({lockId})", ct);
                }

                if (!message.TryClaim(_options.ProcessingLeaseSeconds))
                {
                    await tx.RollbackAsync(ct);
                    return PaymentCancellationClaimOutcome.AlreadyClaimed;
                }

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return PaymentCancellationClaimOutcome.Claimed;
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(ct);
                return PaymentCancellationClaimOutcome.ConcurrencyConflict;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });
    }
    
    private async Task ProcessMessageAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        PaymentCancellationOutboxMessage message,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation(
                "PaymentCancellationOutbox | Processing message {MessageId} | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | Attempt: {Attempt}",
                message.Id,
                message.TenantId,
                message.OrderId,
                message.PaymentIntentId,
                message.AttemptCount + 1);
            
            var paymentLifecycleService = services.GetRequiredService<IPaymentLifecycleService>();

            var lifecycleResult = await paymentLifecycleService.HandlePaymentResultAsync(new HandlePaymentResultRequest
            {
                OrderId = message.OrderId,
                PaymentIntentId = message.PaymentIntentId,
                Outcome = PaymentLifecycleOutcome.Cancelled,
                Source = PaymentLifecycleSource.AdminCancellation,
                Reason = message.Reason,
                Actor = message.Actor,
                IgnoreQueryFilters = true,
                SkipPaymentIntentResolution = message.SkipPaymentIntentResolution
            }, ct);

            if (lifecycleResult.IsSuccess)
            {
                message.MarkProcessed();
                
                await db.SaveChangesAsync(ct);

                stopwatch.Stop();
                
                _logger.LogInformation(
                    "PaymentCancellationOutbox | SUCCESS | MessageId: {MessageId} | TenantId: {TenantId} | OrderId: {OrderId} | Duration: {Duration}ms",
                    message.Id,
                    message.TenantId,
                    message.OrderId,
                    stopwatch.ElapsedMilliseconds);
            }
            else if (lifecycleResult.RequiresManualReview)
            {
                message.MarkProcessed();
                
                // Apply same truncation and fallback logic as failure path
                var errorMessage = lifecycleResult.ErrorMessage ?? "Unknown error";
                message.LastError = errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage;

                await db.SaveChangesAsync(ct);

                stopwatch.Stop();

                _logger.LogWarning(
                    "PaymentCancellationOutbox | MANUAL_REVIEW_REQUIRED | MessageId: {MessageId} | TenantId: {TenantId} | OrderId: {OrderId} | FailureCode: {FailureCode} | Error: {Error} | Duration: {Duration}ms",
                    message.Id,
                    message.TenantId,
                    message.OrderId,
                    lifecycleResult.FailureCode,
                    message.LastError,
                    stopwatch.ElapsedMilliseconds);
            }
            else
            {
                await HandleFailureAsync(db, message, lifecycleResult.ErrorMessage ?? "Unknown error", ct);
            }
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(db, message, ex.Message, ct);
            throw;
        }
    }
    
    private async Task HandleFailureAsync(
        ApplicationDbContext db,
        PaymentCancellationOutboxMessage message,
        string errorMessage,
        CancellationToken ct)
    {
        message.AttemptCount++;
        message.LastError = errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage;
        message.ClearClaim();

        if (message.AttemptCount >= _options.MaxRetryAttempts)
        {
            message.IsFailed = true;
            message.NextRetryAtUtc = null;
            
            _logger.LogError(
                "PaymentCancellationOutbox | PERMANENT_FAILURE | MessageId: {MessageId} | TenantId: {TenantId} | OrderId: {OrderId} | Attempts: {Attempts} | Error: {Error}",
                message.Id,
                message.TenantId,
                message.OrderId,
                message.AttemptCount,
                message.LastError);
        }
        else
        {
            var delayMinutes = Math.Min(
                _options.MaxRetryDelayMinutes,
                Math.Pow(2, message.AttemptCount - 1));
            message.NextRetryAtUtc = DateTime.UtcNow.AddMinutes(delayMinutes);
            
            _logger.LogWarning(
                "PaymentCancellationOutbox | RETRY_SCHEDULED | MessageId: {MessageId} | TenantId: {TenantId} | OrderId: {OrderId} | Attempt: {Attempt} | NextRetry: {NextRetry} | Error: {Error}",
                message.Id,
                message.TenantId,
                message.OrderId,
                message.AttemptCount,
                message.NextRetryAtUtc,
                message.LastError);
        }

        await db.SaveChangesAsync(ct);
    }

    private static bool IsPostgresProvider(ApplicationDbContext db) =>
        db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true ||
        db.Database.ProviderName?.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true;

    private static long ComputeStableAdvisoryLockKey(Guid tenantId, Guid orderId)
    {
        unchecked
        {
            const ulong fnvOffsetBasis = 14695981039346656037UL;
            const ulong fnvPrime = 1099511628211UL;
            var hash = fnvOffsetBasis;

            foreach (var value in tenantId.ToByteArray())
            {
                hash ^= value;
                hash *= fnvPrime;
            }

            foreach (var value in orderId.ToByteArray())
            {
                hash ^= value;
                hash *= fnvPrime;
            }

            return (long)hash;
        }
    }

    private static void RecordActiveOverlapTelemetry(Guid tenantId)
    {
        OperationalConvergenceTelemetry.RecordReplayDecision(
            OperationalConvergencePhase.Outbox,
            "payment_cancellation_outbox",
            OperationalConvergenceAuthority.OrderPayment,
            OperationalConvergenceDecision.Collapsed,
            OperationalConvergenceReason.ActiveDeliveryOverlap,
            tenantId);
    }

    private enum PaymentCancellationClaimOutcome
    {
        MessageNoLongerEligible = 0,
        AlreadyClaimed = 1,
        ConcurrencyConflict = 2,
        Claimed = 3
    }
}
