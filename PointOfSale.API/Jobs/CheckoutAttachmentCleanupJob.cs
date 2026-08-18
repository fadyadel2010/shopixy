using System.Net;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Checkout;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Jobs;

public sealed class CheckoutAttachmentCleanupJob
{
    private const int CleanupBatchSize = 100;

    private readonly ApplicationDbContext _dbContext;
    private readonly IR2StorageService _storageService;
    private readonly ILogger<CheckoutAttachmentCleanupJob> _logger;

    public CheckoutAttachmentCleanupJob(
        ApplicationDbContext dbContext,
        IR2StorageService storageService,
        ILogger<CheckoutAttachmentCleanupJob> logger)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var pendingLeaseCutoffUtc = nowUtc.AddMinutes(-CheckoutAttachmentService.UploadUrlExpiryMinutes);
            var expiredAttachments = await _dbContext.CheckoutAttachments
                .IgnoreQueryFilters()
                .Where(attachment =>
                    attachment.OrderId == null &&
                    !attachment.IsDeleted &&
                    (attachment.Status == CheckoutAttachmentStatus.Deleting ||
                     (attachment.Status == CheckoutAttachmentStatus.Pending &&
                      (attachment.ExpiresAtUtc <= nowUtc || attachment.CreatedAt <= pendingLeaseCutoffUtc)) ||
                     (attachment.ExpiresAtUtc <= nowUtc &&
                      (attachment.Status == CheckoutAttachmentStatus.Uploaded ||
                       attachment.Status == CheckoutAttachmentStatus.Finalized ||
                       attachment.Status == CheckoutAttachmentStatus.Failed))))
                .OrderBy(attachment => attachment.ExpiresAtUtc)
                .ThenBy(attachment => attachment.Id)
                .Take(CleanupBatchSize)
                .ToListAsync(cancellationToken);

            var claimedCount = 0;
            var expiredCount = 0;
            var failureCount = 0;
            foreach (var attachment in expiredAttachments)
            {
                if (!await TryClaimForDeletionAsync(attachment, nowUtc, cancellationToken))
                {
                    continue;
                }

                claimedCount++;
                try
                {
                    await DeleteAttachmentObjectsAsync(attachment, cancellationToken);
                }
                catch (Exception ex)
                {
                    failureCount++;
                    _logger.LogError(
                        ex,
                        "Checkout attachment cleanup failed to delete object | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | AttachmentId: {AttachmentId} | Status: {Status} | ErrorCategory: storage",
                        attachment.TenantId,
                        attachment.CartId,
                        attachment.CheckoutFieldDefinitionId,
                        attachment.Id,
                        attachment.Status);
                    continue;
                }

                await MarkExpiredAsync(attachment, nowUtc, cancellationToken);
                expiredCount++;
            }

            if (expiredAttachments.Count > 0 || failureCount > 0)
            {
                _logger.LogInformation(
                    "Checkout attachment cleanup completed | CandidateCount: {CandidateCount} | ClaimedCount: {ClaimedCount} | ExpiredCount: {ExpiredCount} | FailureCount: {FailureCount}",
                    expiredAttachments.Count,
                    claimedCount,
                    expiredCount,
                    failureCount);
            }

            if (failureCount > 0)
            {
                throw new InvalidOperationException(
                    $"Checkout attachment cleanup could not delete {failureCount} claimed object set(s).");
            }
        }
        catch (PostgresException pgEx) when (pgEx.SqlState == "42P01")
        {
            _logger.LogWarning("CheckoutAttachments table does not exist yet, skipping checkout attachment cleanup.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Checkout attachment cleanup failed.");
            throw;
        }
    }

    private async Task<bool> TryClaimForDeletionAsync(
        PointOfSale.Core.Entities.CheckoutAttachment attachment,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            attachment.Status = CheckoutAttachmentStatus.Deleting;
            attachment.UpdatedAt = nowUtc;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var pendingLeaseCutoffUtc = nowUtc.AddMinutes(-CheckoutAttachmentService.UploadUrlExpiryMinutes);
        var claimed = await _dbContext.CheckoutAttachments
            .IgnoreQueryFilters()
            .Where(item => item.Id == attachment.Id &&
                           item.OrderId == null &&
                           !item.IsDeleted &&
                           (item.Status == CheckoutAttachmentStatus.Deleting ||
                            (item.Status == CheckoutAttachmentStatus.Pending &&
                             (item.ExpiresAtUtc <= nowUtc || item.CreatedAt <= pendingLeaseCutoffUtc)) ||
                            (item.ExpiresAtUtc <= nowUtc &&
                             (item.Status == CheckoutAttachmentStatus.Uploaded ||
                              item.Status == CheckoutAttachmentStatus.Finalized ||
                              item.Status == CheckoutAttachmentStatus.Failed))))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, CheckoutAttachmentStatus.Deleting)
                .SetProperty(item => item.UpdatedAt, nowUtc), cancellationToken);

        _dbContext.Entry(attachment).State = EntityState.Detached;
        attachment.Status = CheckoutAttachmentStatus.Deleting;
        attachment.UpdatedAt = nowUtc;
        return claimed == 1;
    }

    private async Task DeleteAttachmentObjectsAsync(
        PointOfSale.Core.Entities.CheckoutAttachment attachment,
        CancellationToken cancellationToken)
    {
        await DeleteAttachmentObjectIfPresentAsync(attachment, attachment.ObjectKey, cancellationToken);

        if (!string.IsNullOrWhiteSpace(attachment.FinalObjectKey) &&
            !string.Equals(attachment.FinalObjectKey, attachment.ObjectKey, StringComparison.Ordinal))
        {
            await DeleteAttachmentObjectIfPresentAsync(attachment, attachment.FinalObjectKey, cancellationToken);
        }
    }

    private async Task DeleteAttachmentObjectIfPresentAsync(
        PointOfSale.Core.Entities.CheckoutAttachment attachment,
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await _storageService.DeleteObjectAsync(objectKey, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                ex,
                "Checkout attachment cleanup found missing object | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | AttachmentId: {AttachmentId} | Status: {Status} | ErrorCategory: storage_missing",
                attachment.TenantId,
                attachment.CartId,
                attachment.CheckoutFieldDefinitionId,
                attachment.Id,
                attachment.Status);
        }
    }

    private async Task MarkExpiredAsync(
        PointOfSale.Core.Entities.CheckoutAttachment attachment,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            attachment.Status = CheckoutAttachmentStatus.Expired;
            attachment.IsDeleted = true;
            attachment.UpdatedAt = nowUtc;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await _dbContext.CheckoutAttachments
            .IgnoreQueryFilters()
            .Where(item => item.Id == attachment.Id &&
                           item.OrderId == null &&
                           item.Status == CheckoutAttachmentStatus.Deleting &&
                           !item.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, CheckoutAttachmentStatus.Expired)
                .SetProperty(item => item.IsDeleted, true)
                .SetProperty(item => item.UpdatedAt, nowUtc), cancellationToken);
    }
}
