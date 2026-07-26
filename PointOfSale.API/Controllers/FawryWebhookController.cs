using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.Attributes;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Fawry Webhook Controller.
///
/// Route: POST /api/webhooks/fawry
///
/// CRITICAL SECURITY RULES:
/// - Signature MUST be verified using the tenant-specific webhook secret
/// - All events are idempotent and replay-safe
/// - NEVER acknowledge unresolved failures with 200
/// - Webhook is the source of truth for payment status
/// </summary>
[ApiController]
[Route("api/webhooks/fawry")]
[AllowAnonymous]
[AllowAnonymousTenant]
public class FawryWebhookController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPaymentWebhookRequestPipeline _webhookRequestPipeline;
    private readonly IFawrySensitiveArtifactProtector _sensitiveArtifactProtector;
    private readonly IPaymentLifecycleService _paymentLifecycleService;
    private readonly IFawryWebhookRuntimeIntegrationService _fawryWebhookRuntimeIntegrationService;
    private readonly ILogger<FawryWebhookController> _logger;

    private const string FawrySignatureHeader = "X-Fawry-Signature";

    public FawryWebhookController(
        ApplicationDbContext dbContext,
        IPaymentWebhookRequestPipeline webhookRequestPipeline,
        IFawrySensitiveArtifactProtector sensitiveArtifactProtector,
        IPaymentLifecycleService paymentLifecycleService,
        IFawryWebhookRuntimeIntegrationService fawryWebhookRuntimeIntegrationService,
        ILogger<FawryWebhookController> logger)
    {
        _dbContext = dbContext;
        _webhookRequestPipeline = webhookRequestPipeline;
        _sensitiveArtifactProtector = sensitiveArtifactProtector;
        _paymentLifecycleService = paymentLifecycleService;
        _fawryWebhookRuntimeIntegrationService = fawryWebhookRuntimeIntegrationService;
        _logger = logger;
    }

    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(WebhookRequestLimits.PaymentProviderMaxBytes)]
    [EnableRateLimiting(WebhookRateLimiterPolicy.PolicyName)]
    [ProducesResponseType(typeof(WebhookResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(WebhookResponseDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Handle(CancellationToken ct = default)
    {
        var traceId = HttpContext.TraceIdentifier;
        var eventId = string.Empty;
        var eventType = string.Empty;
        PaymentWebhookLog? webhookLog = null;
        var wasPreviouslyFailed = false;

        PaymentWebhookRequestContext webhookContext;
        try
        {
            webhookContext = await _webhookRequestPipeline.PrepareAsync(
                HttpContext,
                "fawry",
                traceId,
                request => request.Headers[FawrySignatureHeader].FirstOrDefault() ?? "messageSignature",
                request =>
                {
                    if (request.Query.TryGetValue("tenant", out var tenantQuery) &&
                        Guid.TryParse(tenantQuery, out var tenantId))
                    {
                        return tenantId;
                    }

                    return null;
                },
                ct);
        }
        catch (PaymentWebhookRequestPreparationException ex) when (ex.UseApiErrorResponse)
        {
            return StatusCode(ex.StatusCode, ex.ToApiError(traceId));
        }
        catch (PaymentWebhookRequestPreparationException ex)
        {
            return StatusCode(ex.StatusCode, Failure(ex.Message));
        }

        try
        {
            var admissionResult = await _fawryWebhookRuntimeIntegrationService.EvaluateAndExecuteAsync(
                new FawryWebhookRuntimeAdmissionRequest(
                    webhookContext.TenantId,
                    webhookContext.RawPayload,
                    traceId,
                    FawryWebhookEvidenceKind.Callback,
                    ReceivedAtUtc: DateTimeOffset.UtcNow),
                ct);

            if (admissionResult.InvalidCallback)
            {
                return BadRequest(Failure("Webhook payload is invalid."));
            }

            var adapterErrorCode = admissionResult.AdapterResult?.Error?.Code;
            if (string.Equals(adapterErrorCode, "FAWRY_WEBHOOK_SIGNATURE_INVALID", StringComparison.Ordinal) ||
                string.Equals(adapterErrorCode, "FAWRY_WEBHOOK_SECRET_MISSING", StringComparison.Ordinal))
            {
                WebhookRequestBodyReader.RecordInvalidSignature(HttpContext, "fawry", "payment");
                return Unauthorized(new ApiErrorResponse("INVALID_SIGNATURE", "Invalid webhook signature.", traceId));
            }

            eventId = admissionResult.ProviderEventId;
            eventType = string.IsNullOrWhiteSpace(admissionResult.EventType)
                ? admissionResult.Admission.ReasonCode
                : admissionResult.EventType;

            if (string.IsNullOrWhiteSpace(eventId))
            {
                return StatusCode(StatusCodes.Status500InternalServerError, Failure("Webhook event ID is required for idempotent processing."));
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                ct);

            try
            {
                var preparation = await PrepareWebhookLogAsync(
                    webhookContext.TenantId,
                    eventId,
                    eventType,
                    webhookContext.RawPayload,
                    ExtractMessageSignature(webhookContext.RawPayload) ?? webhookContext.Signature,
                    ct);

                webhookLog = preparation.Log;
                wasPreviouslyFailed = preparation.WasPreviouslyFailed;

                if (preparation.IsDuplicate)
                {
                    await transaction.RollbackAsync(ct);

                    _logger.LogInformation(
                        "[Fawry Webhook] Duplicate event ignored | EventId: {EventId} | TenantId: {TenantId} | TraceId: {TraceId}",
                        eventId, webhookContext.TenantId, traceId);

                    return Ok(new WebhookResponseDto
                    {
                        Processed = true,
                        Message = "Duplicate event ignored",
                        EventLogId = webhookLog.Id
                    });
                }

                if (preparation.IsInFlight)
                {
                    await transaction.RollbackAsync(ct);

                    _logger.LogWarning(
                        "[Fawry Webhook] Event already in flight | EventId: {EventId} | TenantId: {TenantId} | TraceId: {TraceId}",
                        eventId, webhookContext.TenantId, traceId);

                    return StatusCode(StatusCodes.Status500InternalServerError, new WebhookResponseDto
                    {
                        Processed = false,
                        Message = "Webhook event is already being processed. Please retry.",
                        EventLogId = webhookLog.Id
                    });
                }

                webhookLog.PaymentIntentId = admissionResult.PaymentIntentId;
                webhookLog.OrderId = admissionResult.OrderId;

                await _dbContext.SaveChangesAsync(ct);

                WebhookResponseDto result;
                if (FawryWebhookLifecycleAdmission.IsLifecyclePersistenceAllowed(admissionResult))
                {
                    var processedAtUtc = DateTime.UtcNow;
                    var lifecycleRequest = FawryWebhookLifecycleAdmission.CreateRequest(
                        admissionResult,
                        processedAtUtc);

                    var lifecycleResult = await _paymentLifecycleService.HandlePaymentResultAsync(
                        lifecycleRequest,
                        ct);

                    if (!lifecycleResult.Success)
                    {
                        if (lifecycleResult.RequiresManualReview)
                        {
                            StorefrontSuspenseRecoveryPolicy.ApplyManualReviewMarker(
                                lifecycleResult,
                                webhookLog,
                                lifecycleRequest.PaymentReference);

                            result = new WebhookResponseDto
                            {
                                Processed = true,
                                Message = lifecycleResult.ErrorMessage ?? "Payment quarantined for manual review.",
                                EventLogId = webhookLog.Id
                            };
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"Failed to record Fawry payment outcome: {lifecycleResult.ErrorMessage}");
                        }
                    }
                    else
                    {
                        await _fawryWebhookRuntimeIntegrationService.PersistReplayRecordAsync(
                            admissionResult,
                            admissionResult.PaymentIntentId,
                            admissionResult.OrderId,
                            ct);

                        result = new WebhookResponseDto
                        {
                            Processed = true,
                            Message = lifecycleResult.StateChanged
                                ? "Webhook processed successfully"
                                : "Webhook replay processed idempotently",
                            EventLogId = webhookLog.Id
                        };
                    }
                }
                else
                {
                    webhookLog.ProcessingStatus = WebhookProcessingStatus.Skipped;
                    result = new WebhookResponseDto
                    {
                        Processed = true,
                        Message = admissionResult.RejectedAsDuplicate || admissionResult.RejectedAsReplay
                            ? "Duplicate event ignored"
                            : $"Webhook ignored: {admissionResult.Admission.ReasonCode}",
                        EventLogId = webhookLog.Id
                    };
                }

                var elapsed = DateTime.UtcNow - webhookLog.CreatedAt;
                if (webhookLog.ProcessingStatus != WebhookProcessingStatus.Skipped)
                {
                    webhookLog.ProcessingStatus = WebhookProcessingStatus.Completed;
                }
                webhookLog.ProcessedAt = DateTime.UtcNow;
                webhookLog.ProcessingDurationMs = Math.Max(0, (long)elapsed.TotalMilliseconds);

                await _dbContext.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return Ok(result);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await transaction.RollbackAsync(ct);
                _dbContext.ChangeTracker.Clear();

                var existingLog = await TryGetWebhookLogAsync(webhookContext.TenantId, eventId, ct);
                if (existingLog != null &&
                    (existingLog.ProcessingStatus == WebhookProcessingStatus.Completed ||
                     existingLog.ProcessingStatus == WebhookProcessingStatus.Skipped))
                {
                    return Ok(new WebhookResponseDto
                    {
                        Processed = true,
                        Message = "Duplicate event ignored",
                        EventLogId = existingLog.Id
                    });
                }

                if (existingLog?.ProcessingStatus == WebhookProcessingStatus.Failed)
                {
                    _logger.LogCritical(
                        "[Fawry Webhook] Repeated failure detected | EventId: {EventId} | OrderId: {OrderId} | TenantId: {TenantId} | TraceId: {TraceId}",
                        eventId,
                        existingLog.OrderId,
                        webhookContext.TenantId,
                        traceId);
                }

                return StatusCode(StatusCodes.Status500InternalServerError, new WebhookResponseDto
                {
                    Processed = false,
                    Message = "Webhook event is already being processed or awaiting retry.",
                    EventLogId = existingLog?.Id
                });
            }
        }
        catch (Exception ex)
        {
            try
            {
                if (_dbContext.Database.CurrentTransaction != null)
                {
                    await _dbContext.Database.RollbackTransactionAsync(CancellationToken.None);
                }
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(
                    rollbackEx,
                    "[Fawry Webhook] Transaction rollback failed | EventId: {EventId} | TraceId: {TraceId}",
                    eventId,
                    traceId);
            }

            if (webhookLog != null)
            {
                await PersistWebhookFailureAsync(
                    webhookLog,
                    webhookContext.TenantId,
                    eventId,
                    eventType,
                    webhookContext.RawPayload,
                    ExtractMessageSignature(webhookContext.RawPayload) ?? webhookContext.Signature,
                    ex.Message,
                    wasPreviouslyFailed,
                    traceId,
                    CancellationToken.None);
            }

            _logger.LogError(
                ex,
                "[Fawry Webhook] Processing failed | EventId: {EventId} | TenantId: {TenantId} | TraceId: {TraceId}",
                eventId,
                webhookContext.TenantId,
                traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new WebhookResponseDto
            {
                Processed = false,
                Message = "Webhook processing failed. Provider should retry.",
                EventLogId = webhookLog?.Id
            });
        }
    }

    private async Task<PaymentWebhookLog?> TryGetWebhookLogAsync(
        Guid tenantId,
        string eventId,
        CancellationToken ct)
    {
        return await _dbContext.PaymentWebhookLogs
            .FirstOrDefaultAsync(
                log => log.TenantId == tenantId &&
                       log.Provider == "fawry" &&
                       log.ProviderEventId == eventId,
                ct);
    }

    private async Task<(PaymentWebhookLog Log, bool IsDuplicate, bool IsInFlight, bool WasPreviouslyFailed)> PrepareWebhookLogAsync(
        Guid tenantId,
        string eventId,
        string eventType,
        string rawPayload,
        string signature,
        CancellationToken ct)
    {
        var protectedPayload = _sensitiveArtifactProtector.ProtectWebhookPayload(rawPayload);
        var signatureDigest = _sensitiveArtifactProtector.DigestMessageSignature(signature);
        var existingLog = await TryGetWebhookLogAsync(tenantId, eventId, ct);
        if (existingLog != null)
        {
            if (existingLog.ProcessingStatus == WebhookProcessingStatus.Completed ||
                existingLog.ProcessingStatus == WebhookProcessingStatus.Skipped)
            {
                return (existingLog, true, false, false);
            }

            if (existingLog.ProcessingStatus == WebhookProcessingStatus.Processing)
            {
                return (existingLog, false, true, false);
            }

            var wasPreviouslyFailed = existingLog.ProcessingStatus == WebhookProcessingStatus.Failed;
            existingLog.EventType = eventType;
            existingLog.RawPayload = protectedPayload;
            existingLog.Signature = signatureDigest;
            existingLog.SignatureVerified = true;
            existingLog.ProcessingStatus = WebhookProcessingStatus.Processing;
            existingLog.ProcessingError = null;
            existingLog.ProcessedAt = null;
            existingLog.ProcessingDurationMs = null;
            return (existingLog, false, false, wasPreviouslyFailed);
        }

        var webhookLog = new PaymentWebhookLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = "fawry",
            ProviderEventId = eventId,
            EventType = eventType,
            RawPayload = protectedPayload,
            Signature = signatureDigest,
            SignatureVerified = true,
            ProcessingStatus = WebhookProcessingStatus.Processing,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PaymentWebhookLogs.Add(webhookLog);
        return (webhookLog, false, false, false);
    }

    private async Task PersistWebhookFailureAsync(
        PaymentWebhookLog webhookLog,
        Guid tenantId,
        string eventId,
        string eventType,
        string rawPayload,
        string signature,
        string error,
        bool wasPreviouslyFailed,
        string traceId,
        CancellationToken ct)
    {
        try
        {
            var protectedPayload = _sensitiveArtifactProtector.ProtectWebhookPayload(rawPayload);
            var signatureDigest = _sensitiveArtifactProtector.DigestMessageSignature(signature);
            _dbContext.ChangeTracker.Clear();

            var persistedLog = await TryGetWebhookLogAsync(tenantId, eventId, ct);
            if (persistedLog == null)
            {
                var elapsed = DateTime.UtcNow - (webhookLog.CreatedAt == default ? DateTime.UtcNow : webhookLog.CreatedAt);
                persistedLog = new PaymentWebhookLog
                {
                    Id = webhookLog.Id,
                    TenantId = tenantId,
                    Provider = "fawry",
                    ProviderEventId = eventId,
                    EventType = eventType,
                    PaymentIntentId = webhookLog.PaymentIntentId,
                    OrderId = webhookLog.OrderId,
                    RawPayload = protectedPayload,
                    Signature = signatureDigest,
                    SignatureVerified = true,
                    ProcessingStatus = WebhookProcessingStatus.Failed,
                    ProcessingError = error,
                    ProcessedAt = DateTime.UtcNow,
                    ProcessingDurationMs = Math.Max(0, (long)elapsed.TotalMilliseconds),
                    CreatedAt = webhookLog.CreatedAt == default ? DateTime.UtcNow : webhookLog.CreatedAt
                };

                _dbContext.PaymentWebhookLogs.Add(persistedLog);
            }
            else
            {
                var elapsed = DateTime.UtcNow - persistedLog.CreatedAt;
                persistedLog.EventType = eventType;
                persistedLog.PaymentIntentId = webhookLog.PaymentIntentId ?? persistedLog.PaymentIntentId;
                persistedLog.OrderId = webhookLog.OrderId ?? persistedLog.OrderId;
                persistedLog.RawPayload = protectedPayload;
                persistedLog.Signature = signatureDigest;
                persistedLog.SignatureVerified = true;
                persistedLog.ProcessingStatus = WebhookProcessingStatus.Failed;
                persistedLog.ProcessingError = error;
                persistedLog.ProcessedAt = DateTime.UtcNow;
                persistedLog.ProcessingDurationMs = Math.Max(0, (long)elapsed.TotalMilliseconds);
            }

            await _dbContext.SaveChangesAsync(ct);

            if (wasPreviouslyFailed)
            {
                _logger.LogCritical(
                    "[Fawry Webhook] Repeated failure persisted | EventId: {EventId} | OrderId: {OrderId} | TenantId: {TenantId} | TraceId: {TraceId}",
                    eventId,
                    persistedLog.OrderId,
                    tenantId,
                    traceId);
            }
        }
        catch (Exception persistenceEx)
        {
            _logger.LogError(
                persistenceEx,
                "[Fawry Webhook] Failed to persist failure log | EventId: {EventId} | TenantId: {TenantId} | TraceId: {TraceId}",
                eventId,
                tenantId,
                traceId);
        }
    }

    private static WebhookResponseDto Failure(string message) => new()
    {
        Processed = false,
        Message = message
    };

    private static string? ExtractMessageSignature(string rawPayload)
    {
        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            return document.RootElement.TryGetProperty("messageSignature", out var signatureElement)
                ? signatureElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
}
