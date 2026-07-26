using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Security;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Controllers.Admin;

/// <summary>
/// Admin Abandoned Cart Recovery Controller
/// Phase: Smart Abandoned Cart Recovery Engine - Foundation
/// 
/// Base Path: /api/admin/ecommerce/abandoned-cart-recovery
/// 
/// Endpoints:
/// - GET /settings (get automation settings)
/// - PUT /settings (update automation settings)
/// - GET /eligibility (get eligibility status)
/// </summary>
[ApiController]
[Route("api/admin/ecommerce/abandoned-cart-recovery")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminAbandonedCartRecoveryController : ControllerBase
{
    private const string RecoveryApiKeyHeader = "X-Recovery-Api-Key";

    private readonly ITenantContext _tenantContext;
    private readonly ITenantAutomationSettingsService _settingsService;
    private readonly IAbandonedCartEligibilityService _eligibilityService;
    private readonly IAbandonedCartRecoveryTelemetryService _telemetryService;
    private readonly AbandonedCartRecoveryTelemetry _operationalTelemetry;
    private readonly ILogger<AdminAbandonedCartRecoveryController> _logger;

    public AdminAbandonedCartRecoveryController(
        ITenantContext tenantContext,
        ITenantAutomationSettingsService settingsService,
        IAbandonedCartEligibilityService eligibilityService,
        IAbandonedCartRecoveryTelemetryService telemetryService,
        AbandonedCartRecoveryTelemetry operationalTelemetry,
        ILogger<AdminAbandonedCartRecoveryController> logger)
    {
        _tenantContext = tenantContext;
        _settingsService = settingsService;
        _eligibilityService = eligibilityService;
        _telemetryService = telemetryService;
        _operationalTelemetry = operationalTelemetry;
        _logger = logger;
    }

    /// <summary>
    /// Gets automation settings for the tenant.
    /// Returns default settings if none exist.
    /// 
    /// GET /api/admin/ecommerce/abandoned-cart-recovery/settings
    /// Tenant scope comes from trusted tenant context.
    /// </summary>
    [HttpGet("settings")]
    [ProducesResponseType(typeof(AutomationSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AutomationApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSettings(CancellationToken ct = default)
    {
        var traceId = GetTraceId();
        var tenantId = _tenantContext.RequiredTenantId;

        _logger.LogInformation(
            "[Abandoned Cart Recovery] GetSettings | TenantId: {TenantId} | TraceId: {TraceId}",
            tenantId, traceId);

        try
        {
            var settings = await _settingsService.GetSettingsAsync(tenantId, ct);

            var response = new AutomationSettingsResponse
            {
                EnableAbandonedRecovery = settings.EnableAbandonedRecovery,
                FirstMessageDelayMinutes = settings.FirstMessageDelayMinutes,
                SecondMessageDelayMinutes = settings.SecondMessageDelayMinutes,
                WorkingHoursFrom = settings.WorkingHoursFrom.ToString(@"hh\:mm"),
                WorkingHoursTo = settings.WorkingHoursTo.ToString(@"hh\:mm"),
                UpdatedAtUtc = settings.UpdatedAtUtc,
                CreatedAtUtc = settings.CreatedAtUtc
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Abandoned Cart Recovery] GetSettings error | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AutomationApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Updates automation settings for the tenant.
    /// Creates if not exists, updates if exists.
    /// 
    /// PUT /api/admin/ecommerce/abandoned-cart-recovery/settings
    /// Tenant scope comes from trusted tenant context.
    /// </summary>
    [HttpPut("settings")]
    [ProducesResponseType(typeof(AutomationSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AutomationApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateAutomationSettingsRequest request,
        CancellationToken ct = default)
    {
        var traceId = GetTraceId();
        var tenantId = _tenantContext.RequiredTenantId;

        if (!ModelState.IsValid)
        {
            var validationErrors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .ToArray();
            var errorMessage = validationErrors.Length > 0
                ? string.Join(", ", validationErrors)
                : "Validation failed.";
            return BadRequest(AutomationApiErrorResponse.ValidationError(errorMessage, traceId));
        }

        _logger.LogInformation(
            "[Abandoned Cart Recovery] UpdateSettings | TenantId: {TenantId} | Enabled: {Enabled} | TraceId: {TraceId}",
            tenantId, request.EnableAbandonedRecovery, traceId);

        try
        {
            var workingHoursFrom = TimeSpan.Parse(request.WorkingHoursFrom);
            var workingHoursTo = TimeSpan.Parse(request.WorkingHoursTo);

            if (workingHoursFrom >= workingHoursTo)
            {
                return BadRequest(AutomationApiErrorResponse.ValidationError(
                    "Working hours 'from' must be before 'to'", traceId));
            }

            var settings = new TenantAutomationSettings
            {
                TenantId = tenantId,
                EnableAbandonedRecovery = request.EnableAbandonedRecovery,
                FirstMessageDelayMinutes = request.FirstMessageDelayMinutes,
                SecondMessageDelayMinutes = request.SecondMessageDelayMinutes,
                WorkingHoursFrom = workingHoursFrom,
                WorkingHoursTo = workingHoursTo
            };

            var updated = await _settingsService.UpdateSettingsAsync(tenantId, settings, ct);

            var response = new AutomationSettingsResponse
            {
                EnableAbandonedRecovery = updated.EnableAbandonedRecovery,
                FirstMessageDelayMinutes = updated.FirstMessageDelayMinutes,
                SecondMessageDelayMinutes = updated.SecondMessageDelayMinutes,
                WorkingHoursFrom = updated.WorkingHoursFrom.ToString(@"hh\:mm"),
                WorkingHoursTo = updated.WorkingHoursTo.ToString(@"hh\:mm"),
                UpdatedAtUtc = updated.UpdatedAtUtc,
                CreatedAtUtc = updated.CreatedAtUtc
            };

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex,
                "[Abandoned Cart Recovery] Validation error | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return BadRequest(AutomationApiErrorResponse.ValidationError(ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Abandoned Cart Recovery] UpdateSettings error | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AutomationApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Gets eligibility status for abandoned cart recovery.
    /// </summary>
    [HttpGet("eligibility")]
    [ProducesResponseType(typeof(AbandonedCartEligibilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AutomationApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetEligibility(CancellationToken ct = default)
    {
        var traceId = GetTraceId();
        var tenantId = _tenantContext.RequiredTenantId;

        _logger.LogInformation(
            "[Abandoned Cart Recovery] GetEligibility | TenantId: {TenantId} | TraceId: {TraceId}",
            tenantId, traceId);

        try
        {
            var status = await _eligibilityService.GetEligibilityStatusAsync(tenantId, ct);

            var response = new AbandonedCartEligibilityResponse
            {
                IsEligible = status.IsEligible,
                HasWhatsAppAccount = status.HasWhatsAppAccount,
                WhatsAppAccountEnabledForRecovery = status.WhatsAppAccountEnabledForRecovery,
                WhatsAppAccountConnected = status.WhatsAppAccountConnected,
                AutomationEnabled = status.AutomationEnabled,
                IneligibilityReason = status.IneligibilityReason,
                CheckedAtUtc = status.CheckedAtUtc
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Abandoned Cart Recovery] GetEligibility error | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AutomationApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Logs recovery workflow results from automation callbacks.
    /// Accepts either an authenticated admin principal or a valid callback API key.
    /// </summary>
    [HttpPost("recovery-log")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [RequestSizeLimit(WebhookRequestLimits.ShippingProviderMaxBytes)]
    [EnableRateLimiting(WebhookRateLimiterPolicy.PolicyName)]
    [ProducesResponseType(typeof(AbandonedCartRecoveryLogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AbandonedCartRecoveryLogResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AbandonedCartRecoveryLogResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AbandonedCartRecoveryLogResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(AbandonedCartRecoveryLogResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(AbandonedCartRecoveryLogResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> LogRecoveryResult(
        [FromBody] AbandonedCartRecoveryLogRequest request,
        CancellationToken ct = default)
    {
        var traceId = GetTraceId();

        if (request.TenantId == Guid.Empty)
        {
            _operationalTelemetry.RecordCallbackValidationFailure(Guid.Empty, "missing_tenant_id");
            return BadRequest(new AbandonedCartRecoveryLogResponse
            {
                Success = false,
                Message = "TenantId is required"
            });
        }

        if (request.AbandonedCartId == Guid.Empty)
        {
            _operationalTelemetry.RecordCallbackValidationFailure(request.TenantId, "missing_abandoned_cart_id");
            return BadRequest(new AbandonedCartRecoveryLogResponse
            {
                Success = false,
                Message = "AbandonedCartId is required"
            });
        }

        var isPrivilegedCaller = IsPrivilegedRecoveryPrincipal();
        if (isPrivilegedCaller &&
            (!_tenantContext.TenantId.HasValue || _tenantContext.TenantId.Value != request.TenantId))
        {
            _operationalTelemetry.RecordCallbackAuthFailure(request.TenantId, "admin_tenant_scope_mismatch");
            _logger.LogWarning(
                "[Abandoned Cart Recovery] Rejected cross-tenant admin recovery log | RequestedTenantId: {RequestedTenantId} | TrustedTenantId: {TrustedTenantId} | TraceId: {TraceId}",
                request.TenantId,
                _tenantContext.TenantId,
                traceId);
            return StatusCode(StatusCodes.Status403Forbidden, new AbandonedCartRecoveryLogResponse
            {
                Success = false,
                Message = "Tenant scope mismatch"
            });
        }

        _logger.LogInformation(
            "[Abandoned Cart Recovery] Recovery log received | TenantId: {TenantId} | AbandonedCartId: {AbandonedCartId} | TraceId: {TraceId}",
            request.TenantId,
            request.AbandonedCartId,
            traceId);

        try
        {
            var settings = await _settingsService.GetSettingsAsync(request.TenantId, ct);

            if (!isPrivilegedCaller)
            {
                if (string.IsNullOrWhiteSpace(settings.N8nWebhookApiKey))
                {
                    _operationalTelemetry.RecordCallbackAuthFailure(request.TenantId, "missing_callback_api_key_configuration");
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, new AbandonedCartRecoveryLogResponse
                    {
                        Success = false,
                        Message = "Recovery callback API key is not configured"
                    });
                }

                var providedApiKey = ResolveRecoveryApiKey(request);
                if (!IsValidApiKey(settings.N8nWebhookApiKey, providedApiKey))
                {
                    _operationalTelemetry.RecordCallbackAuthFailure(request.TenantId, "invalid_api_key");
                    _logger.LogWarning(
                        "[Abandoned Cart Recovery] Invalid callback authentication | TenantId: {TenantId} | AbandonedCartId: {AbandonedCartId} | TraceId: {TraceId}",
                        request.TenantId,
                        request.AbandonedCartId,
                        traceId);

                    return Unauthorized(new AbandonedCartRecoveryLogResponse
                    {
                        Success = false,
                        Message = "Invalid API key"
                    });
                }
            }

            if (!TryParseOptionalUtcTimestamp(request.FirstMessageSentAt, out var firstMessageSentAtUtc))
            {
                _operationalTelemetry.RecordCallbackValidationFailure(request.TenantId, "invalid_first_message_timestamp");
                return BadRequest(new AbandonedCartRecoveryLogResponse
                {
                    Success = false,
                    Message = "FirstMessageSentAt must be a valid timestamp"
                });
            }

            if (!TryParseOptionalUtcTimestamp(request.SecondMessageSentAt, out var secondMessageSentAtUtc))
            {
                _operationalTelemetry.RecordCallbackValidationFailure(request.TenantId, "invalid_second_message_timestamp");
                return BadRequest(new AbandonedCartRecoveryLogResponse
                {
                    Success = false,
                    Message = "SecondMessageSentAt must be a valid timestamp"
                });
            }

            if (!TryParseOptionalUtcTimestamp(request.CompletedAt, out var completedAtUtc))
            {
                _operationalTelemetry.RecordCallbackValidationFailure(request.TenantId, "invalid_completion_timestamp");
                return BadRequest(new AbandonedCartRecoveryLogResponse
                {
                    Success = false,
                    Message = "CompletedAt must be a valid timestamp"
                });
            }

            var command = new AbandonedCartRecoveryLogCommand(
                request.TenantId,
                request.AbandonedCartId,
                request.FirstMessageSent,
                firstMessageSentAtUtc,
                request.FirstMessageError,
                request.SecondMessageSent,
                secondMessageSentAtUtc,
                request.SecondMessageError,
                completedAtUtc,
                BuildIdempotencyKey(request, completedAtUtc, firstMessageSentAtUtc, secondMessageSentAtUtc));

            var result = await _telemetryService.RecordWorkflowResultAsync(command, ct);
            if (result.NotFound)
            {
                _operationalTelemetry.RecordCallbackValidationFailure(request.TenantId, "abandoned_cart_not_found");
                return NotFound(new AbandonedCartRecoveryLogResponse
                {
                    Success = false,
                    Message = "Abandoned cart not found"
                });
            }

            if (result.IsDuplicate)
            {
                _operationalTelemetry.RecordCallbackDuplicate(request.TenantId);
                return Ok(new AbandonedCartRecoveryLogResponse
                {
                    Success = true,
                    Message = "Recovery log already recorded"
                });
            }

            _operationalTelemetry.RecordCallbackSuccess(request.TenantId);
            return Ok(new AbandonedCartRecoveryLogResponse
            {
                Success = true,
                Message = "Recovery log recorded successfully"
            });
        }
        catch (Exception ex)
        {
            _operationalTelemetry.RecordCallbackPersistenceFailure(request.TenantId, "unhandled_exception");
            _logger.LogError(ex,
                "[Abandoned Cart Recovery] Error logging recovery result | TenantId: {TenantId} | AbandonedCartId: {AbandonedCartId} | TraceId: {TraceId}",
                request.TenantId,
                request.AbandonedCartId,
                traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new AbandonedCartRecoveryLogResponse
            {
                Success = false,
                Message = "Recovery log processing failed"
            });
        }
    }

    /// <summary>
    /// Gets abandoned cart recovery statistics.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(AbandonedCartStatsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(CancellationToken ct = default)
    {
        var traceId = GetTraceId();
        var tenantId = _tenantContext.RequiredTenantId;

        _logger.LogInformation(
            "[Abandoned Cart Recovery] GetStats | TenantId: {TenantId} | TraceId: {TraceId}",
            tenantId, traceId);

        try
        {
            var stats = await _telemetryService.GetStatsAsync(tenantId, ct);
            var response = new AbandonedCartStatsResponse
            {
                TotalAbandoned = stats.TotalAbandoned,
                MessagesSent = stats.MessagesSent,
                Recovered = stats.Recovered,
                RecoveryRate = stats.RecoveryRate
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _operationalTelemetry.RecordStatsFailure(tenantId);
            _logger.LogError(ex,
                "[Abandoned Cart Recovery] GetStats error | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AutomationApiErrorResponse.InternalError(traceId));
        }
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private bool IsPrivilegedRecoveryPrincipal()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return User.IsInRole("Owner") ||
               User.IsInRole("Admin") ||
               User.IsInRole("Manager");
    }

    private string? ResolveRecoveryApiKey(AbandonedCartRecoveryLogRequest request)
    {
        if (Request.Headers.TryGetValue(RecoveryApiKeyHeader, out var headerValue))
        {
            var headerApiKey = headerValue.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerApiKey))
            {
                return headerApiKey.Trim();
            }
        }

        return string.IsNullOrWhiteSpace(request.ApiKey)
            ? null
            : request.ApiKey.Trim();
    }

    private static bool IsValidApiKey(string expectedApiKey, string? providedApiKey)
    {
        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedApiKey.Trim());
        var providedBytes = Encoding.UTF8.GetBytes(providedApiKey.Trim());

        if (expectedBytes.Length != providedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private static bool TryParseOptionalUtcTimestamp(string? input, out DateTime? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(
                input,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return false;
        }

        value = parsed.UtcDateTime;
        return true;
    }

    private static string BuildIdempotencyKey(
        AbandonedCartRecoveryLogRequest request,
        DateTime? completedAtUtc,
        DateTime? firstMessageSentAtUtc,
        DateTime? secondMessageSentAtUtc)
    {
        var stableTimestamp = completedAtUtc?.ToString("O")
            ?? firstMessageSentAtUtc?.ToString("O")
            ?? secondMessageSentAtUtc?.ToString("O")
            ?? "no-timestamp";

        return $"recovery:{request.TenantId:D}:{request.AbandonedCartId:D}:{stableTimestamp}:{request.FirstMessageSent}:{request.SecondMessageSent}";
    }
}
