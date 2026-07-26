using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Jobs;

/// <summary>
/// Deletes completed order idempotency records after the replay window expires.
/// Active claims stay intact because only rows with CompletedAtUtc set are eligible.
/// </summary>
public sealed class OrderRequestIdempotencyCleanupJob
{
    private const int RetentionDays = 30;
    private const int BatchSize = 500;

    private readonly ApplicationDbContext _context;
    private readonly ILogger<OrderRequestIdempotencyCleanupJob> _logger;

    public OrderRequestIdempotencyCleanupJob(
        ApplicationDbContext context,
        ILogger<OrderRequestIdempotencyCleanupJob> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            var totalDeleted = 0;
            var batches = 0;

            _logger.LogInformation(
                "OrderRequestIdempotency cleanup starting. Deleting completed records older than {Cutoff}",
                cutoff);

            while (!cancellationToken.IsCancellationRequested)
            {
                var expiredIds = await _context.OrderRequestIdempotencyRecords
                    .IgnoreQueryFilters()
                    .Where(record =>
                        !record.IsDeleted &&
                        record.CompletedAtUtc != null &&
                        record.CompletedAtUtc < cutoff)
                    .OrderBy(record => record.CompletedAtUtc)
                    .ThenBy(record => record.Id)
                    .Select(record => record.Id)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken);

                if (expiredIds.Count == 0)
                {
                    break;
                }

                var deletedCount = await _context.OrderRequestIdempotencyRecords
                    .IgnoreQueryFilters()
                    .Where(record => expiredIds.Contains(record.Id))
                    .ExecuteDeleteAsync(cancellationToken);

                totalDeleted += deletedCount;
                batches++;

                if (expiredIds.Count < BatchSize)
                {
                    break;
                }
            }

            _logger.LogInformation(
                "OrderRequestIdempotency cleanup completed. Deleted {DeletedCount} records across {BatchCount} batches",
                totalDeleted,
                batches);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PostgresException pgEx) when (pgEx.SqlState == "42P01")
        {
            _logger.LogWarning("OrderRequestIdempotency table does not exist yet, skipping cleanup");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrderRequestIdempotency cleanup failed");
            // Exception is intentionally swallowed - failures will only appear in application logs.
            // Hangfire will NOT mark this job as failed and will NOT retry.
            // This is a non-critical maintenance job, so we prefer silent failure over retry storms.
        }
    }
}
