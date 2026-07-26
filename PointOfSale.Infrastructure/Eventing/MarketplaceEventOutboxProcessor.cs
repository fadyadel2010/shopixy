using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Eventing;

public class MarketplaceEventOutboxProcessor : IMarketplaceEventOutboxProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MarketplaceEventOutboxProcessor> _logger;
    private readonly MarketplaceEventBackboneOptions _options;

    public MarketplaceEventOutboxProcessor(
        IServiceProvider serviceProvider,
        ILogger<MarketplaceEventOutboxProcessor> logger,
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

        var pendingMessageIds = await batchDbContext.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.PublishedAtUtc == null &&
                !message.IsFailed &&
                (message.NextRetryAtUtc == null || message.NextRetryAtUtc <= DateTime.UtcNow) &&
                (message.ClaimedUntilUtc == null || message.ClaimedUntilUtc <= DateTime.UtcNow) &&
                !batchDbContext.Set<MarketplaceEventOutboxMessage>()
                    .IgnoreQueryFilters()
                    .Any(other =>
                        other.Id != message.Id &&
                        other.PublishedAtUtc == null &&
                        (other.AggregateRootId != Guid.Empty ? other.AggregateRootId : other.EntityId) ==
                        (message.AggregateRootId != Guid.Empty ? message.AggregateRootId : message.EntityId) &&
                        other.TenantId == message.TenantId &&
                        (other.Version < message.Version ||
                         (other.Version == message.Version && other.OccurredAtUtc < message.OccurredAtUtc) ||
                         (other.Version == message.Version &&
                          other.OccurredAtUtc == message.OccurredAtUtc &&
                          other.CreatedAt < message.CreatedAt) ||
                         (other.Version == message.Version &&
                          other.OccurredAtUtc == message.OccurredAtUtc &&
                          other.CreatedAt == message.CreatedAt &&
                          other.Id.CompareTo(message.Id) < 0))))
            // Only queue the head-of-line message for each aggregate stream. Global version ordering
            // across unrelated aggregates causes backlog amplification without improving correctness.
            .OrderBy(message => message.OccurredAtUtc)
            .ThenBy(message => message.CreatedAt)
            .ThenBy(message => message.Id) // Final tie-breaker for deterministic total ordering
            .Take(batchSize)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken);

        if (pendingMessageIds.Count == 0)
        {
            return 0;
        }

        _logger.LogInformation("Processing {Count} marketplace outbox messages", pendingMessageIds.Count);

        var publishedCount = 0;

        foreach (var messageId in pendingMessageIds)
        {
            var result = await ProcessMessageAsync(messageId, cancellationToken);
            if (result == MarketplaceEventProcessResult.Published)
            {
                publishedCount++;
            }
        }

        return publishedCount;
    }

    private async Task<MarketplaceEventProcessResult> ProcessMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        // Initial check to see if message exists and is eligible for processing
        // Use AsNoTracking for initial eligibility check to avoid stale tracked state
        var initialMessage = await dbContext.Set<MarketplaceEventOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == messageId, cancellationToken);

        if (initialMessage == null ||
            initialMessage.PublishedAtUtc != null ||
            initialMessage.IsFailed ||
            (initialMessage.NextRetryAtUtc != null && initialMessage.NextRetryAtUtc > DateTime.UtcNow))
        {
            return MarketplaceEventProcessResult.Skipped;
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        var claimResult = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // Re-query for fresh entity on each retry attempt
                // Clear tracked entities first to force a fresh DB query
                dbContext.ChangeTracker.Clear();
                
                var message = await dbContext.Set<MarketplaceEventOutboxMessage>()
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entry => entry.Id == messageId, cancellationToken);

                if (message == null)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return ClaimOutcome.AlreadyClaimed;
                }

                var idForLock = message.AggregateRootId != Guid.Empty
                    ? message.AggregateRootId
                    : message.EntityId;

                if (!message.TenantId.HasValue ||
                    message.TenantId.Value == Guid.Empty ||
                    idForLock == Guid.Empty)
                {
                    message.RecordFailure(
                        "Marketplace outbox message is missing authoritative tenant or aggregate identity.",
                        maxRetries: 1);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                    return ClaimOutcome.MessageNoLongerEligible;
                }

                if (dbContext.Database.ProviderName?.Contains("Npgsql") == true)
                {
                    var lockId = ComputeStableAdvisoryLockKey(message.TenantId.Value, idForLock);
                    await dbContext.Database.ExecuteSqlAsync(
                        $"SELECT pg_advisory_xact_lock({lockId})", cancellationToken);
                }

                if (await HasUnpublishedEarlierEntityEventAsync(dbContext, message, cancellationToken))
                {
                    await tx.RollbackAsync(cancellationToken);
                    return ClaimOutcome.Deferred;
                }

                if (!message.TryClaim(claimDurationSeconds: _options.ClaimLeaseSeconds))
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

        if (claimResult == ClaimOutcome.Deferred)
        {
            _logger.LogDebug(
                "Marketplace outbox message {MessageId} deferred until earlier entity events publish | EntityId: {EntityId} | Version: {Version} | OccurredAtUtc: {OccurredAtUtc}",
                initialMessage.Id,
                initialMessage.EntityId,
                initialMessage.Version,
                initialMessage.OccurredAtUtc);

            return MarketplaceEventProcessResult.Skipped;
        }

        if (claimResult == ClaimOutcome.AlreadyClaimed)
        {
            _logger.LogDebug(
                "Marketplace outbox message {MessageId} is already claimed or no longer claimable",
                initialMessage.Id);
            return MarketplaceEventProcessResult.Skipped;
        }

        if (claimResult == ClaimOutcome.MessageNoLongerEligible)
        {
            _logger.LogError(
                "Marketplace outbox message {MessageId} failed closed because tenant or aggregate authority metadata was missing",
                initialMessage.Id);

            return MarketplaceEventProcessResult.Failed;
        }

        if (claimResult == ClaimOutcome.ConcurrencyConflict)
        {
            _logger.LogDebug(
                "Concurrency conflict claiming marketplace outbox message {MessageId}; another worker claimed it first",
                initialMessage.Id);

            return MarketplaceEventProcessResult.Skipped;
        }

        // Reload message after successful claim to get the updated state with tracking
        // Clear tracked entities first to force a fresh DB query with updated claim fields
        dbContext.ChangeTracker.Clear();
        
        var claimedMessage = await dbContext.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == messageId, cancellationToken);

        if (claimedMessage == null)
        {
            _logger.LogWarning(
                "Marketplace outbox message {MessageId} disappeared after claim",
                messageId);
            return MarketplaceEventProcessResult.Skipped;
        }

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = claimedMessage.CorrelationId,
            ["TenantId"] = claimedMessage.TenantId,
            ["EventId"] = claimedMessage.EventId,
            ["EventType"] = claimedMessage.EventType,
            ["OutboxMessageId"] = claimedMessage.Id,
        });

        try
        {
            await PublishAsync(publishEndpoint, claimedMessage, cancellationToken);
            claimedMessage.MarkPublished();
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Marketplace event published | MessageId: {MessageId} | EventType: {EventType} | EventId: {EventId} | RetryCount: {RetryCount}",
                claimedMessage.Id,
                claimedMessage.EventType,
                claimedMessage.EventId,
                claimedMessage.AttemptCount);

            return MarketplaceEventProcessResult.Published;
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogDebug(
                "Concurrency conflict publishing message {MessageId} - another processor already published it",
                claimedMessage.Id);

            return MarketplaceEventProcessResult.Skipped;
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
                claimedMessage.RecordNonTerminalProjectionFailure(ex.Message);
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
                                "marketplace-outbox",
                                claimedMessage.Id,
                                claimedMessage.EventId,
                                claimedMessage.EventType,
                                claimedMessage.TenantId,
                                ex.Message,
                                DateTime.UtcNow,
                                claimedMessage.AttemptCount),
                            cancellationToken);
                    }
                    catch (Exception compensationEx)
                    {
                        _logger.LogCritical(
                            compensationEx,
                            "Marketplace projection compensation failed after terminal publish failure | MessageId: {MessageId} | EventType: {EventType}",
                            claimedMessage.Id,
                            claimedMessage.EventType);
                    }
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogDebug(
                    "Concurrency conflict recording failure for message {MessageId} - another processor modified it",
                    claimedMessage.Id);
                return MarketplaceEventProcessResult.Skipped;
            }

            _logger.LogWarning(
                ex,
                "Marketplace event publish failed | MessageId: {MessageId} | EventId: {EventId} | EventType: {EventType} | Attempt: {AttemptCount} | NextRetryAtUtc: {NextRetryAtUtc} | FailureReason: {FailureReason} | CorrelationId: {CorrelationId} | TerminalThresholdReached: {TerminalThresholdReached}",
                claimedMessage.Id,
                claimedMessage.EventId,
                claimedMessage.EventType,
                claimedMessage.AttemptCount,
                claimedMessage.NextRetryAtUtc,
                claimedMessage.LastError,
                claimedMessage.CorrelationId,
                reachedTerminalThreshold);

            return MarketplaceEventProcessResult.Failed;
        }
    }

    public async Task<bool> RetryFailedMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var message = await dbContext.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == messageId && entry.IsFailed, cancellationToken);

        if (message == null)
        {
            return false;
        }

        message.ResetForRetry();
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Marketplace outbox failed message reset for retry | MessageId: {MessageId} | EventType: {EventType} | EventId: {EventId}",
            message.Id,
            message.EventType,
            message.EventId);

        return true;
    }

    private static async Task PublishAsync(
        IPublishEndpoint publishEndpoint,
        MarketplaceEventOutboxMessage message,
        CancellationToken cancellationToken)
    {
        var eventType = ResolveType(message.ClrType);
        if (eventType == null)
        {
            throw new InvalidOperationException($"Unable to resolve event CLR type '{message.ClrType}'.");
        }

        var payload = JsonSerializer.Deserialize(message.PayloadJson, eventType);
        if (payload == null)
        {
            throw new InvalidOperationException($"Unable to deserialize payload for {message.EventType}.");
        }

        await publishEndpoint.Publish(payload, eventType, context =>
        {
            context.MessageId = message.EventId;
            context.Headers.Set("marketplace-event-id", message.EventId);
            context.Headers.Set("marketplace-event-type", message.EventType);
            context.Headers.Set("marketplace-event-version", message.Version);
            context.Headers.Set("correlation-id", message.CorrelationId);
            context.Headers.Set("entity-id", message.EntityId);

            if (message.TenantId.HasValue)
            {
                context.Headers.Set("tenant-id", message.TenantId.Value);
            }

            if (Guid.TryParse(message.CorrelationId, out var correlationGuid))
            {
                context.CorrelationId = correlationGuid;
            }
        }, cancellationToken);
    }

    private static Task<bool> HasUnpublishedEarlierEntityEventAsync(
        ApplicationDbContext dbContext,
        MarketplaceEventOutboxMessage message,
        CancellationToken cancellationToken)
    {
        // S56: Version is the source-mutation ordering key. Time is only a tie-breaker.
        // Failed critical marketplace messages remain blockers. SQL is the
        // authority, and downstream systems must not observe newer aggregate
        // events while an older event in the same stream is unresolved.
        // Use AggregateRootId if present, otherwise fall back to EntityId for lock consistency
        var idForComparison = message.AggregateRootId != Guid.Empty
            ? message.AggregateRootId
            : message.EntityId;
        
        return dbContext.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(other =>
                other.Id != message.Id &&
                other.PublishedAtUtc == null &&
                (other.AggregateRootId != Guid.Empty ? other.AggregateRootId : other.EntityId) == idForComparison &&
                other.TenantId == message.TenantId &&
                (other.Version < message.Version ||
                 (other.Version == message.Version && other.OccurredAtUtc < message.OccurredAtUtc) ||
                 (other.Version == message.Version &&
                  other.OccurredAtUtc == message.OccurredAtUtc &&
                  other.CreatedAt < message.CreatedAt) ||
                 (other.Version == message.Version &&
                  other.OccurredAtUtc == message.OccurredAtUtc &&
                  other.CreatedAt == message.CreatedAt &&
                  other.Id.CompareTo(message.Id) < 0)),
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

    private static long ComputeStableAdvisoryLockKey(Guid tenantId, Guid aggregateId)
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

            foreach (var value in aggregateId.ToByteArray())
            {
                hash ^= value;
                hash *= fnvPrime;
            }

            return (long)hash;
        }
    }
}

internal enum MarketplaceEventProcessResult
{
    Skipped = 0,
    Published = 1,
    Failed = 2
}
