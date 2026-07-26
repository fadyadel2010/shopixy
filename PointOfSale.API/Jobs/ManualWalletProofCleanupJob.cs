using System.Net;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Jobs;

/// <summary>
/// Background job to delete expired manual wallet proof uploads.
/// New uploads are stored in object storage. Legacy local references are cleaned up for compatibility.
/// </summary>
public class ManualWalletProofCleanupJob
{
    private const int CleanupBatchSize = 100;
    private const int OrphanCheckoutProofRetentionHours = 24;

    private readonly IServiceProvider _serviceProvider;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ManualWalletProofCleanupJob> _logger;

    public ManualWalletProofCleanupJob(
        IServiceProvider serviceProvider,
        IWebHostEnvironment environment,
        ILogger<ManualWalletProofCleanupJob> logger)
    {
        _serviceProvider = serviceProvider;
        _environment = environment;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Manual Wallet Proof Cleanup Job");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storageService = scope.ServiceProvider.GetRequiredService<IR2StorageService>();
            var r2Options = scope.ServiceProvider.GetRequiredService<IOptions<R2Options>>().Value;
            var reliabilityTelemetry = scope.ServiceProvider.GetRequiredService<CommerceReliabilityTelemetry>();
            var retentionAuthority = scope.ServiceProvider.GetRequiredService<IManualPaymentRetentionAuthority>();

            var now = DateTime.UtcNow;
            var totalProcessed = 0;
            var totalDeleted = 0;
            var totalMissing = 0;
            var totalLegacyDeleted = 0;

            while (true)
            {
                var expiredPayments = await dbContext.PaymentIntents
                    .Where(p => p.ProofImageExpiresAtUtc != null &&
                                p.ProofImageExpiresAtUtc < now &&
                                !p.ProofImageDeleted)
                    .OrderBy(p => p.ProofImageExpiresAtUtc)
                    .ThenBy(p => p.Id)
                    .Take(CleanupBatchSize)
                    .ToListAsync(cancellationToken);

                if (expiredPayments.Count == 0)
                {
                    if (totalProcessed == 0)
                    {
                        _logger.LogInformation("No expired manual wallet proofs found.");
                    }

                    break;
                }

                var batchProgressMade = false;

                foreach (var payment in expiredPayments)
                {
                    try
                    {
                        var retention = retentionAuthority.EvaluateCleanup(payment, new DateTimeOffset(now, TimeSpan.Zero));
                        if (!retention.Expire)
                        {
                            continue;
                        }
                    }
                    catch (ManualPaymentApplicationException ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Manual wallet proof cleanup rejected by retention authority | PaymentIntentId: {PaymentIntentId} | Code: {Code}",
                            payment.Id,
                            ex.Code);
                        continue;
                    }

                    // Already filtered by query, but defensive check
                    if (string.IsNullOrWhiteSpace(payment.ProofImageUrl))
                    {
                        // Mark as processed to prevent re-selection
                        payment.ProofImageDeleted = true;
                        batchProgressMade = true;
                        continue;
                    }

                    try
                    {
                        if (ManualWalletProofStorage.TryNormalizeTenantScopedObjectKey(
                                payment.ProofImageUrl,
                                payment.TenantId,
                                r2Options,
                                out var objectKey))
                        {
                            try
                            {
                                await storageService.DeleteObjectAsync(objectKey, cancellationToken);
                                totalDeleted++;
                                reliabilityTelemetry.RecordOrphanFilesDeleted(1, "r2");
                            }
                            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                            {
                                totalMissing++;
                                _logger.LogWarning(
                                    ex,
                                    "Manual wallet proof cleanup found missing cloud object | PaymentIntentId: {PaymentIntentId} | ObjectKey: {ObjectKey}",
                                    payment.Id,
                                    objectKey);
                            }

                            payment.ProofImageDeleted = true;
                            payment.ProofImageUrl = null;
                            batchProgressMade = true;
                            continue;
                        }

                        if (!TryDeleteLegacyLocalProof(payment.ProofImageUrl, out var legacyDeleted, out var legacyMissing))
                        {
                            _logger.LogWarning(
                                "Manual wallet proof cleanup skipped unsupported legacy reference | PaymentIntentId: {PaymentIntentId} | ProofImageUrl: {ProofImageUrl}",
                                payment.Id,
                                payment.ProofImageUrl);
                            // Mark as processed to prevent poison pill re-selection
                            payment.ProofImageDeleted = true;
                            payment.ProofImageUrl = null;
                            batchProgressMade = true;
                            continue;
                        }

                        if (legacyDeleted)
                        {
                            totalLegacyDeleted++;
                            reliabilityTelemetry.RecordOrphanFilesDeleted(1, "local_legacy");
                        }

                        if (legacyMissing)
                        {
                            totalMissing++;
                        }

                        payment.ProofImageDeleted = true;
                        payment.ProofImageUrl = null;
                        batchProgressMade = true;
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // Let cooperative cancellation propagate
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete proof for PaymentIntent {Id}", payment.Id);
                    }
                }

                if (!batchProgressMade)
                {
                    _logger.LogWarning(
                        "No progress made in batch of {Count} expired payments. Breaking loop to prevent infinite reprocessing.",
                        expiredPayments.Count);
                    break;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                totalProcessed += expiredPayments.Count;

                if (expiredPayments.Count < CleanupBatchSize)
                {
                    break;
                }
            }

            var checkoutSessionCutoff = now.AddHours(-OrphanCheckoutProofRetentionHours);
            while (true)
            {
                var staleCheckoutSessions = await dbContext.CheckoutSessions
                    .IgnoreQueryFilters()
                    .Where(session =>
                        !string.IsNullOrWhiteSpace(session.ProofImageUrl) &&
                        session.LastActivityAtUtc < checkoutSessionCutoff &&
                        (session.Status != CheckoutSessionStatus.ConvertedToOrder ||
                         // For converted sessions, only delete if:
                         // 1. No manual wallet payment intent exists with a proof (already copied), OR
                         // 2. The order is in a terminal state (Completed, Cancelled, Refunded) AND
                         //    all manual wallet payment intents are in terminal states (Paid, Failed, Cancelled, Expired, Rejected)
                         (!dbContext.PaymentIntents.Any(paymentIntent =>
                             paymentIntent.TenantId == session.TenantId &&
                             paymentIntent.OrderId == session.ConvertedOrderId &&
                             paymentIntent.Provider == PaymentProvider.ManualWallet &&
                             !string.IsNullOrWhiteSpace(paymentIntent.ProofImageUrl)) ||
                          (dbContext.Orders.Any(order =>
                              order.Id == session.ConvertedOrderId &&
                              order.TenantId == session.TenantId &&
                              (order.Status == OrderStatus.Completed ||
                               order.Status == OrderStatus.Cancelled ||
                               order.Status == OrderStatus.Refunded)) &&
                          !dbContext.PaymentIntents.Any(paymentIntent =>
                              paymentIntent.TenantId == session.TenantId &&
                              paymentIntent.OrderId == session.ConvertedOrderId &&
                              paymentIntent.Provider == PaymentProvider.ManualWallet &&
                              paymentIntent.Status != PaymentIntentStatus.Paid &&
                              paymentIntent.Status != PaymentIntentStatus.Failed &&
                              paymentIntent.Status != PaymentIntentStatus.Cancelled &&
                              paymentIntent.Status != PaymentIntentStatus.Expired &&
                              paymentIntent.Status != PaymentIntentStatus.Rejected)))))
                    .OrderBy(session => session.LastActivityAtUtc)
                    .ThenBy(session => session.Id)
                    .Take(CleanupBatchSize)
                    .ToListAsync(cancellationToken);

                if (staleCheckoutSessions.Count == 0)
                {
                    break;
                }

                var batchProgressMade = false;

                foreach (var session in staleCheckoutSessions)
                {
                    try
                    {
                        if (ManualWalletProofStorage.TryNormalizeTenantScopedObjectKey(
                                session.ProofImageUrl,
                                session.TenantId,
                                r2Options,
                                out var objectKey))
                        {
                            try
                            {
                                await storageService.DeleteObjectAsync(objectKey, cancellationToken);
                                totalDeleted++;
                                reliabilityTelemetry.RecordOrphanFilesDeleted(1, "checkout_session");
                            }
                            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                            {
                                totalMissing++;
                                _logger.LogWarning(
                                    ex,
                                    "Manual wallet proof cleanup found missing checkout-session object | CheckoutSessionId: {CheckoutSessionId} | ObjectKey: {ObjectKey}",
                                    session.Id,
                                    objectKey);
                            }

                            session.ProofImageUrl = null;
                            session.WalletPhoneNumber = null;
                            session.WalletType = null;
                            batchProgressMade = true;
                            continue;
                        }

                        if (!TryDeleteLegacyLocalProof(session.ProofImageUrl!, out var legacyDeleted, out var legacyMissing))
                        {
                            _logger.LogWarning(
                                "Manual wallet proof cleanup skipped unsupported checkout-session reference | CheckoutSessionId: {CheckoutSessionId} | ProofImageUrl: {ProofImageUrl}",
                                session.Id,
                                session.ProofImageUrl);
                            session.ProofImageUrl = null;
                            session.WalletPhoneNumber = null;
                            session.WalletType = null;
                            batchProgressMade = true;
                            continue;
                        }

                        if (legacyDeleted)
                        {
                            totalLegacyDeleted++;
                            reliabilityTelemetry.RecordOrphanFilesDeleted(1, "checkout_session_legacy");
                        }

                        if (legacyMissing)
                        {
                            totalMissing++;
                        }

                        session.ProofImageUrl = null;
                        session.WalletPhoneNumber = null;
                        session.WalletType = null;
                        batchProgressMade = true;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete proof for CheckoutSession {Id}", session.Id);
                    }
                }

                if (!batchProgressMade)
                {
                    _logger.LogWarning(
                        "No progress made in batch of {Count} stale checkout-session proofs. Breaking loop to prevent infinite reprocessing.",
                        staleCheckoutSessions.Count);
                    break;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                totalProcessed += staleCheckoutSessions.Count;

                if (staleCheckoutSessions.Count < CleanupBatchSize)
                {
                    break;
                }
            }

            _logger.LogInformation(
                "Manual Wallet Proof Cleanup Job Completed | ProcessedRows: {ProcessedRows} | DeletedCloudFiles: {DeletedCloudFiles} | DeletedLegacyFiles: {DeletedLegacyFiles} | MissingFiles: {MissingFiles}",
                totalProcessed,
                totalDeleted,
                totalLegacyDeleted,
                totalMissing);
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation - not a failure
            _logger.LogInformation("Manual Wallet Proof Cleanup Job canceled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical failure in Manual Wallet Proof Cleanup Job");
        }
    }

    private bool TryDeleteLegacyLocalProof(
        string proofImageUrl,
        out bool deleted,
        out bool missing)
    {
        deleted = false;
        missing = false;

        if (!ManualWalletProofStorage.LooksLikeLegacyLocalReference(proofImageUrl))
        {
            return false;
        }

        var baseDir = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "secure-uploads"));
        var filePath = Path.GetFullPath(Path.Combine(baseDir, proofImageUrl));
        var relativePath = Path.GetRelativePath(baseDir, filePath);

        if (string.IsNullOrEmpty(relativePath) ||
            relativePath.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            _logger.LogWarning(
                "Path traversal attempt detected for legacy proof reference {ProofImageUrl}",
                proofImageUrl);
            return false;
        }

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            deleted = true;
            _logger.LogInformation("Deleted legacy manual wallet proof file: {Path}", filePath);
            return true;
        }

        missing = true;
        _logger.LogWarning("Legacy manual wallet proof file not found on disk: {Path}", filePath);
        return true;
    }
}
