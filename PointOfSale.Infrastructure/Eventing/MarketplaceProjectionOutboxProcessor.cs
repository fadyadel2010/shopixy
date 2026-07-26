using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.ReadModels.Marketplace;
using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Infrastructure.Eventing;

internal enum MarketplaceProjectionOutboxProcessResult
{
    Skipped = 0,
    Processed = 1,
    Failed = 2
}

internal enum MarketplaceProjectionClaimOutcome
{
    MessageNoLongerEligible = 0,
    AlreadyClaimed = 1,
    ConcurrencyConflict = 2,
    Claimed = 3
}

public sealed class MarketplaceProjectionOutboxProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MarketplaceProjectionOutboxProcessor> _logger;
    private readonly MarketplaceEventBackboneOptions _options;

    public MarketplaceProjectionOutboxProcessor(
        IServiceProvider serviceProvider,
        ILogger<MarketplaceProjectionOutboxProcessor> logger,
        IOptions<MarketplaceEventBackboneOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<int> ProcessPendingMessagesAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        using var batchScope = _serviceProvider.CreateScope();
        var batchDbContext = batchScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var nowUtc = DateTime.UtcNow;

        var pendingMessageIds = await batchDbContext.Set<MarketplaceProjectionOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.ProcessedAtUtc == null &&
                !message.IsFailed &&
                (message.NextRetryAtUtc == null || message.NextRetryAtUtc <= nowUtc) &&
                (message.ClaimedUntilUtc == null || message.ClaimedUntilUtc <= nowUtc))
            .OrderBy(message => message.OccurredAtUtc)
            .ThenBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(batchSize)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken);

        if (pendingMessageIds.Count == 0)
        {
            return 0;
        }

        _logger.LogInformation("Processing {Count} marketplace projection outbox messages", pendingMessageIds.Count);

        var processedCount = 0;
        foreach (var messageId in pendingMessageIds)
        {
            var result = await ProcessMessageAsync(messageId, cancellationToken);
            if (result == MarketplaceProjectionOutboxProcessResult.Processed)
            {
                processedCount++;
            }
        }

        return processedCount;
    }

    internal async Task<MarketplaceProjectionOutboxProcessResult> ProcessMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pipeline = scope.ServiceProvider.GetRequiredService<IMarketplaceEventPipeline>();

        var initialMessage = await dbContext.Set<MarketplaceProjectionOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == messageId, cancellationToken);

        if (initialMessage == null ||
            initialMessage.ProcessedAtUtc != null ||
            initialMessage.IsFailed ||
            (initialMessage.NextRetryAtUtc != null && initialMessage.NextRetryAtUtc > DateTime.UtcNow))
        {
            return MarketplaceProjectionOutboxProcessResult.Skipped;
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        var claimResult = await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();

            var message = await dbContext.Set<MarketplaceProjectionOutboxMessage>()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(entry => entry.Id == messageId, cancellationToken);

            if (message == null ||
                message.ProcessedAtUtc != null ||
                message.IsFailed ||
                (message.NextRetryAtUtc != null && message.NextRetryAtUtc > DateTime.UtcNow))
            {
                return MarketplaceProjectionClaimOutcome.MessageNoLongerEligible;
            }

            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                if (!message.TryClaim(_options.ClaimLeaseSeconds))
                {
                    await tx.RollbackAsync(cancellationToken);
                    return MarketplaceProjectionClaimOutcome.AlreadyClaimed;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return MarketplaceProjectionClaimOutcome.Claimed;
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(cancellationToken);
                return MarketplaceProjectionClaimOutcome.ConcurrencyConflict;
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });

        if (claimResult == MarketplaceProjectionClaimOutcome.MessageNoLongerEligible ||
            claimResult == MarketplaceProjectionClaimOutcome.AlreadyClaimed ||
            claimResult == MarketplaceProjectionClaimOutcome.ConcurrencyConflict)
        {
            if (claimResult != MarketplaceProjectionClaimOutcome.MessageNoLongerEligible)
            {
                OperationalConvergenceTelemetry.RecordReplayDecision(
                    OperationalConvergencePhase.Outbox,
                    "marketplace_projection_outbox",
                    OperationalConvergenceAuthority.ProjectionEventFence,
                    OperationalConvergenceDecision.Collapsed,
                    OperationalConvergenceReason.ActiveDeliveryOverlap,
                    initialMessage?.TenantId);
            }

            return MarketplaceProjectionOutboxProcessResult.Skipped;
        }

        dbContext.ChangeTracker.Clear();
        var claimedMessage = await dbContext.Set<MarketplaceProjectionOutboxMessage>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == messageId, cancellationToken);

        if (claimedMessage == null)
        {
            _logger.LogWarning("Marketplace projection outbox message {MessageId} disappeared after claim", messageId);
            return MarketplaceProjectionOutboxProcessResult.Skipped;
        }

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = claimedMessage.CorrelationId,
            ["TenantId"] = claimedMessage.TenantId,
            ["EventId"] = claimedMessage.EventId,
            ["EventType"] = claimedMessage.EventType,
            ["OutboxMessageId"] = claimedMessage.Id,
            ["ConsumerName"] = claimedMessage.ConsumerName,
        });

        try
        {
            if (await CompletionAlreadyExistsAsync(dbContext, claimedMessage, cancellationToken))
            {
                OperationalConvergenceTelemetry.RecordReplayDecision(
                    OperationalConvergencePhase.Outbox,
                    "marketplace_projection_outbox",
                    OperationalConvergenceAuthority.ProjectionEventFence,
                    OperationalConvergenceDecision.Collapsed,
                    OperationalConvergenceReason.DuplicateCompletedEvent,
                    claimedMessage.TenantId);

                claimedMessage.MarkProcessed();
                dbContext.Set<MarketplaceProjectionOutboxMessage>().Remove(claimedMessage);
                await dbContext.SaveChangesAsync(cancellationToken);
                return MarketplaceProjectionOutboxProcessResult.Skipped;
            }

            await DispatchAsync(pipeline, claimedMessage, cancellationToken);

            claimedMessage.MarkProcessed();
            dbContext.Set<MarketplaceProjectionOutboxMessage>().Remove(claimedMessage);
            dbContext.Set<ProcessedEvent>().Add(
                ProcessedEvent.Create(
                    claimedMessage.EventId,
                    claimedMessage.ConsumerName,
                    claimedMessage.EventType,
                    claimedMessage.EntityId,
                    claimedMessage.TenantId,
                    claimedMessage.CorrelationId));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Duplicate completion can happen if a reclaim/duplicate processor commits the
                // same logical processed-event first after this worker has already dispatched the
                // read-model write. Treat the processed-event inbox as authoritative and clean up
                // the durable outbox row so the duplicate cannot be replay-amplified later.
                await DeleteOutboxResidueAfterDuplicateCompletionAsync(
                    dbContext,
                    claimedMessage.Id,
                    claimedMessage.EventId,
                    claimedMessage.ConsumerName,
                    claimedMessage.EventType,
                    cancellationToken);

                OperationalConvergenceTelemetry.RecordReplayDecision(
                    OperationalConvergencePhase.Outbox,
                    "marketplace_projection_outbox",
                    OperationalConvergenceAuthority.ProjectionEventFence,
                    OperationalConvergenceDecision.Collapsed,
                    OperationalConvergenceReason.DuplicateCompletedRace,
                    claimedMessage.TenantId);

                _logger.LogDebug(
                    "Marketplace projection completion already existed for message {MessageId}",
                    claimedMessage.Id);
                return MarketplaceProjectionOutboxProcessResult.Skipped;
            }

            _logger.LogInformation(
                "Marketplace projection outbox message processed | MessageId: {MessageId} | EventType: {EventType} | EventId: {EventId} | AttemptCount: {AttemptCount}",
                claimedMessage.Id,
                claimedMessage.EventType,
                claimedMessage.EventId,
                claimedMessage.AttemptCount);

            return MarketplaceProjectionOutboxProcessResult.Processed;
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogDebug(
                "Marketplace projection outbox concurrency conflict for message {MessageId}; another processor already handled it",
                claimedMessage.Id);
            return MarketplaceProjectionOutboxProcessResult.Skipped;
        }
        catch (Exception ex)
        {
            var attemptedCount = claimedMessage.AttemptCount + 1;
            var reachedTerminalThreshold = attemptedCount >= _options.ProjectionMaxRetryAttempts;

            if (reachedTerminalThreshold)
            {
                claimedMessage.RecordFailure(ex.Message, _options.ProjectionMaxRetryAttempts);
            }
            else
            {
                claimedMessage.RecordFailure(ex.Message, _options.ProjectionMaxRetryAttempts);
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogDebug(
                    "Concurrency conflict recording failure for marketplace projection message {MessageId}",
                    claimedMessage.Id);
                return MarketplaceProjectionOutboxProcessResult.Skipped;
            }

            _logger.LogWarning(
                ex,
                "Marketplace projection outbox processing failed | MessageId: {MessageId} | EventId: {EventId} | EventType: {EventType} | Attempt: {AttemptCount} | NextRetryAtUtc: {NextRetryAtUtc} | TerminalThresholdReached: {TerminalThresholdReached}",
                claimedMessage.Id,
                claimedMessage.EventId,
                claimedMessage.EventType,
                claimedMessage.AttemptCount,
                claimedMessage.NextRetryAtUtc,
                reachedTerminalThreshold);

            return MarketplaceProjectionOutboxProcessResult.Failed;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    private static async Task<bool> CompletionAlreadyExistsAsync(
        ApplicationDbContext dbContext,
        MarketplaceProjectionOutboxMessage message,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<ProcessedEvent>()
            .IgnoreQueryFilters()
            .AnyAsync(processed =>
                processed.EventId == message.EventId &&
                processed.ConsumerName == message.ConsumerName &&
                processed.EventType == message.EventType,
                cancellationToken);
    }

    private static async Task DeleteOutboxResidueAfterDuplicateCompletionAsync(
        ApplicationDbContext dbContext,
        Guid messageId,
        Guid eventId,
        string consumerName,
        string eventType,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();

        var completionExists = await dbContext.Set<ProcessedEvent>()
            .IgnoreQueryFilters()
            .AnyAsync(processed =>
                processed.EventId == eventId &&
                processed.ConsumerName == consumerName &&
                processed.EventType == eventType,
                cancellationToken);

        if (!completionExists)
        {
            return;
        }

        var residualMessage = await dbContext.Set<MarketplaceProjectionOutboxMessage>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == messageId, cancellationToken);

        if (residualMessage == null)
        {
            return;
        }

        dbContext.Set<MarketplaceProjectionOutboxMessage>().Remove(residualMessage);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another processor reclaimed and cleaned up the same durable row.
        }
    }

    private static async Task DispatchAsync(
        IMarketplaceEventPipeline pipeline,
        MarketplaceProjectionOutboxMessage message,
        CancellationToken cancellationToken)
    {
        var eventType = ResolveType(message.ClrType)
            ?? throw new InvalidOperationException($"Unable to resolve marketplace event type '{message.ClrType}'.");

        var payload = JsonSerializer.Deserialize(message.PayloadJson, eventType)
            ?? throw new InvalidOperationException($"Unable to deserialize marketplace projection payload for '{message.EventType}'.");

        switch (payload)
        {
            case MarketplaceEvents.OrderCreatedEvent orderCreated:
                await pipeline.ProcessAsync(orderCreated, cancellationToken);
                return;
            case MarketplaceEvents.PaymentCompletedEvent paymentCompleted:
                await pipeline.ProcessAsync(paymentCompleted, cancellationToken);
                return;
            case MarketplaceEvents.OrderDeliveredEvent orderDelivered:
                await pipeline.ProcessAsync(orderDelivered, cancellationToken);
                return;
            case MarketplaceEvents.RefundProcessedEvent refundProcessed:
                await pipeline.ProcessAsync(refundProcessed, cancellationToken);
                return;
            case MarketplaceEvents.ProductUpdatedEvent productUpdated:
                await pipeline.ProcessAsync(productUpdated, cancellationToken);
                return;
            default:
                throw new InvalidOperationException(
                    $"Marketplace projection pipeline does not support payload type '{payload.GetType().FullName}'.");
        }
    }

    private static Type? ResolveType(string clrType)
    {
        var type = Type.GetType(clrType, throwOnError: false);
        if (type != null)
        {
            return type;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(clrType, throwOnError: false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }
}

public sealed class MarketplaceProjectionOutboxWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MarketplaceProjectionOutboxWorker> _logger;
    private readonly MarketplaceEventBackboneOptions _options;

    public MarketplaceProjectionOutboxWorker(
        IServiceProvider serviceProvider,
        ILogger<MarketplaceProjectionOutboxWorker> logger,
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
                var processor = scope.ServiceProvider.GetRequiredService<MarketplaceProjectionOutboxProcessor>();
                var totalProcessed = 0;

                for (var batchIndex = 0; batchIndex < _options.MaxBatchesPerPoll; batchIndex++)
                {
                    var processed = await processor.ProcessPendingMessagesAsync(
                        _options.ProjectionOutboxBatchSize,
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
                        "Marketplace projection outbox worker processed {Count} messages",
                        totalProcessed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Marketplace projection outbox worker iteration failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.OutboxPollIntervalSeconds), stoppingToken);
        }
    }
}
