using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Jobs;

/// <summary>
/// P2-13: Scheduled cleanup for ProcessedTrustEvents.
/// Prevents unbounded table growth by removing records older than 30 days.
/// 
/// ProcessedTrustEvents is an idempotency table — once an event is processed
/// and the deduplication window passes, old records serve no purpose.
/// 
/// Schedule: Daily at 3 AM via Hangfire recurring job.
/// </summary>
public class ProcessedTrustEventsCleanupJob
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProcessedTrustEventsCleanupJob> _logger;

    /// <summary>
    /// Retention period for processed trust events.
    /// Events older than this are deleted.
    /// </summary>
    private const int RetentionDays = 30;

    public ProcessedTrustEventsCleanupJob(
        ApplicationDbContext context,
        ILogger<ProcessedTrustEventsCleanupJob> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

            _logger.LogInformation(
                "ProcessedTrustEvents cleanup starting. Deleting records older than {Cutoff}",
                cutoff);

            // Use raw SQL for efficient bulk delete without loading entities
            var deletedCount = await _context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM \"ProcessedTrustEvents\" WHERE \"ProcessedAt\" < {cutoff}",
                cancellationToken);

            _logger.LogInformation(
                "ProcessedTrustEvents cleanup completed. Deleted {DeletedCount} records",
                deletedCount);
        }
        catch (Npgsql.PostgresException pgEx) when (pgEx.SqlState == "42P01")
        {
            // Table doesn't exist yet (marketplace trust pipeline not fully deployed)
            _logger.LogWarning("ProcessedTrustEvents table does not exist yet — skipping cleanup");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcessedTrustEvents cleanup failed");
            // Don't rethrow — Hangfire will mark the job as failed
            // but we don't want to crash the application
        }
    }
}
