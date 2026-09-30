using System.Diagnostics;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PointOfSale.API.DTOs.Admin;
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
            var isPersisted = await _settingsService.ExistsAsync(tenantId, ct);

            var response = new AutomationSettingsResponse
            {
                EnableAbandonedRecovery = settings.EnableAbandonedRecovery,
                FirstMessageDelayMinutes = settings.FirstMessageDelayMinutes,
                SecondMessageDelayMinutes = settings.SecondMessageDelayMinutes,
                WorkingHoursFrom = settings.WorkingHoursFrom.ToString(@"hh\:mm"),
                WorkingHoursTo = settings.WorkingHoursTo.ToString(@"hh\:mm"),
                TimeZoneId = settings.TimeZoneId,
                IsPersisted = isPersisted,
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
            if (request.EnableAbandonedRecovery)
            {
                var readiness = await _eligibilityService.GetEligibilityStatusAsync(tenantId, ct);
                var activationFailure = GetActivationFailure(readiness);
                if (activationFailure != null)
                {
                    return BadRequest(AutomationApiErrorResponse.ValidationError(
                        activationFailure.Value.Reason,
                        traceId,
                        activationFailure.Value.Code));
                }
            }

            var workingHoursFrom = TimeSpan.Parse(request.WorkingHoursFrom);
            var workingHoursTo = TimeSpan.Parse(request.WorkingHoursTo);

            if (workingHoursFrom == workingHoursTo)
            {
                return BadRequest(AutomationApiErrorResponse.ValidationError(
                    "Working hours must define a non-empty sending window", traceId));
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
                TimeZoneId = updated.TimeZoneId,
                IsPersisted = true,
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
                WhatsAppAccountEnabled = status.WhatsAppAccountEnabled,
                WhatsAppAccountConnected = status.WhatsAppAccountConnected,
                AutomationEnabled = status.AutomationEnabled,
                NativeSchedulerOperational = status.NativeSchedulerOperational,
                NativeDispatcherOperational = status.NativeDispatcherOperational,
                WhatsAppAccountStatus = status.WhatsAppAccountStatus,
                WhatsAppStatusReason = status.WhatsAppStatusReason,
                TimeZoneId = status.TimeZoneId,
                IneligibilityCode = status.IneligibilityCode,
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

    private static (string Code, string Reason)? GetActivationFailure(AbandonedCartEligibilityStatus status)
    {
        if (!status.HasWhatsAppAccount)
        {
            return ("whatsapp_not_configured", "No WhatsApp account is configured.");
        }

        if (!status.WhatsAppAccountEnabled)
        {
            return ("whatsapp_account_disabled", "The WhatsApp account is disabled.");
        }

        if (!status.WhatsAppAccountConnected)
        {
            return ("whatsapp_disconnected", "The WhatsApp account is not connected.");
        }

        if (!status.NativeSchedulerOperational)
        {
            var hasNativeFailure = status.IneligibilityCode is "native_recovery_disabled" or "native_database_unavailable" or "native_scheduler_unavailable";
            var code = hasNativeFailure ? status.IneligibilityCode! : "native_scheduler_unavailable";
            var reason = hasNativeFailure
                ? status.IneligibilityReason ?? "The native recovery scheduler is unavailable."
                : "The native recovery scheduler is unavailable.";
            return (code, reason);
        }

        if (!status.NativeDispatcherOperational)
        {
            return ("native_dispatcher_unavailable", status.IneligibilityReason ?? "The audited WhatsApp dispatcher is unavailable.");
        }

        return null;
    }

}
