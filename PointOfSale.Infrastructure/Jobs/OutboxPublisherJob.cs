using System.Linq.Expressions;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Jobs;

/// <summary>
/// Background job that publishes outbox messages to RabbitMQ.
/// Phase: Abandoned Cart Hardening
/// 
/// GUARANTEES:
/// - At-least-once delivery
/// - Ordered processing per batch
/// - Retry on failure
/// - Cleanup after processing
/// 
/// RUNS: Every 5 seconds via Hangfire recurring job
/// </summary>
public class OutboxPublisherJob
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxPublisherJob> _logger;
    
    private const int BatchSize = 100;
    private const int RetentionDays = 7;
    private static readonly TimeSpan CriticalPoisonSlowReplayInterval = TimeSpan.FromMinutes(15);

    public OutboxPublisherJob(
        IServiceProvider serviceProvider,
        ILogger<OutboxPublisherJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Main execution method called by Hangfire.
    /// </summary>
    public async Task ExecuteAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            // 1. Process pending messages
            await ProcessPendingMessagesAsync(context);

            // 2. Cleanup old processed messages
            await CleanupOldMessagesAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Outbox] Unexpected error in publisher job");
        }
    }

    private async Task ProcessPendingMessagesAsync(ApplicationDbContext context)
    {
        // Select all unprocessed messages. Critical aggregate streams are batched
        // in the same version-aware order used by HasUnprocessedEarlierCriticalAggregateMessageAsync
        // so older event versions are pulled before newer ones for the same aggregate.
        var pendingMessages = context.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(m => m.ProcessedAtUtc == null);

        await MarkExhaustedCriticalMessagesForOperatorReconciliationAsync(context);

        // Critical poison rows remain operator-visible, but they are not terminal dead letters.
        // Keep them on a slow replay cadence so a transient downstream outage cannot leave POS
        // committed while storefront/marketplace/accounting projections are stale forever.
        var criticalPoisonReplayCutoffUtc = DateTime.UtcNow.Subtract(CriticalPoisonSlowReplayInterval);
        var criticalMessageIds = await pendingMessages
            .Where(BuildCriticalEventTypeFilter())
            .Where(m =>
                m.RetryCount < OutboxMessage.MaxRetries ||
                m.LastErrorAtUtc == null ||
                m.LastErrorAtUtc <= criticalPoisonReplayCutoffUtc)
            .OrderBy(m => m.TenantId)
            .ThenBy(m => m.AggregateId)
            .ThenBy(m => m.EventVersion)
            .ThenBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .Take(BatchSize)
            .Select(m => m.Id)
            .ToListAsync();

        var messageIds = new List<Guid>(BatchSize);
        messageIds.AddRange(criticalMessageIds);

        var remainingSlots = BatchSize - messageIds.Count;
        if (remainingSlots > 0)
        {
            // Apply RetryCount filtering only for non-critical messages
            var nonCriticalMessageIds = await pendingMessages
                .Where(BuildNonCriticalEventTypeFilter())
                .Where(m => m.RetryCount < OutboxMessage.MaxRetries)
                .OrderBy(m => m.CreatedAtUtc)
                .ThenBy(m => m.Id)
                .Take(remainingSlots)
                .Select(m => m.Id)
                .ToListAsync();

            messageIds.AddRange(nonCriticalMessageIds);
        }

        if (!messageIds.Any())
        {
            return;
        }

        _logger.LogDebug("[Outbox] Processing {Count} pending messages", messageIds.Count);

        var successCount = 0;
        var failCount = 0;

        foreach (var messageId in messageIds)
        {
            var result = await ProcessMessageAsync(messageId);

            if (result.Succeeded)
            {
                successCount++;
            }
            else if (result.Attempted)
            {
                failCount++;
            }
        }

        if (successCount > 0 || failCount > 0)
        {
            _logger.LogInformation(
                "[Outbox] Published {Success} messages, {Failed} failed",
                successCount, failCount);
        }
    }

    private static async Task MarkExhaustedCriticalMessagesForOperatorReconciliationAsync(ApplicationDbContext context)
    {
        var exhaustedCriticalMessages = await context.Set<OutboxMessage>()
            .Where(message =>
                message.ProcessedAtUtc == null &&
                message.RetryCount >= OutboxMessage.MaxRetries &&
                !message.RequiresOperatorReconciliation)
            .Where(BuildCriticalEventTypeFilter())
            .OrderBy(message => message.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync();

        if (exhaustedCriticalMessages.Count == 0)
        {
            return;
        }

        // Save each message individually to avoid batch failure on concurrency conflicts
        foreach (var message in exhaustedCriticalMessages)
        {
            MarkRequiresOperatorReconciliation(
                message,
                $"Critical authoritative event exhausted {OutboxMessage.MaxRetries} publish attempts and is blocking its aggregate stream.");

            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // Message was concurrently updated - skip it and continue with remaining messages
                // The concurrent update may have already marked it or processed it
                context.Entry(message).State = EntityState.Detached;
            }
        }
    }

    private static void MarkRequiresOperatorReconciliation(OutboxMessage message, string reason)
    {
        message.RequiresOperatorReconciliation = true;
        message.OperatorReconciliationRequiredAtUtc ??= DateTime.UtcNow;
        message.OperatorReconciliationReason = reason.Length > 2000 ? reason[..2000] : reason;
    }

    private async Task<OutboxProcessResult> ProcessMessageAsync(Guid messageId)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        IDbContextTransaction? transaction = null;

        try
        {
            if (context.Database.IsRelational())
            {
                transaction = await context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable);
            }

            var message = await LoadMessageForMutationAsync(context, messageId);
            if (message == null || message.ProcessedAtUtc != null)
            {
                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }

                return new OutboxProcessResult(Attempted: false, Succeeded: false);
            }

            var eventType = ResolveEventType(message.EventType);
            var isCriticalOutboxEvent = IsCriticalOutboxEvent(message.EventType, eventType);
            if (message.RetryCount >= OutboxMessage.MaxRetries && isCriticalOutboxEvent)
            {
                MarkRequiresOperatorReconciliation(
                    message,
                    $"Critical authoritative event exhausted {OutboxMessage.MaxRetries} publish attempts and is blocking its aggregate stream; automatic slow replay remains active.");

                await SaveChangesWithConcurrencyHandlingAsync(context, message.Id);

                _logger.LogCritical(
                    "[Outbox] CRITICAL POISON MESSAGE - aggregate stream is operator-visible and automatic slow replay is attempting recovery | MessageId: {MessageId} | EventType: {EventType} | TenantId: {TenantId} | AggregateId: {AggregateId} | EventVersion: {EventVersion} | RetryCount: {RetryCount} | LastError: {LastError}",
                    message.Id,
                    message.EventType,
                    message.TenantId,
                    message.AggregateId,
                    message.EventVersion,
                    message.RetryCount,
                    message.LastErrorMessage);
            }

            if (message.RetryCount >= OutboxMessage.MaxRetries && !isCriticalOutboxEvent)
            {
                // Terminal failure: non-critical message has exhausted retries
                // Mark as processed to prevent it from blocking newer messages
                _logger.LogWarning(
                    "[Outbox] Terminal failure - non-critical message exhausted retries | MessageId: {MessageId} | EventType: {EventType} | RetryCount: {RetryCount}",
                    message.Id, message.EventType, message.RetryCount);
                
                message.ProcessedAtUtc = DateTime.UtcNow;
                message.LastErrorMessage = $"Terminal failure: exhausted {OutboxMessage.MaxRetries} retries (non-critical event)";
                message.LastErrorAtUtc = DateTime.UtcNow;
                
                try
                {
                    await SaveChangesWithConcurrencyHandlingAsync(context, message.Id);
                    if (transaction != null)
                    {
                        await transaction.CommitAsync();
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Another worker already processed this message
                    _logger.LogDebug(
                        "[Outbox] Concurrency conflict on terminal failure - another worker already processed | MessageId: {MessageId} | MaxRetries: {MaxRetries}",
                        message.Id, OutboxMessage.MaxRetries);
                    
                    if (transaction != null)
                    {
                        await transaction.RollbackAsync();
                        await transaction.DisposeAsync();
                        transaction = null;
                    }
                }

                return new OutboxProcessResult(Attempted: false, Succeeded: false);
            }

            if (eventType == null)
            {
                _logger.LogWarning(
                    "[Outbox] Unknown event type: {EventType} | MessageId: {MessageId}",
                    message.EventType, message.Id);

                message.RetryCount++;
                message.LastErrorMessage = $"Unknown event type: {message.EventType}";
                message.LastErrorAtUtc = DateTime.UtcNow;
                await SaveChangesWithConcurrencyHandlingAsync(context, message.Id);
                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }

                return new OutboxProcessResult(Attempted: true, Succeeded: false);
            }

            var evt = JsonSerializer.Deserialize(message.EventPayload, eventType);
            if (evt == null)
            {
                _logger.LogWarning(
                    "[Outbox] Failed to deserialize event | MessageId: {MessageId}",
                    message.Id);

                message.RetryCount++;
                message.LastErrorMessage = "Deserialization failed";
                message.LastErrorAtUtc = DateTime.UtcNow;
                await SaveChangesWithConcurrencyHandlingAsync(context, message.Id);
                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }

                return new OutboxProcessResult(Attempted: true, Succeeded: false);
            }

            if (evt is IntegrationEvent integrationEvent)
            {
                EnsureOutboxAuthorityScope(message, eventType, integrationEvent, isCriticalOutboxEvent);
            }

            if (isCriticalOutboxEvent &&
                await HasUnprocessedEarlierCriticalAggregateMessageAsync(context, message))
            {
                _logger.LogInformation(
                    "[Outbox] Deferred critical message until earlier aggregate message publishes | MessageId: {MessageId} | EventType: {EventType} | TenantId: {TenantId} | AggregateId: {AggregateId}",
                    message.Id,
                    message.EventType,
                    message.TenantId,
                    message.AggregateId);

                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                    await transaction.DisposeAsync();
                    transaction = null;
                }

                return new OutboxProcessResult(Attempted: false, Succeeded: false);
            }

            await publishEndpoint.Publish(evt, eventType, publishContext =>
            {
                if (evt is IntegrationEvent integrationEvent)
                {
                    publishContext.MessageId = integrationEvent.EventId;
                    
                    // Only set publishContext.CorrelationId if it's a valid GUID
                    // Otherwise, preserve the original string in a header to avoid losing W3C trace IDs
                    var parsedCorrelationId = TryParseCorrelationId(integrationEvent.CorrelationId);
                    if (parsedCorrelationId.HasValue)
                    {
                        publishContext.CorrelationId = parsedCorrelationId.Value;
                    }
                    
                    // Always set the raw correlation ID as a header to preserve non-GUID formats
                    if (!string.IsNullOrWhiteSpace(integrationEvent.CorrelationId))
                    {
                        publishContext.Headers.Set("correlation-id", integrationEvent.CorrelationId);
                    }
                    
                    publishContext.Headers.Set("event-id", integrationEvent.EventId);
                    publishContext.Headers.Set("idempotency-key", integrationEvent.IdempotencyKey);
                    publishContext.Headers.Set("event-version", integrationEvent.EventVersion);
                    publishContext.Headers.Set("aggregate-id", message.AggregateId);
                }
            });

            message.ProcessedAtUtc = DateTime.UtcNow;
            message.RequiresOperatorReconciliation = false;
            message.OperatorReconciliationReason = null;
            
            try
            {
                await SaveChangesWithConcurrencyHandlingAsync(context, message.Id);
                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                    await transaction.DisposeAsync();
                    transaction = null;
                }

                // Another worker already processed this message
                _logger.LogDebug(
                    "[Outbox] Concurrency conflict - message already processed | MessageId: {MessageId}",
                    message.Id);
                return new OutboxProcessResult(Attempted: true, Succeeded: true);
            }

            _logger.LogDebug(
                "[Outbox] Published message | MessageId: {MessageId} | EventType: {EventType} | RetryCount: {RetryCount}",
                message.Id, message.EventType, message.RetryCount);

            return new OutboxProcessResult(Attempted: true, Succeeded: true);
        }
        catch (Exception ex)
        {
            // Rollback the transaction first to discard any changes (including ProcessedAtUtc)
            if (transaction != null)
            {
                await transaction.RollbackAsync();
                await transaction.DisposeAsync();
                transaction = null;
            }

            // Detach the tracked entity to ensure we get a fresh copy from the database
            var trackedEntry = context.ChangeTracker.Entries<OutboxMessage>()
                .FirstOrDefault(e => e.Entity.Id == messageId);
            if (trackedEntry != null)
            {
                trackedEntry.State = EntityState.Detached;
            }

            // Start a new transaction for the error update
            if (context.Database.IsRelational())
            {
                transaction = await context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable);
            }

            // Reload the message fresh from the database
            var message = await context.Set<OutboxMessage>()
                .FirstOrDefaultAsync(m => m.Id == messageId);
            if (message == null)
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                    await transaction.DisposeAsync();
                    transaction = null;
                }

                return new OutboxProcessResult(Attempted: false, Succeeded: false);
            }

            // Update only the error metadata fields
            message.RetryCount++;
            message.LastErrorMessage = ex.Message.Length > 2000
                ? ex.Message.Substring(0, 2000)
                : ex.Message;
            message.LastErrorAtUtc = DateTime.UtcNow;
            
            try
            {
                await SaveChangesWithConcurrencyHandlingAsync(context, message.Id);
                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                    await transaction.DisposeAsync();
                    transaction = null;
                }

                // Another worker already processed this message, ignore
                _logger.LogDebug(
                    "[Outbox] Concurrency conflict on error update | MessageId: {MessageId}",
                    message.Id);
            }

            _logger.LogWarning(
                ex,
                "[Outbox] Failed to publish message | MessageId: {MessageId} | RetryCount: {RetryCount} | FailureReason: {FailureReason}",
                message.Id, message.RetryCount, message.LastErrorMessage);

            return new OutboxProcessResult(Attempted: true, Succeeded: false);
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static async Task<OutboxMessage?> LoadMessageForMutationAsync(
        ApplicationDbContext context,
        Guid messageId)
    {
        if (context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await context.Set<OutboxMessage>().FromSqlInterpolated($@"
                -- Include xmin explicitly because PostgreSQL system columns are not part of SELECT *.
                -- OutboxMessage uses xmin as the concurrency token, so omitting it causes every
                -- post-publish update to fail closed as a false DbUpdateConcurrencyException.
                SELECT *, xmin
                FROM ""OutboxMessages""
                WHERE ""Id"" = {messageId}
                FOR UPDATE")
                .FirstOrDefaultAsync();
        }

        if (context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await context.Set<OutboxMessage>().FromSqlInterpolated($@"
                SELECT *
                FROM [OutboxMessages] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [Id] = {messageId}")
                .FirstOrDefaultAsync();
        }

        return await context.Set<OutboxMessage>()
            .FirstOrDefaultAsync(m => m.Id == messageId);
    }

    private static bool IsCriticalOutboxEvent(string eventTypeName, Type? eventType) =>
        eventType != null && IntegrationEventAuthority.IsAuthoritativeEventType(eventType) ||
        IntegrationEventAuthority.IsCriticalEventTypeName(eventTypeName);

    /// <summary>
    /// Builds a LINQ expression filter for critical event types using IntegrationEventAuthority.
    /// This ensures a single source of truth for critical event classification.
    /// EF Core translates this to SQL as: WHERE EventType LIKE '%Order%' OR EventType LIKE '%Payment%' OR ...
    /// </summary>
    private static Expression<Func<OutboxMessage, bool>> BuildCriticalEventTypeFilter()
    {
        var parameter = Expression.Parameter(typeof(OutboxMessage), "m");
        var eventTypeProperty = Expression.Property(parameter, nameof(OutboxMessage.EventType));
        var toLowerMethod = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
        var containsMethod = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;
        var endsWithMethod = typeof(string).GetMethod(nameof(string.EndsWith), new[] { typeof(string) })!;
        var normalizedEventType = Expression.Call(eventTypeProperty, toLowerMethod);

        Expression? combinedExpression = null;

        foreach (var fragment in IntegrationEventAuthority.CriticalEventNameFragments)
        {
            var fragmentConstant = Expression.Constant(fragment.ToLowerInvariant());
            var containsCall = Expression.Call(normalizedEventType, containsMethod, fragmentConstant);

            combinedExpression = combinedExpression == null
                ? containsCall
                : Expression.OrElse(combinedExpression, containsCall);
        }

        foreach (var eventTypeName in IntegrationEventAuthority.NonAuthoritativeEventTypeNames)
        {
            var eventTypeConstant = Expression.Constant(eventTypeName.ToLowerInvariant());
            var excludedTypeCall = Expression.Call(normalizedEventType, endsWithMethod, eventTypeConstant);
            combinedExpression = Expression.AndAlso(combinedExpression!, Expression.Not(excludedTypeCall));
        }

        return Expression.Lambda<Func<OutboxMessage, bool>>(combinedExpression!, parameter);
    }

    /// <summary>
    /// Builds a LINQ expression filter for non-critical event types using IntegrationEventAuthority.
    /// This is the inverse of BuildCriticalEventTypeFilter.
    /// EF Core translates this to SQL as: WHERE EventType NOT LIKE '%Order%' AND EventType NOT LIKE '%Payment%' AND ...
    /// </summary>
    private static Expression<Func<OutboxMessage, bool>> BuildNonCriticalEventTypeFilter()
    {
        var parameter = Expression.Parameter(typeof(OutboxMessage), "m");
        var eventTypeProperty = Expression.Property(parameter, nameof(OutboxMessage.EventType));
        var toLowerMethod = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
        var containsMethod = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;
        var endsWithMethod = typeof(string).GetMethod(nameof(string.EndsWith), new[] { typeof(string) })!;
        var normalizedEventType = Expression.Call(eventTypeProperty, toLowerMethod);

        Expression? combinedExpression = null;

        foreach (var fragment in IntegrationEventAuthority.CriticalEventNameFragments)
        {
            var fragmentConstant = Expression.Constant(fragment.ToLowerInvariant());
            var containsCall = Expression.Call(normalizedEventType, containsMethod, fragmentConstant);
            var notContains = Expression.Not(containsCall);

            combinedExpression = combinedExpression == null
                ? notContains
                : Expression.AndAlso(combinedExpression, notContains);
        }

        Expression? excludedExpression = null;
        foreach (var eventTypeName in IntegrationEventAuthority.NonAuthoritativeEventTypeNames)
        {
            var eventTypeConstant = Expression.Constant(eventTypeName.ToLowerInvariant());
            var excludedTypeCall = Expression.Call(normalizedEventType, endsWithMethod, eventTypeConstant);
            excludedExpression = excludedExpression == null
                ? excludedTypeCall
                : Expression.OrElse(excludedExpression, excludedTypeCall);
        }

        if (excludedExpression != null)
        {
            combinedExpression = Expression.OrElse(combinedExpression!, excludedExpression);
        }

        return Expression.Lambda<Func<OutboxMessage, bool>>(combinedExpression!, parameter);
    }


    private static async Task<bool> HasUnprocessedEarlierCriticalAggregateMessageAsync(
        ApplicationDbContext context,
        OutboxMessage message)
    {
        if (!message.AggregateId.HasValue)
        {
            return false;
        }

        var messageTenantScope = AffectedTenantScopeResolver.Resolve(message.TenantId, message.EventPayload);
        if (messageTenantScope.Count == 0)
        {
            return false;
        }

        var candidateBlockers = await context.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(other =>
                other.Id != message.Id &&
                other.ProcessedAtUtc == null &&
                other.AggregateId == message.AggregateId)
            .Where(BuildCriticalEventTypeFilter())
            .Select(other => new
            {
                other.Id,
                other.TenantId,
                other.EventPayload,
                other.EventVersion,
                other.CreatedAtUtc
            })
            .ToListAsync();

        return candidateBlockers.Any(other =>
            AffectedTenantScopeResolver.Overlaps(messageTenantScope, other.TenantId, other.EventPayload) &&
            IsEarlierCriticalAggregateMessage(
                other.EventVersion,
                other.CreatedAtUtc,
                other.Id,
                message.EventVersion,
                message.CreatedAtUtc,
                message.Id));
    }

    private static bool IsEarlierCriticalAggregateMessage(
        int otherEventVersion,
        DateTime otherCreatedAtUtc,
        Guid otherId,
        int messageEventVersion,
        DateTime messageCreatedAtUtc,
        Guid messageId)
    {
        if (otherEventVersion > 0 && messageEventVersion > 0)
        {
            return otherEventVersion < messageEventVersion ||
                   (otherEventVersion == messageEventVersion && IsEarlierOutboxPosition(otherCreatedAtUtc, otherId, messageCreatedAtUtc, messageId));
        }

        return IsEarlierOutboxPosition(otherCreatedAtUtc, otherId, messageCreatedAtUtc, messageId);
    }

    private static bool IsEarlierOutboxPosition(
        DateTime otherCreatedAtUtc,
        Guid otherId,
        DateTime messageCreatedAtUtc,
        Guid messageId)
    {
        return otherCreatedAtUtc < messageCreatedAtUtc ||
               (otherCreatedAtUtc == messageCreatedAtUtc && otherId.CompareTo(messageId) < 0);
    }

    private static Guid? TryParseCorrelationId(string? correlationId)
    {
        return Guid.TryParse(correlationId, out var parsed) ? parsed : null;
    }

    private static void EnsureOutboxAuthorityScope(
        OutboxMessage message,
        Type eventType,
        IntegrationEvent @event,
        bool isCriticalOutboxEvent)
    {
        if (!isCriticalOutboxEvent)
        {
            return;
        }

        if (!@event.TenantId.HasValue || @event.TenantId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Critical outbox event {eventType.Name} cannot be published without a payload TenantId. MessageId={message.Id}");
        }

        if (!message.TenantId.HasValue || message.TenantId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Critical outbox event {eventType.Name} cannot be published without an outbox TenantId. MessageId={message.Id}");
        }

        if (message.TenantId.Value != @event.TenantId.Value)
        {
            throw new InvalidOperationException(
                $"Critical outbox event {eventType.Name} has conflicting TenantId values. MessageId={message.Id}; OutboxTenantId={message.TenantId}; PayloadTenantId={@event.TenantId}");
        }

        if (@event.OccurredAtUtc == default || @event.OccurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException(
                $"Critical outbox event {eventType.Name} cannot be published without a non-default UTC OccurredAtUtc. MessageId={message.Id}; OccurredAtUtc={@event.OccurredAtUtc:o}; Kind={@event.OccurredAtUtc.Kind}");
        }

        if (@event.EventVersion <= 0)
        {
            throw new InvalidOperationException(
                $"Critical outbox event {eventType.Name} cannot be published without a positive EventVersion. MessageId={message.Id}; EventVersion={@event.EventVersion}");
        }

        if (message.EventVersion <= 0)
        {
            message.EventVersion = @event.EventVersion;
        }
        else if (message.EventVersion != @event.EventVersion)
        {
            throw new InvalidOperationException(
                $"Critical outbox event {eventType.Name} has conflicting EventVersion values. MessageId={message.Id}; OutboxEventVersion={message.EventVersion}; PayloadEventVersion={@event.EventVersion}");
        }

        if (!message.AggregateId.HasValue || message.AggregateId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Critical outbox event {eventType.Name} cannot be published without a non-empty AggregateId ordering key. MessageId={message.Id}");
        }

        var payloadAggregateId = IntegrationEventAuthority.TryGetAggregateId(@event);
        if (!payloadAggregateId.HasValue || payloadAggregateId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Critical outbox event {eventType.Name} cannot be published without a payload aggregate ordering key. MessageId={message.Id}");
        }

        if (message.AggregateId.Value != payloadAggregateId.Value)
        {
            throw new InvalidOperationException(
                $"Critical outbox event {eventType.Name} has conflicting AggregateId values. MessageId={message.Id}; OutboxAggregateId={message.AggregateId}; PayloadAggregateId={payloadAggregateId}");
        }
    }

    /// <summary>
    /// Saves changes with optimistic concurrency handling.
    /// If a concurrency conflict occurs, it means another worker already processed the message.
    /// </summary>
    private async Task SaveChangesWithConcurrencyHandlingAsync(ApplicationDbContext context, Guid messageId)
    {
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Concurrency conflict - another worker already updated this message
            // This is expected behavior and prevents duplicate processing
            _logger.LogDebug(
                "[Outbox] Concurrency conflict prevented duplicate processing | MessageId: {MessageId}",
                messageId);
            throw; // Re-throw to let caller handle
        }
    }

    private async Task CleanupOldMessagesAsync(ApplicationDbContext context)
    {
        var threshold = DateTime.UtcNow.AddDays(-RetentionDays);

        var sql = @"
            DELETE FROM ""OutboxMessages""
            WHERE ""ProcessedAtUtc"" IS NOT NULL 
              AND ""ProcessedAtUtc"" < @p0";

        var deleted = await context.Database.ExecuteSqlRawAsync(sql, threshold);

        if (deleted > 0)
        {
            _logger.LogInformation("[Outbox] Cleaned up {Count} old processed messages", deleted);
        }
    }

    private static Type? ResolveEventType(string typeName)
    {
        // Try to find the type in loaded assemblies
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(typeName);
                if (type != null) return type;
            }
            catch
            {
                // Ignore assembly resolution errors
            }
        }

        return null;
    }
}

internal sealed record OutboxProcessResult(bool Attempted, bool Succeeded);
