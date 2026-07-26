using System.Net;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Jobs;

/// <summary>
/// Deletes abandoned temporary refund evidence uploads after their staging lease expires.
/// Runs on a recurring schedule so unused R2 objects do not accumulate indefinitely.
/// </summary>
public class RefundEvidenceCleanupJob
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IR2StorageService _storageService;
    private readonly RefundSettings _settings;
    private readonly ILogger<RefundEvidenceCleanupJob> _logger;

    public RefundEvidenceCleanupJob(
        ApplicationDbContext dbContext,
        IR2StorageService storageService,
        IOptions<RefundSettings> settings,
        ILogger<RefundEvidenceCleanupJob> logger)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var batchSize = Math.Max(_settings.MaxEvidenceFiles, _settings.EvidenceCleanupBatchSize);

            var expiredTemporaryMedia = await _dbContext.CommerceHubRefundEvidenceMedia
                .Where(media =>
                    media.IsTemporary &&
                    !media.IsDeleted &&
                    !media.RefundRequestId.HasValue &&
                    media.ExpiresAtUtc <= nowUtc)
                .OrderBy(media => media.ExpiresAtUtc)
                .ThenBy(media => media.CreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (expiredTemporaryMedia.Count == 0)
            {
                _logger.LogDebug("Refund evidence cleanup found no expired temporary uploads.");
                return;
            }

            var deletedCount = 0;
            var missingCount = 0;

            foreach (var media in expiredTemporaryMedia)
            {
                try
                {
                    await _storageService.DeleteObjectAsync(media.ObjectKey, cancellationToken);
                    deletedCount++;
                }
                catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    missingCount++;
                    _logger.LogWarning(
                        ex,
                        "Refund evidence cleanup found missing object | MediaId: {MediaId} | ObjectKey: {ObjectKey}",
                        media.Id,
                        media.ObjectKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Refund evidence cleanup failed to delete object | MediaId: {MediaId} | ObjectKey: {ObjectKey}",
                        media.Id,
                        media.ObjectKey);
                    continue;
                }

                media.IsDeleted = true;
                media.UpdatedAt = nowUtc;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Refund evidence cleanup completed | DeletedObjects: {DeletedCount} | MissingObjects: {MissingCount} | ProcessedRows: {ProcessedRows}",
                deletedCount,
                missingCount,
                expiredTemporaryMedia.Count);
        }
        catch (PostgresException pgEx) when (pgEx.SqlState == "42P01")
        {
            _logger.LogWarning("CommerceHubRefundEvidenceMedia table does not exist yet, skipping refund evidence cleanup.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refund evidence cleanup failed.");
        }
    }
}
