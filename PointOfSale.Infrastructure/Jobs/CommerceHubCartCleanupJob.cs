using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Jobs;

/// <summary>
/// Deletes stale persisted CommerceHub carts so the table stays bounded.
/// Schedule: Every 6 hours via Hangfire recurring job.
/// </summary>
public class CommerceHubCartCleanupJob
{
    private const int RetentionDays = 30;

    private readonly ApplicationDbContext _context;
    private readonly ILogger<CommerceHubCartCleanupJob> _logger;

    public CommerceHubCartCleanupJob(
        ApplicationDbContext context,
        ILogger<CommerceHubCartCleanupJob> logger)
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
                "CommerceHub cart cleanup starting. Deleting carts older than {Cutoff}",
                cutoff);

            var deletedCount = await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM "CommerceHubCarts"
                WHERE COALESCE("UpdatedAt", "CreatedAt") < {cutoff}
                """,
                cancellationToken);

            _logger.LogInformation(
                "CommerceHub cart cleanup completed. Deleted {DeletedCount} carts",
                deletedCount);
        }
        catch (PostgresException pgEx) when (pgEx.SqlState == "42P01")
        {
            _logger.LogWarning("CommerceHubCarts table does not exist yet, skipping cleanup");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CommerceHub cart cleanup failed");
        }
    }
}
