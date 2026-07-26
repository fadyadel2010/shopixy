using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Reliability;

/// <summary>
/// SQL-backed projection fence for at-least-once consumers.
/// It collapses already-completed EventId deliveries and active overlapping deliveries before side effects run.
/// </summary>
public sealed class ProjectionEventFence : IAsyncDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger _logger;
    private readonly long _lockKey;
    private readonly bool _closeConnection;
    private readonly bool _useAdvisoryLock;
    private bool _released;

    private ProjectionEventFence(
        ApplicationDbContext dbContext,
        ILogger logger,
        long lockKey,
        bool closeConnection,
        bool useAdvisoryLock = true)
    {
        _dbContext = dbContext;
        _logger = logger;
        _lockKey = lockKey;
        _closeConnection = closeConnection;
        _useAdvisoryLock = useAdvisoryLock;
    }

    public static async Task<ProjectionEventFence?> TryAcquireAsync(
        ApplicationDbContext dbContext,
        IntegrationEvent @event,
        ProjectionEventFenceKey key,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        return await TryAcquireCoreAsync(
            dbContext,
            @event.EventId,
            @event.TenantId,
            key,
            logger,
            cancellationToken);
    }

    public static async Task<ProjectionEventFence?> TryAcquireAsync(
        ApplicationDbContext dbContext,
        Guid eventId,
        ProjectionEventFenceKey key,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        return await TryAcquireCoreAsync(
            dbContext,
            eventId,
            tenantId: null,
            key,
            logger,
            cancellationToken);
    }

    public static async Task<ProjectionEventFence?> TryAcquireAsync(
        ApplicationDbContext dbContext,
        Guid eventId,
        Guid? tenantId,
        ProjectionEventFenceKey key,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        return await TryAcquireCoreAsync(
            dbContext,
            eventId,
            tenantId,
            key,
            logger,
            cancellationToken);
    }

    private static async Task<ProjectionEventFence?> TryAcquireCoreAsync(
        ApplicationDbContext dbContext,
        Guid eventId,
        Guid? tenantId,
        ProjectionEventFenceKey key,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (await IsAlreadyCompletedAsync(dbContext, eventId, key, cancellationToken))
        {
            OperationalConvergenceTelemetry.RecordReplayDecision(
                OperationalConvergencePhase.Replay,
                key.ConsumerName,
                OperationalConvergenceAuthority.ProjectionEventFence,
                OperationalConvergenceDecision.Collapsed,
                OperationalConvergenceReason.DuplicateCompletedEvent,
                tenantId);
            return null;
        }

        if (!dbContext.Database.IsRelational())
        {
            return new ProjectionEventFence(
                dbContext,
                logger ?? NullLogger.Instance,
                0,
                closeConnection: false,
                useAdvisoryLock: false);
        }

        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        var lockKey = BuildLockKey(eventId, key.ConsumerName, key.EventType);
        var acquired = await dbContext.Database
            .SqlQueryRaw<bool>("SELECT pg_try_advisory_lock({0}) AS \"Value\"", lockKey)
            .SingleAsync(cancellationToken);

        if (!acquired)
        {
            if (closeConnection)
            {
                await dbContext.Database.CloseConnectionAsync();
            }

            OperationalConvergenceTelemetry.RecordReplayDecision(
                OperationalConvergencePhase.Replay,
                key.ConsumerName,
                OperationalConvergenceAuthority.ProjectionEventFence,
                OperationalConvergenceDecision.Collapsed,
                OperationalConvergenceReason.ActiveDeliveryOverlap,
                tenantId);
            return null;
        }

        if (await IsAlreadyCompletedAsync(dbContext, eventId, key, cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})", lockKey);
            if (closeConnection)
            {
                await dbContext.Database.CloseConnectionAsync();
            }

            OperationalConvergenceTelemetry.RecordReplayDecision(
                OperationalConvergencePhase.Replay,
                key.ConsumerName,
                OperationalConvergenceAuthority.ProjectionEventFence,
                OperationalConvergenceDecision.Collapsed,
                OperationalConvergenceReason.DuplicateCompletedAfterLock,
                tenantId);
            return null;
        }

        return new ProjectionEventFence(
            dbContext,
            logger ?? NullLogger.Instance,
            lockKey,
            closeConnection);
    }

    public async Task CompleteAsync(
        IntegrationEvent @event,
        ProjectionEventFenceKey key,
        CancellationToken cancellationToken)
    {
        await CompleteAsync(
            @event.EventId,
            @event.TenantId,
            @event.CorrelationId ?? @event.EventId.ToString(),
            key,
            cancellationToken);
    }

    public async Task CompleteAsync(
        Guid eventId,
        Guid? tenantId,
        string correlationId,
        ProjectionEventFenceKey key,
        CancellationToken cancellationToken)
    {
        if (await IsAlreadyCompletedAsync(_dbContext, eventId, key, cancellationToken))
        {
            return;
        }

        _dbContext.ProcessedEvents.Add(ProcessedEvent.Create(
            eventId,
            key.ConsumerName,
            key.EventType,
            key.EntityId,
            tenantId,
            correlationId));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            OperationalConvergenceTelemetry.RecordReplayDecision(
                OperationalConvergencePhase.Replay,
                key.ConsumerName,
                OperationalConvergenceAuthority.ProjectionEventFence,
                OperationalConvergenceDecision.Applied,
                OperationalConvergenceReason.FenceCompleted,
                tenantId);
        }
        catch (DbUpdateException ex) when (IsProcessedEventDuplicate(ex))
        {
            _dbContext.ChangeTracker.Clear();
            OperationalConvergenceTelemetry.RecordReplayDecision(
                OperationalConvergencePhase.Replay,
                key.ConsumerName,
                OperationalConvergenceAuthority.ProjectionEventFence,
                OperationalConvergenceDecision.Collapsed,
                OperationalConvergenceReason.DuplicateCompletedRace,
                tenantId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_released)
        {
            return;
        }

        _released = true;

        if (!_useAdvisoryLock)
        {
            return;
        }

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})", _lockKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release projection event advisory lock.");
        }

        if (_closeConnection)
        {
            await _dbContext.Database.CloseConnectionAsync();
        }
    }

    private static Task<bool> IsAlreadyCompletedAsync(
        ApplicationDbContext dbContext,
        Guid eventId,
        ProjectionEventFenceKey key,
        CancellationToken cancellationToken)
    {
        return dbContext.ProcessedEvents
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(item =>
                item.EventId == eventId &&
                item.ConsumerName == key.ConsumerName &&
                item.EventType == key.EventType,
                cancellationToken);
    }

    private static long BuildLockKey(Guid eventId, string consumerName, string eventType)
    {
        var input = $"{eventId:N}:{consumerName}:{eventType}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToInt64(hash, 0);
    }

    private static bool IsProcessedEventDuplicate(DbUpdateException exception)
    {
        return exception.InnerException?.GetType().Name == "PostgresException" &&
               exception.InnerException.Message.Contains("IX_ProcessedEvents_Idempotency", StringComparison.OrdinalIgnoreCase);
    }
}
