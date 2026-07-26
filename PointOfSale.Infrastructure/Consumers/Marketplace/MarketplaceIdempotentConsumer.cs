using System.Diagnostics;
using System.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Eventing;

namespace PointOfSale.Infrastructure.Consumers.Marketplace;

public abstract class MarketplaceIdempotentConsumer<TEvent> : IConsumer<TEvent>
    where TEvent : class, IMarketplaceDomainEvent
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger _logger;
    private readonly IEventLagTracker _lagTracker;
    private readonly MarketplaceProjectionOutboxProcessor _projectionOutboxProcessor;

    protected MarketplaceIdempotentConsumer(
        ApplicationDbContext dbContext,
        ILogger logger,
        IEventLagTracker lagTracker,
        MarketplaceProjectionOutboxProcessor projectionOutboxProcessor)
    {
        _dbContext = dbContext;
        _logger = logger;
        _lagTracker = lagTracker;
        _projectionOutboxProcessor = projectionOutboxProcessor;
    }

    protected abstract string ConsumerName { get; }

    protected virtual Task HandleAsync(ConsumeContext<TEvent> context, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task Consume(ConsumeContext<TEvent> context)
    {
        var message = context.Message;
        var stopwatch = Stopwatch.StartNew();
        var eventType = typeof(TEvent).FullName ?? typeof(TEvent).Name;
        var legacyEventType = typeof(TEvent).Name;

        try
        {
            var alreadyQueued = await _dbContext.Set<MarketplaceProjectionOutboxMessage>()
                .IgnoreQueryFilters()
                .AnyAsync(
                    queued => queued.EventId == message.EventId && queued.ConsumerName == ConsumerName && (queued.EventType == eventType || queued.EventType == legacyEventType),
                    context.CancellationToken);

            var alreadyCompleted = await _dbContext.Set<ProcessedEvent>()
                .IgnoreQueryFilters()
                .AnyAsync(
                    processed => processed.EventId == message.EventId &&
                                 processed.ConsumerName == ConsumerName &&
                                 (processed.EventType == eventType || processed.EventType == legacyEventType),
                    context.CancellationToken);

            if (alreadyQueued || alreadyCompleted)
            {
                _logger.LogInformation(
                    "Marketplace projection outbox duplicate ignored | Consumer: {ConsumerName} | EventType: {EventType} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                    ConsumerName,
                    eventType,
                    message.EventId,
                    message.CorrelationId);

                return;
            }

            MarketplaceProjectionOutboxMessage? queuedMessage = null;

            if (_dbContext.Database.CurrentTransaction == null)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    context.CancellationToken);

                try
                {
                    queuedMessage = AddProjectionOutboxMessage(message);
                    await _dbContext.SaveChangesAsync(context.CancellationToken);
                    await transaction.CommitAsync(context.CancellationToken);
                }
                catch (DbUpdateException ex) when (IsProcessedEventUniqueViolation(ex))
                {
                    // PRODUCTION HARDENING: Detach only the failed projection outbox claim.
                    // Clearing the whole tracker here can drop unrelated state when this
                    // consumer is invoked inside an ambient transaction.
                    DetachPendingProjectionOutboxMessage(message.EventId, eventType, legacyEventType);
                    
                    _logger.LogInformation(
                        "Marketplace projection outbox duplicate collapsed by unique constraint during claim | Consumer: {ConsumerName} | EventType: {EventType} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                        ConsumerName,
                        eventType,
                        message.EventId,
                        message.CorrelationId);
                    return;
                }
            }
            else
            {
                try
                {
                    queuedMessage = AddProjectionOutboxMessage(message);
                    await _dbContext.SaveChangesAsync(context.CancellationToken);
                }
                catch (DbUpdateException ex) when (IsProcessedEventUniqueViolation(ex))
                {
                    // PRODUCTION HARDENING: Detach only the failed projection outbox claim.
                    // Clearing the whole tracker here can drop unrelated state when this
                    // consumer is invoked inside an ambient transaction.
                    DetachPendingProjectionOutboxMessage(message.EventId, eventType, legacyEventType);
                    
                    _logger.LogInformation(
                        "Marketplace projection outbox duplicate collapsed by unique constraint during claim | Consumer: {ConsumerName} | EventType: {EventType} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                        ConsumerName,
                        eventType,
                        message.EventId,
                        message.CorrelationId);
                    return;
                }
            }

            if (_dbContext.Database.CurrentTransaction != null)
            {
                return;
            }

            if (queuedMessage == null)
            {
                return;
            }

            var projectionResult = await _projectionOutboxProcessor.ProcessMessageAsync(
                queuedMessage.Id,
                context.CancellationToken);

            if (projectionResult == MarketplaceProjectionOutboxProcessResult.Failed)
            {
                stopwatch.Stop();
                _lagTracker.TrackEventFailed(
                    eventType,
                    message.OccurredAtUtc,
                    "Marketplace projection outbox immediate dispatch failed.",
                    message.CorrelationId);

                _logger.LogWarning(
                    "Marketplace projection outbox immediate dispatch failed but the durable queue row remains for retry | Consumer: {ConsumerName} | EventType: {EventType} | EventId: {EventId}",
                    ConsumerName,
                    eventType,
                    message.EventId);

                return;
            }

            stopwatch.Stop();
            _lagTracker.TrackEventProcessed(eventType, message.OccurredAtUtc, stopwatch.Elapsed, message.CorrelationId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _lagTracker.TrackEventFailed(eventType, message.OccurredAtUtc, ex.Message, message.CorrelationId);

            _logger.LogError(
                ex,
                "Marketplace event consumer failed | Consumer: {ConsumerName} | EventType: {EventType} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                ConsumerName,
                eventType,
                message.EventId,
                message.CorrelationId);

            throw;
        }
    }

    private static bool IsProcessedEventUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    private MarketplaceProjectionOutboxMessage AddProjectionOutboxMessage(TEvent message)
    {
        var queuedMessage = MarketplaceProjectionOutboxMessage.Create(message, ConsumerName);
        _dbContext.Set<MarketplaceProjectionOutboxMessage>().Add(queuedMessage);
        return queuedMessage;
    }

    private void DetachPendingProjectionOutboxMessage(Guid eventId, string eventType, string legacyEventType)
    {
        var entries = _dbContext.ChangeTracker
            .Entries<MarketplaceProjectionOutboxMessage>()
            .Where(entry =>
                entry.State == EntityState.Added &&
                entry.Entity.EventId == eventId &&
                entry.Entity.ConsumerName == ConsumerName &&
                (entry.Entity.EventType == eventType || entry.Entity.EventType == legacyEventType))
            .ToList();

        foreach (var entry in entries)
        {
            entry.State = EntityState.Detached;
        }
    }
}
