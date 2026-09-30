using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Linq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Services;

public sealed class AbandonedCartRecoveryTelemetryService : IAbandonedCartRecoveryTelemetryService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AbandonedCartRecoveryTelemetryService> _logger;

    public AbandonedCartRecoveryTelemetryService(
        ApplicationDbContext dbContext,
        ILogger<AbandonedCartRecoveryTelemetryService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<AbandonedCartRecoveryLogWriteResult> RecordWorkflowResultAsync(
        AbandonedCartRecoveryLogCommand command,
        CancellationToken cancellationToken = default)
    {
        var abandonedCart = await _dbContext.AbandonedCarts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                cart => cart.Id == command.AbandonedCartId && cart.TenantId == command.TenantId,
                cancellationToken);

        if (abandonedCart == null)
        {
            return AbandonedCartRecoveryLogWriteResult.MissingCart();
        }

        var idempotencyKey = BuildIdempotencyKey(command);
        var existingLog = await _dbContext.AbandonedCartRecoveryLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                log => log.TenantId == command.TenantId && log.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existingLog != null)
        {
            return AbandonedCartRecoveryLogWriteResult.Duplicate(
                existingLog.Id,
                existingLog.CartId,
                existingLog.PhoneNumber);
        }

        var logEntry = new AbandonedCartRecoveryLog
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            AbandonedCartId = abandonedCart.Id,
            CartId = abandonedCart.CartId,
            PhoneNumber = abandonedCart.PhoneNumber,
            FirstMessageSent = command.FirstMessageSent,
            FirstMessageSentAtUtc = command.FirstMessageSentAtUtc,
            FirstMessageError = NormalizeText(command.FirstMessageError),
            SecondMessageSent = command.SecondMessageSent,
            SecondMessageSentAtUtc = command.SecondMessageSentAtUtc,
            SecondMessageError = NormalizeText(command.SecondMessageError),
            WorkflowStartedAtUtc = abandonedCart.TriggeredAtUtc
                ?? abandonedCart.PendingAtUtc
                ?? abandonedCart.AbandonedAtUtc
                ?? abandonedCart.CreatedAtUtc,
            WorkflowCompletedAtUtc = command.WorkflowCompletedAtUtc,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.AbandonedCartRecoveryLogs.Add(logEntry);
        
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            // Detach the failed entity to prevent DbContext poisoning
            _dbContext.Entry(logEntry).State = EntityState.Detached;

            // Concurrent insert detected - re-query for the existing record
            _logger.LogInformation(
                "[Abandoned Cart Recovery] Duplicate idempotency key detected (concurrent insert) | TenantId: {TenantId} | IdempotencyKey: {IdempotencyKey}",
                command.TenantId,
                idempotencyKey);

            var existing = await _dbContext.AbandonedCartRecoveryLogs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    log => log.TenantId == command.TenantId && log.IdempotencyKey == idempotencyKey,
                    cancellationToken);

            if (existing != null)
            {
                return AbandonedCartRecoveryLogWriteResult.Duplicate(
                    existing.Id,
                    existing.CartId,
                    existing.PhoneNumber);
            }

            // Shouldn't happen, but if we can't find the duplicate, rethrow
            _logger.LogError(ex,
                "[Abandoned Cart Recovery] Duplicate key exception but record not found | TenantId: {TenantId} | IdempotencyKey: {IdempotencyKey}",
                command.TenantId,
                idempotencyKey);
            throw;
        }

        _logger.LogInformation(
            "[Abandoned Cart Recovery] Workflow telemetry persisted | TenantId: {TenantId} | AbandonedCartId: {AbandonedCartId} | LogId: {LogId}",
            command.TenantId,
            command.AbandonedCartId,
            logEntry.Id);

        return AbandonedCartRecoveryLogWriteResult.Created(
            logEntry.Id,
            logEntry.CartId,
            logEntry.PhoneNumber);
    }

    public async Task<AbandonedCartRecoveryStatsSnapshot> GetStatsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var totalAbandoned = await _dbContext.AbandonedCarts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(
                cart => cart.TenantId == tenantId && cart.AbandonedAtUtc != null,
                cancellationToken);

        var recovered = await _dbContext.AbandonedCarts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(
                cart => cart.TenantId == tenantId &&
                        cart.RecoveryStatus == AbandonedCartRecoveryStatus.Recovered &&
                        cart.RecoveredAtUtc != null &&
                        cart.RecoveredOrderId != null,
                cancellationToken);

        var logProjection = await _dbContext.AbandonedCartRecoveryLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(log => log.TenantId == tenantId)
            .Select(log => new
            {
                log.FirstMessageSent,
                log.SecondMessageSent
            })
            .ToListAsync(cancellationToken);

        var messagesSent = logProjection.Sum(log =>
            (log.FirstMessageSent ? 1 : 0) +
            (log.SecondMessageSent ? 1 : 0));

        var recoveryRate = totalAbandoned == 0
            ? 0m
            : Math.Round((decimal)recovered * 100m / totalAbandoned, 2, MidpointRounding.AwayFromZero);

        return new AbandonedCartRecoveryStatsSnapshot(
            totalAbandoned,
            messagesSent,
            recovered,
            recoveryRate);
    }

    private static string BuildIdempotencyKey(AbandonedCartRecoveryLogCommand command)
    {
        var providedKey = NormalizeText(command.IdempotencyKey);
        if (!string.IsNullOrWhiteSpace(providedKey))
        {
            return providedKey;
        }

        var completionPart = command.WorkflowCompletedAtUtc?.ToString("O")
            ?? command.FirstMessageSentAtUtc?.ToString("O")
            ?? command.SecondMessageSentAtUtc?.ToString("O")
            ?? "no-timestamp";

        return $"recovery:{command.TenantId:D}:{command.AbandonedCartId:D}:{completionPart}";
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    /// <summary>
    /// Detects if a DbUpdateException is caused by a duplicate key violation.
    /// Uses PostgreSQL-specific SqlState property for reliable detection.
    /// </summary>
    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pg && pg.SqlState == "23505";
    }
}
