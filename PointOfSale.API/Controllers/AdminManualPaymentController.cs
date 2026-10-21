using System.Diagnostics;
using System.Security.Claims; // For Claims
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.DTOs.Payments; 
using PointOfSale.API.DTOs;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.API.DTOs.Storefront;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Infrastructure.Data;
using Microsoft.Extensions.Options;
using Amazon.S3;
using System.Net;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin Controller for Manual Wallet Payments.
/// </summary>
[ApiController]
[Route("api/admin/payments/manual-wallet")]
[Authorize(Roles = "Owner,Admin,Manager")] // Tenant admin access only; platform operators use dedicated super-admin surfaces.
public class AdminManualPaymentController : ControllerBase
{
    private readonly IManualPaymentReviewApplicationService _manualPaymentReviewApplicationService;
    private readonly IManualPaymentCapabilityAdmissionAuthority _manualPaymentCapabilityAdmissionAuthority;
    private readonly IManualPaymentExpirationAuthority _manualPaymentExpirationAuthority;
    private readonly IManualPaymentRetentionAuthority _manualPaymentRetentionAuthority;
    private readonly ITenantContext _tenantContext;
    private readonly ApplicationDbContext _dbContext;
    private readonly IR2StorageService _storageService;
    private readonly R2Options _r2Options;
    private readonly ILogger<AdminManualPaymentController> _logger;

    public AdminManualPaymentController(
        IManualPaymentReviewApplicationService manualPaymentReviewApplicationService,
        IManualPaymentCapabilityAdmissionAuthority manualPaymentCapabilityAdmissionAuthority,
        IManualPaymentExpirationAuthority manualPaymentExpirationAuthority,
        IManualPaymentRetentionAuthority manualPaymentRetentionAuthority,
        ITenantContext tenantContext,
        ApplicationDbContext dbContext,
        IR2StorageService storageService,
        IOptions<R2Options> r2Options,
        ILogger<AdminManualPaymentController> logger)
    {
        _manualPaymentReviewApplicationService = manualPaymentReviewApplicationService;
        _manualPaymentCapabilityAdmissionAuthority = manualPaymentCapabilityAdmissionAuthority;
        _manualPaymentExpirationAuthority = manualPaymentExpirationAuthority;
        _manualPaymentRetentionAuthority = manualPaymentRetentionAuthority;
        _tenantContext = tenantContext;
        _dbContext = dbContext;
        _storageService = storageService;
        _r2Options = r2Options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Gets paged pending manual wallet payments.
    /// GET /api/admin/payments/manual-wallet/pending
    /// </summary>
    [HttpGet("pending")]
    [ProducesResponseType(typeof(PendingManualPaymentPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPendingPayments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var tenantId = _tenantContext.RequiredTenantId;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        _logger.LogInformation("GetPendingManualPayments | TenantId: {TenantId} | TraceId: {TraceId}", tenantId, traceId);

        try 
        {
            await _manualPaymentCapabilityAdmissionAuthority.AdmitAsync(
                tenantId,
                ManualPaymentOperationalCapability.PendingReviewRead,
                requestedChannel: null,
                cancellationToken);

            var observedAtUtc = DateTimeOffset.UtcNow;
            await _manualPaymentExpirationAuthority.ExpireElapsedPendingPaymentsAsync(
                tenantId,
                observedAtUtc,
                cancellationToken);

            var query = _dbContext.PaymentIntents
                .AsNoTracking()
                .Include(pi => pi.Order)
                    .ThenInclude(order => order.Customer)
                .Where(pi =>
                    pi.TenantId == tenantId &&
                    pi.Provider == PaymentProvider.ManualWallet &&
                    pi.ExpiresAtUtc.HasValue &&
                    pi.ExpiresAtUtc.Value > observedAtUtc.UtcDateTime &&
                    (
                        pi.Status == PaymentIntentStatus.AwaitingVerification ||
                        pi.Status == PaymentIntentStatus.Pending ||
                        // Paid recovery is only for an incomplete approval, never a later treasury/refund state.
                        (pi.Status == PaymentIntentStatus.Paid &&
                            (pi.Order.PaymentStatus == PaymentStatus.Pending || pi.Order.PaymentStatus == PaymentStatus.AwaitingVerification))
                    ));

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(pi => pi.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(pi => new PendingManualPaymentDto
                {
                    PaymentIntentId = pi.Id,
                    OrderId = pi.OrderId,
                    OrderNumber = pi.Order.OrderNumber,
                    CustomerName = pi.Order.Customer != null ? pi.Order.Customer.Name : pi.Order.GuestName ?? "Guest",
                    Amount = pi.Amount,
                    Currency = pi.Currency,
                    WalletType = pi.WalletType ?? "Unknown",
                    WalletPhoneNumber = pi.WalletPhoneNumber ?? "N/A",
                    HasProofFile = !string.IsNullOrWhiteSpace(pi.ProofImageUrl),
                    CreatedAtUtc = pi.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return Ok(new PendingManualPaymentPagedResponse
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                HasMore = page * pageSize < totalCount
            });
        }
        catch (ManualPaymentApplicationException ex)
        {
            _logger.LogWarning(
                ex,
                "GetPendingManualPayments rejected by Manual Payment capability authority | TenantId: {TenantId} | Code: {Code} | TraceId: {TraceId}",
                tenantId,
                ex.Code,
                traceId);
            return ManualPaymentAdmissionRejected(ex, traceId);
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "GetPendingManualPayments failed | TenantId: {TenantId} | TraceId: {TraceId}", tenantId, traceId);
             return StatusCode(500, new ApiErrorResponse("INTERNAL_ERROR", "Failed to retrieve pending payments", traceId));
        }
    }

    /// <summary>
    /// Confirms a manual payment via IPaymentLifecycleService (ledger-first).
    /// POST /api/admin/payments/manual-wallet/{paymentIntentId}/confirm
    /// 
    /// ARCHITECTURE: Routes through PaymentLifecycleService which:
    /// 1. Validates payment state (AwaitingVerification)
    /// 2. Transitions PaymentStatus → Collected (authorized context)
    /// 3. Enqueues outbox message for ledger journal (DEBIT 1140 Wallet Clearing, CREDIT 4020 Revenue)
    /// 4. Publishes ReviewedPaymentApprovedEvent
    /// 5. Commits atomically
    /// </summary>
    [HttpPost("{paymentIntentId:guid}/confirm")]
    [Authorize(Roles = "Owner,Admin")]
    [EnableRateLimiting("FinancialWrite")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmPayment(
        [FromRoute] Guid paymentIntentId,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = _tenantContext.RequiredTenantId;
        var adminId = GetCurrentAdminId();

        _logger.LogInformation(
            "ConfirmManualPayment [LEDGER-FIRST] | TenantId: {TenantId} | PaymentIntentId: {PaymentIntentId} | AdminId: {AdminId} | TraceId: {TraceId}",
            tenantId, paymentIntentId, adminId, traceId);

        try
        {
            // Step 1: Resolve PaymentIntent → OrderId
            var paymentIntent = await _dbContext.PaymentIntents
                .FirstOrDefaultAsync(pi => pi.Id == paymentIntentId && pi.TenantId == tenantId, cancellationToken);

            if (paymentIntent == null)
            {
                return NotFound(new ApiErrorResponse("PAYMENT_NOT_FOUND", $"Payment intent {paymentIntentId} not found", traceId));
            }

            if (paymentIntent.Provider != PaymentProvider.ManualWallet)
            {
                return BadRequest(new ApiErrorResponse("INVALID_PROVIDER", $"Payment intent is not a manual wallet payment", traceId));
            }
            var result = await _manualPaymentReviewApplicationService.ApproveAsync(
                new ManualPaymentReviewApplicationRequest(
                    tenantId,
                    paymentIntent.Id,
                    adminId,
                    traceId,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            if (!result.LifecycleResult.IsSuccess)
            {
                _logger.LogWarning(
                    "ApproveWalletPayment failed | PaymentIntentId: {PaymentIntentId} | Error: {Error} | TraceId: {TraceId}",
                    paymentIntentId, result.LifecycleResult.ErrorMessage, traceId);
                return Conflict(new ApiErrorResponse(
                    result.LifecycleResult.FailureCode ?? "LIFECYCLE_ERROR",
                    result.LifecycleResult.ErrorMessage ?? "Wallet approval failed",
                    traceId));
            }

            if (!result.LifecycleResult.StateChanged)
            {
                return Ok(new { Message = "Payment already confirmed", OutboxStatus = result.LifecycleResult.OutboxStatus, Idempotent = true });
            }

            _logger.LogInformation(
                "ManualPayment confirmed [LEDGER-FIRST] | PaymentIntentId: {PaymentIntentId} | OrderId: {OrderId} | OutboxStatus: {OutboxStatus} | TraceId: {TraceId}",
                paymentIntentId, paymentIntent.OrderId, result.LifecycleResult.OutboxStatus, traceId);

            return Ok(new { Message = "Payment confirmed successfully", OutboxStatus = result.LifecycleResult.OutboxStatus });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(
                ex,
                "ConfirmManualPayment concurrency conflict | PaymentIntentId: {Id} | TraceId: {TraceId}",
                paymentIntentId,
                traceId);
            return Conflict(new ApiErrorResponse(
                "PAYMENT_CONCURRENCY_CONFLICT",
                "This payment was updated by another administrator. Refresh and try again.",
                traceId));
        }
        catch (ManualPaymentApplicationException ex)
        {
            _logger.LogWarning(
                ex,
                "ConfirmManualPayment rejected by Manual Payment review application | PaymentIntentId: {Id} | Code: {Code} | TraceId: {TraceId}",
                paymentIntentId,
                ex.Code,
                traceId);
            return Conflict(new ApiErrorResponse(ex.Code, ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConfirmManualPayment failed | PaymentIntentId: {Id} | TraceId: {TraceId}", paymentIntentId, traceId);
            return StatusCode(500, new ApiErrorResponse("INTERNAL_ERROR", "Failed to confirm payment", traceId));
        }
    }

    /// <summary>
    /// Rejects a manual payment via IPaymentLifecycleService (ledger-first).
    /// POST /api/admin/payments/manual-wallet/{paymentIntentId}/reject
    /// 
    /// ARCHITECTURE: Routes through PaymentLifecycleService which:
    /// 1. Validates payment state (AwaitingVerification)
    /// 2. Transitions PaymentStatus → Rejected (authorized context)
    /// 3. NO journal (money never recognized)
    /// 4. Publishes ReviewedPaymentRejectedEvent
    /// </summary>
    [HttpPost("{paymentIntentId:guid}/reject")]
    [Authorize(Roles = "Owner,Admin")]
    [EnableRateLimiting("FinancialWrite")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RejectPayment(
        [FromRoute] Guid paymentIntentId,
        [FromBody] RejectManualPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = _tenantContext.RequiredTenantId;
        var adminId = GetCurrentAdminId();

        if (request == null || string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new ApiErrorResponse("REASON_REQUIRED", "Rejection reason is required", traceId));
        }

        _logger.LogInformation(
            "RejectManualPayment [LEDGER-FIRST] | TenantId: {TenantId} | PaymentIntentId: {PaymentIntentId} | AdminId: {AdminId} | Reason: {Reason} | TraceId: {TraceId}",
            tenantId, paymentIntentId, adminId, request.Reason, traceId);

        try
        {
            // Step 1: Resolve PaymentIntent → OrderId
            var paymentIntent = await _dbContext.PaymentIntents
                .FirstOrDefaultAsync(pi => pi.Id == paymentIntentId && pi.TenantId == tenantId, cancellationToken);

            if (paymentIntent == null)
            {
                return NotFound(new ApiErrorResponse("PAYMENT_NOT_FOUND", $"Payment intent {paymentIntentId} not found", traceId));
            }

            if (paymentIntent.Provider != PaymentProvider.ManualWallet)
            {
                return BadRequest(new ApiErrorResponse("INVALID_PROVIDER", $"Payment intent is not a manual wallet payment", traceId));
            }
            var result = await _manualPaymentReviewApplicationService.RejectAsync(
                new ManualPaymentReviewApplicationRequest(
                    tenantId,
                    paymentIntent.Id,
                    adminId,
                    traceId,
                    DateTimeOffset.UtcNow,
                    request.Reason),
                cancellationToken);

            if (!result.LifecycleResult.IsSuccess)
            {
                _logger.LogWarning(
                    "RejectWalletPayment failed | PaymentIntentId: {PaymentIntentId} | Error: {Error} | TraceId: {TraceId}",
                    paymentIntentId, result.LifecycleResult.ErrorMessage, traceId);
                return Conflict(new ApiErrorResponse("LIFECYCLE_ERROR", result.LifecycleResult.ErrorMessage ?? "Wallet rejection failed", traceId));
            }

            if (!result.LifecycleResult.StateChanged)
            {
                return Ok(new { Message = "Payment already rejected", Idempotent = true });
            }

            _logger.LogInformation(
                "ManualPayment rejected [LEDGER-FIRST] | PaymentIntentId: {PaymentIntentId} | OrderId: {OrderId} | TraceId: {TraceId}",
                paymentIntentId, paymentIntent.OrderId, traceId);

            return Ok(new { Message = "Payment rejected successfully" });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(
                ex,
                "RejectManualPayment concurrency conflict | PaymentIntentId: {Id} | TraceId: {TraceId}",
                paymentIntentId,
                traceId);
            return Conflict(new ApiErrorResponse(
                "PAYMENT_CONCURRENCY_CONFLICT",
                "This payment was updated by another administrator. Refresh and try again.",
                traceId));
        }
        catch (ManualPaymentApplicationException ex)
        {
            _logger.LogWarning(
                ex,
                "RejectManualPayment rejected by Manual Payment review application | PaymentIntentId: {Id} | Code: {Code} | TraceId: {TraceId}",
                paymentIntentId,
                ex.Code,
                traceId);
            return Conflict(new ApiErrorResponse(ex.Code, ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RejectManualPayment failed | PaymentIntentId: {Id} | TraceId: {TraceId}", paymentIntentId, traceId);
            return StatusCode(500, new ApiErrorResponse("INTERNAL_ERROR", "Failed to reject payment", traceId));
        }
    }

    /// <summary>
    /// Serves the proof image for a manual payment securely.
    /// GET /api/admin/payments/manual-wallet/proof-image/{paymentIntentId}
    /// </summary>
    [HttpGet("proof-image/{paymentIntentId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProofImage(
        [FromRoute] Guid paymentIntentId,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = _tenantContext.RequiredTenantId;
        var adminId = GetCurrentAdminId();

        _logger.LogInformation(
            "GetProofImage | TenantId: {TenantId} | PaymentIntentId: {PaymentIntentId} | AdminId: {AdminId} | TraceId: {TraceId}",
            tenantId, paymentIntentId, adminId, traceId);

        try
        {
            await _manualPaymentCapabilityAdmissionAuthority.AdmitAsync(
                tenantId,
                ManualPaymentOperationalCapability.ProofAccess,
                requestedChannel: null,
                cancellationToken);
        }
        catch (ManualPaymentApplicationException ex)
        {
            _logger.LogWarning(
                ex,
                "GetProofImage rejected by Manual Payment capability authority | TenantId: {TenantId} | PaymentIntentId: {PaymentIntentId} | Code: {Code} | TraceId: {TraceId}",
                tenantId,
                paymentIntentId,
                ex.Code,
                traceId);
            return ManualPaymentAdmissionRejected(ex, traceId);
        }

        // 1. Get PaymentIntent and its order so proof access is bound to the
        // same tenant-owned manual payment/order authority before storage I/O.
        var paymentIntent = await _dbContext.PaymentIntents
            .AsNoTracking()
            .Include(pi => pi.Order)
            .FirstOrDefaultAsync(
                pi => pi.Id == paymentIntentId &&
                      pi.TenantId == tenantId &&
                      pi.Provider == PaymentProvider.ManualWallet,
                cancellationToken);

        if (paymentIntent == null || string.IsNullOrWhiteSpace(paymentIntent.ProofImageUrl))
        {
            return NotFound();
        }

        if (paymentIntent.Order == null ||
            paymentIntent.OrderId == Guid.Empty ||
            paymentIntent.Order.Id != paymentIntent.OrderId ||
            paymentIntent.Order.TenantId != tenantId ||
            paymentIntent.Order.IsDeleted)
        {
            _logger.LogWarning(
                "GetProofImage rejected due to payment/order authority mismatch | TenantId: {TenantId} | PaymentIntentId: {PaymentIntentId} | OrderId: {OrderId} | TraceId: {TraceId}",
                tenantId,
                paymentIntentId,
                paymentIntent.OrderId,
                traceId);
            return NotFound();
        }

        try
        {
            _manualPaymentRetentionAuthority.EvaluateProofAccess(paymentIntent, DateTimeOffset.UtcNow);
        }
        catch (ManualPaymentApplicationException ex)
        {
            _logger.LogWarning(
                ex,
                "GetProofImage rejected by Manual Payment retention authority | TenantId: {TenantId} | PaymentIntentId: {PaymentIntentId} | Code: {Code} | TraceId: {TraceId}",
                tenantId,
                paymentIntentId,
                ex.Code,
                traceId);
            return ManualPaymentAdmissionRejected(ex, traceId);
        }

        // Try normalized tenant-scoped cloud storage first
        if (ManualWalletProofStorage.TryNormalizeTenantScopedObjectKey(
                paymentIntent.ProofImageUrl,
                tenantId,
                _r2Options,
                out var objectKey))
        {
            try
            {
                var download = await _storageService.DownloadObjectAsync(
                    objectKey,
                    ifMatchETag: null,
                    cancellationToken);

                return File(download.Content, download.ContentType);
            }
            catch (Exception ex) when (
                ex is FileNotFoundException ||
                ex is AmazonS3Exception { StatusCode: HttpStatusCode.NotFound })
            {
                _logger.LogWarning(
                    ex,
                    "Manual wallet proof image not found in storage | TenantId: {TenantId} | PaymentIntentId: {PaymentIntentId} | TraceId: {TraceId}",
                    tenantId,
                    paymentIntentId,
                    traceId);
                return NotFound();
            }
        }

        // Fallback: Try legacy local file reference
        if (ManualWalletProofStorage.LooksLikeLegacyLocalReference(paymentIntent.ProofImageUrl))
        {
            _logger.LogInformation(
                "Attempting legacy local file retrieval | PaymentIntentId: {PaymentIntentId} | ProofImageUrl: {ProofImageUrl}",
                paymentIntentId,
                paymentIntent.ProofImageUrl);

            // Construct tenant-scoped object key from legacy path
            // Legacy format: "manual-payments/..." -> "tenants/{tenantId}/manual-wallet-proofs/..."
            var legacyPath = paymentIntent.ProofImageUrl.Trim().TrimStart('/');
            if (legacyPath.StartsWith("manual-payments/", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = legacyPath.Substring("manual-payments/".Length);
                
                // Sanitize filename to prevent path traversal attacks
                // Mirror the traversal checks used in ManualWalletProofCleanupJob
                var sanitizedFileName = Path.GetFileName(fileName);
                if (string.IsNullOrWhiteSpace(sanitizedFileName) ||
                    sanitizedFileName.Contains("..") ||
                    sanitizedFileName.Contains("/") ||
                    sanitizedFileName.Contains("\\"))
                {
                    _logger.LogWarning(
                        "Path traversal attempt detected in legacy proof reference | PaymentIntentId: {PaymentIntentId} | FileName: {FileName}",
                        paymentIntentId,
                        fileName);
                }
                else
                {
                    var legacyObjectKey = $"tenants/{tenantId:D}/manual-wallet-proofs/{sanitizedFileName}";

                    try
                    {
                        var download = await _storageService.DownloadObjectAsync(
                            legacyObjectKey,
                            ifMatchETag: null,
                            cancellationToken);

                        _logger.LogInformation(
                            "Legacy local file reference resolved | PaymentIntentId: {PaymentIntentId} | ObjectKey: {ObjectKey}",
                            paymentIntentId,
                            legacyObjectKey);

                        return File(download.Content, download.ContentType);
                    }
                    catch (Exception ex) when (
                        ex is FileNotFoundException ||
                        ex is AmazonS3Exception { StatusCode: HttpStatusCode.NotFound })
                    {
                        _logger.LogWarning(
                            ex,
                            "Legacy local file reference not found in cloud storage | PaymentIntentId: {PaymentIntentId} | ObjectKey: {ObjectKey}",
                            paymentIntentId,
                            legacyObjectKey);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to retrieve legacy local file reference | PaymentIntentId: {PaymentIntentId} | ObjectKey: {ObjectKey}",
                            paymentIntentId,
                            legacyObjectKey);
                    }
                }
            }
        }

        _logger.LogWarning(
            "Manual wallet proof reference rejected - not a trusted tenant-scoped storage key and no legacy fallback succeeded | PaymentIntentId: {PaymentIntentId} | ProofImageUrl: {ProofImageUrl}",
            paymentIntentId,
            paymentIntent.ProofImageUrl);
        return NotFound();
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private string GetCurrentAdminId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub")
               ?? User.Identity?.Name
               ?? throw new UnauthorizedAccessException();
    }

    private static IActionResult ManualPaymentAdmissionRejected(
        ManualPaymentApplicationException exception,
        string traceId)
    {
        var statusCode = exception.FailureType == ManualPaymentApplicationFailureType.Configuration
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status409Conflict;

        return new ObjectResult(new ApiErrorResponse(exception.Code, exception.Message, traceId))
        {
            StatusCode = statusCode
        };
    }
}
