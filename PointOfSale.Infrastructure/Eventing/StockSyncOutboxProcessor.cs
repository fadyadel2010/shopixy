using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Eventing;

public class StockSyncOutboxProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<StockSyncOutboxProcessor> _logger;
    private readonly StockSyncOutboxOptions _options;

    public StockSyncOutboxProcessor(
        IServiceProvider serviceProvider,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<StockSyncOutboxProcessor> logger,
        IOptions<StockSyncOutboxOptions> options)
    {
        _serviceProvider = serviceProvider;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<int> ProcessPendingMessagesAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        using var batchScope = _serviceProvider.CreateScope();
        var batchDbContext = batchScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var nowUtc = DateTime.UtcNow;

        var pendingMessageIds = await batchDbContext.Set<StockSyncOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.PublishedAtUtc == null &&
                !message.IsFailed &&
                (message.NextRetryAtUtc == null || message.NextRetryAtUtc <= nowUtc) &&
                (message.ClaimedUntilUtc == null || message.ClaimedUntilUtc <= nowUtc)) // Exclude currently claimed messages
            // S38C: Canonical ordering: StockUpdatedAtUtc → OccurredAtUtc → CreatedAt → Id
            // Must match HasUnpublishedEarlierVariantEventAsync to prevent starvation
            .OrderBy(message => message.EventVersion)
            .ThenBy(message => message.StockUpdatedAtUtc)
            .ThenBy(message => message.OccurredAtUtc)
            .ThenBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(batchSize)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken);

        if (pendingMessageIds.Count == 0)
        {
            return 0;
        }

        var publishedCount = 0;

        foreach (var messageId in pendingMessageIds)
        {
            var result = await ProcessMessageAsync(messageId, cancellationToken);
            if (result == StockSyncProcessResult.Published)
            {
                publishedCount++;
            }
        }

        return publishedCount;
    }

    private async Task<StockSyncProcessResult> ProcessMessageAsync(Guid messageId, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        // Initial eligibility check - use AsNoTracking to avoid stale tracked state
        var message = await dbContext.Set<StockSyncOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == messageId, cancellationToken);

        if (message == null ||
            message.PublishedAtUtc != null ||
            message.IsFailed ||
            (message.NextRetryAtUtc != null && message.NextRetryAtUtc > DateTime.UtcNow))
        {
            return StockSyncProcessResult.Skipped;
        }

        // AF-38/RISK-2 FIX: Wrap check and claim in an atomic transaction with PG advisory lock
        // to close the multi-instance race window between HasUnpublishedEarlierVariantEventAsync and TryClaim.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        var claimResult = await strategy.ExecuteAsync(async () =>
        {
            // PRODUCTION HARDENING: Reload message inside ExecuteAsync to ensure each retry
            // sees the current DB state, avoiding stale in-memory claim state on retries.
            // Clear tracked entities first to force a fresh DB query
            dbContext.ChangeTracker.Clear();
            
            var freshMessage = await dbContext.Set<StockSyncOutboxMessage>()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(entry => entry.Id == messageId, cancellationToken);

            if (freshMessage == null ||
                freshMessage.PublishedAtUtc != null ||
                freshMessage.IsFailed ||
                (freshMessage.NextRetryAtUtc != null && freshMessage.NextRetryAtUtc > DateTime.UtcNow))
            {
                return ClaimOutcome.MessageNoLongerEligible;
            }

            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                if (dbContext.Database.ProviderName?.Contains("Npgsql") == true)
                {
                    var lockId = ComputeStableAdvisoryLockKey(
                        freshMessage.TenantId,
                        freshMessage.VariantId,
                        freshMessage.WarehouseId);
                    await dbContext.Database.ExecuteSqlAsync(
                        $"SELECT pg_advisory_xact_lock({lockId})", cancellationToken);
                }

                if (await HasUnpublishedEarlierVariantEventAsync(dbContext, freshMessage, cancellationToken))
                {
                    await tx.RollbackAsync(cancellationToken);
                    return ClaimOutcome.Deferred;
                }

                if (!freshMessage.TryClaim(claimDurationSeconds: _options.ClaimLeaseSeconds))
                {
                    await tx.RollbackAsync(cancellationToken);
                    return ClaimOutcome.AlreadyClaimed;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return ClaimOutcome.Claimed;
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(cancellationToken);
                return ClaimOutcome.ConcurrencyConflict;
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });

        if (claimResult == ClaimOutcome.MessageNoLongerEligible)
        {
            _logger.LogDebug(
                "Stock sync outbox message {MessageId} is no longer eligible for processing",
                messageId);
            return StockSyncProcessResult.Skipped;
        }

        if (claimResult == ClaimOutcome.Deferred)
        {
            _logger.LogDebug(
                "Stock sync outbox message {MessageId} deferred until earlier warehouse-scoped variant events publish | TenantId: {TenantId} | VariantId: {VariantId} | WarehouseId: {WarehouseId} | StockUpdatedAtUtc: {StockUpdatedAtUtc}",
                message.Id, message.TenantId, message.VariantId, message.WarehouseId, message.StockUpdatedAtUtc);
            return StockSyncProcessResult.Skipped;
        }
        if (claimResult == ClaimOutcome.AlreadyClaimed)
        {
            _logger.LogDebug("Stock sync outbox message {MessageId} is already claimed or not claimable", message.Id);
            return StockSyncProcessResult.Skipped;
        }
        if (claimResult == ClaimOutcome.ConcurrencyConflict)
        {
            _logger.LogDebug("Stock sync outbox concurrency conflict while claiming message {MessageId}; another worker claimed it first", message.Id);
            return StockSyncProcessResult.Skipped;
        }

        // PRODUCTION HARDENING: Reload message after successful claim to get the updated state
        // (ClaimedUntilUtc, UpdatedAt, etc.) before processing
        // Clear tracked entities first to force a fresh DB query with updated claim fields
        dbContext.ChangeTracker.Clear();
        
        message = await dbContext.Set<StockSyncOutboxMessage>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == messageId, cancellationToken);

        if (message == null)
        {
            _logger.LogWarning(
                "Stock sync outbox message {MessageId} disappeared after claim",
                messageId);
            return StockSyncProcessResult.Skipped;
        }

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = message.TenantId,
            ["VariantId"] = message.VariantId,
            ["WarehouseId"] = message.WarehouseId,
            ["ProductId"] = message.ProductId,
            ["EventId"] = message.EventId,
            ["OutboxMessageId"] = message.Id,
            ["CorrelationId"] = message.CorrelationId
        });

        // STEP 2: Now that we own the claim, publish the message
        try
        {
            var suppressionReason = await ResolveStockSyncSuppressionReasonAsync(
                dbContext,
                message,
                cancellationToken);
            if (suppressionReason != null)
            {
                message.MarkPublished();
                await dbContext.SaveChangesAsync(cancellationToken);

                OperationalConvergenceTelemetry.RecordReplayDecision(
                    OperationalConvergencePhase.Outbox,
                    "stock_sync_outbox",
                    OperationalConvergenceAuthority.ProductVariantInventory,
                    OperationalConvergenceDecision.Suppressed,
                    OperationalConvergenceTelemetry.ReasonFromStockSyncSuppression(suppressionReason),
                    message.TenantId);

                _logger.LogWarning(
                    "Stock sync outbox replay suppressed by SQL inventory lineage authority | MessageId: {MessageId} | EventId: {EventId} | VariantId: {VariantId} | WarehouseId: {WarehouseId} | Reason: {Reason}",
                    message.Id,
                    message.EventId,
                    message.VariantId,
                    message.WarehouseId,
                    suppressionReason);

                return StockSyncProcessResult.Published;
            }

            await PublishAsync(publishEndpoint, message, cancellationToken);
            message.MarkPublished();
            await dbContext.SaveChangesAsync(cancellationToken);

            var lagSeconds = Math.Max(0d, (DateTime.UtcNow - message.OccurredAtUtc).TotalSeconds);
            _reliabilityTelemetry.RecordStockSyncDeliveryLag("outbox_dispatch", lagSeconds);

            _logger.LogInformation(
                "Stock sync outbox published | MessageId: {MessageId} | EventId: {EventId} | VariantId: {VariantId} | AttemptCount: {AttemptCount}",
                message.Id,
                message.EventId,
                message.VariantId,
                message.AttemptCount);

            return StockSyncProcessResult.Published;
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogDebug(
                "Stock sync outbox concurrency conflict for message {MessageId}; another worker already handled it",
                message.Id);
            return StockSyncProcessResult.Skipped;
        }
        catch (Exception ex)
        {
            var attemptedCount = message.AttemptCount + 1;
            var reachedTerminalThreshold = attemptedCount >= _options.MaxRetryAttempts;

            if (reachedTerminalThreshold)
            {
                message.RecordFailure(ex.Message, _options.MaxRetryAttempts);
            }
            else
            {
                message.RecordNonTerminalProjectionFailure(ex.Message);
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                if (reachedTerminalThreshold)
                {
                    try
                    {
                        var compensation = scope.ServiceProvider.GetRequiredService<IProjectionFailureCompensationService>();
                        await compensation.HandleTerminalFailureAsync(
                            new ProjectionOutboxTerminalFailure(
                                "stock-sync-outbox",
                                message.Id,
                                message.EventId,
                                message.EventType,
                                message.TenantId,
                                ex.Message,
                                DateTime.UtcNow,
                                message.AttemptCount),
                            cancellationToken);
                    }
                    catch (Exception compensationEx)
                    {
                        _logger.LogCritical(
                            compensationEx,
                            "Stock sync projection compensation failed after terminal publish failure | MessageId: {MessageId} | EventType: {EventType}",
                            message.Id,
                            message.EventType);
                    }
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogDebug(
                    "Stock sync outbox concurrency conflict while recording failure for message {MessageId}",
                    message.Id);
                return StockSyncProcessResult.Skipped;
            }

            _reliabilityTelemetry.RecordStockSyncRetry(message.EventType, message.AttemptCount);
            _reliabilityTelemetry.RecordStockSyncFailure(
                "outbox_dispatch",
                message.EventType,
                terminal: message.IsFailed);

            if (message.IsFailed || message.AttemptCount >= 3)
            {
                _logger.LogError(
                    ex,
                    "Stock sync outbox publish failed repeatedly | MessageId: {MessageId} | EventId: {EventId} | Attempt: {AttemptCount} | NextRetryAtUtc: {NextRetryAtUtc} | Terminal: {Terminal} | Error: {Error}",
                    message.Id,
                    message.EventId,
                    message.AttemptCount,
                    message.NextRetryAtUtc,
                    message.IsFailed || reachedTerminalThreshold,
                    message.LastError);
            }
            else
            {
                _logger.LogWarning(
                    ex,
                    "Stock sync outbox publish failed | MessageId: {MessageId} | EventId: {EventId} | Attempt: {AttemptCount} | NextRetryAtUtc: {NextRetryAtUtc} | Error: {Error}",
                    message.Id,
                    message.EventId,
                    message.AttemptCount,
                    message.NextRetryAtUtc,
                    message.LastError);
            }

            return StockSyncProcessResult.Failed;
        }
    }

    public async Task<bool> RetryFailedMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var message = await dbContext.Set<StockSyncOutboxMessage>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == messageId && entry.IsFailed, cancellationToken);

        if (message == null)
        {
            return false;
        }

        message.ResetForRetry();
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Stock sync outbox failed message reset for retry | MessageId: {MessageId} | EventType: {EventType} | EventId: {EventId} | VariantId: {VariantId}",
            message.Id,
            message.EventType,
            message.EventId,
            message.VariantId);

        return true;
    }

    private static async Task PublishAsync(
        IPublishEndpoint publishEndpoint,
        StockSyncOutboxMessage message,
        CancellationToken cancellationToken)
    {
        var eventType = ResolveType(message.ClrType);
        if (eventType == null)
        {
            throw new InvalidOperationException($"Unable to resolve stock sync event CLR type '{message.ClrType}'.");
        }

        var payload = JsonSerializer.Deserialize(message.PayloadJson, eventType);
        if (payload == null)
        {
            throw new InvalidOperationException($"Unable to deserialize stock sync payload for {message.EventType}.");
        }

        await publishEndpoint.Publish(payload, eventType, context =>
        {
            context.MessageId = message.EventId;
            context.Headers.Set("stock-sync-event-id", message.EventId);
            context.Headers.Set("stock-sync-event-type", message.EventType);
            context.Headers.Set("tenant-id", message.TenantId);
            context.Headers.Set("variant-id", message.VariantId);
            context.Headers.Set("warehouse-id", message.WarehouseId);
            context.Headers.Set("product-id", message.ProductId);
            context.Headers.Set("event-version", message.EventVersion);
            context.Headers.Set("correlation-id", message.CorrelationId);

            if (Guid.TryParse(message.CorrelationId, out var correlationGuid))
            {
                context.CorrelationId = correlationGuid;
            }
        }, cancellationToken);
    }

    private static async Task<string?> ResolveStockSyncSuppressionReasonAsync(
        ApplicationDbContext dbContext,
        StockSyncOutboxMessage message,
        CancellationToken cancellationToken)
    {
        var variantAuthority = await dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(variant =>
                variant.TenantId == message.TenantId &&
                variant.Id == message.VariantId)
            .Select(variant => new
            {
                variant.IsDeleted,
                variant.IsActive,
                variant.ProductId,
                variant.SemanticLifecycleState,
                variant.SemanticSupersededAtUtc,
                variant.SemanticReplacementVariantId,
                variant.ProjectionMutationVersion,
                variant.StockUpdatedAtUtc,
                ProductDeleted = variant.Product.IsDeleted,
                ProductActive = variant.Product.IsActive
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (variantAuthority == null ||
            variantAuthority.IsDeleted ||
            !variantAuthority.IsActive ||
            variantAuthority.ProductId == Guid.Empty ||
            variantAuthority.SemanticLifecycleState != ProductVariantSemanticLifecycleState.Current ||
            variantAuthority.SemanticSupersededAtUtc != null ||
            variantAuthority.SemanticReplacementVariantId != null ||
            variantAuthority.ProductDeleted ||
            !variantAuthority.ProductActive)
        {
            return "VariantInventoryLineageLost";
        }

        if (variantAuthority.ProjectionMutationVersion > message.EventVersion &&
            variantAuthority.StockUpdatedAtUtc.HasValue &&
            variantAuthority.StockUpdatedAtUtc.Value > message.StockUpdatedAtUtc)
        {
            return "StaleStockMutationVersion";
        }

        var stockStillOwned = await dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .WhereCurrentInventoryStockOwner(message.TenantId)
            .AnyAsync(stock =>
                stock.VariantId == message.VariantId &&
                stock.WarehouseId == message.WarehouseId,
                cancellationToken);

        return stockStillOwned ? null : "WarehouseStockLineageLost";
    }

    private static Task<bool> HasUnpublishedEarlierVariantEventAsync(
        ApplicationDbContext dbContext,
        StockSyncOutboxMessage message,
        CancellationToken cancellationToken)
    {
        // S38C: Canonical ordering: StockUpdatedAtUtc → OccurredAtUtc → CreatedAt → Id
        // Must match batch selection ordering to prevent starvation
        // Failed critical stock messages remain blockers. SQL is the authority, and
        // downstream systems must not observe newer stock events while an older
        // event in the same warehouse-scoped variant stream is unresolved.
        return dbContext.Set<StockSyncOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(other =>
                other.Id != message.Id &&
                other.PublishedAtUtc == null &&
                other.TenantId == message.TenantId &&
                other.VariantId == message.VariantId &&
                other.WarehouseId == message.WarehouseId &&
                (other.EventVersion < message.EventVersion ||
                 (other.EventVersion == message.EventVersion && other.StockUpdatedAtUtc < message.StockUpdatedAtUtc) ||
                 (other.EventVersion == message.EventVersion &&
                  other.StockUpdatedAtUtc == message.StockUpdatedAtUtc &&
                  other.OccurredAtUtc < message.OccurredAtUtc) ||
                 (other.EventVersion == message.EventVersion &&
                  other.StockUpdatedAtUtc == message.StockUpdatedAtUtc &&
                  other.OccurredAtUtc == message.OccurredAtUtc &&
                  other.CreatedAt < message.CreatedAt) ||
                 (other.EventVersion == message.EventVersion &&
                 (other.StockUpdatedAtUtc == message.StockUpdatedAtUtc &&
                  other.OccurredAtUtc == message.OccurredAtUtc &&
                  other.CreatedAt == message.CreatedAt &&
                  other.Id.CompareTo(message.Id) < 0))),
                cancellationToken);
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

    private static long ComputeStableAdvisoryLockKey(Guid tenantId, Guid variantId, Guid warehouseId)
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

            foreach (var value in variantId.ToByteArray())
            {
                hash ^= value;
                hash *= fnvPrime;
            }

            foreach (var value in warehouseId.ToByteArray())
            {
                hash ^= value;
                hash *= fnvPrime;
            }

            return (long)hash;
        }
    }
}

internal enum StockSyncProcessResult
{
    Skipped = 0,
    Published = 1,
    Failed = 2
}

/// <summary>
/// Internal result type for the atomic claim transaction.
/// </summary>
internal enum ClaimOutcome
{
    Claimed = 0,
    Deferred = 1,
    AlreadyClaimed = 2,
    ConcurrencyConflict = 3,
    MessageNoLongerEligible = 4
}
