using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin Store Config Controller - POS Admin store configuration management.
/// 
/// PHASE 7.1: THEME & STORE CONFIG SYSTEM
/// 
/// Base Path: /api/admin/store-config
/// 
/// Endpoints:
/// - GET /api/admin/store-config (get store config)
/// - PUT /api/admin/store-config (update store config)
/// 
/// Required Headers:
/// - X-Tenant-Id: GUID (required)
/// 
/// ARCHITECTURAL RULES:
/// - POS Admin is the ONLY source of truth for store config
/// - Full config replacement (no partial patching)
/// - Storefront reads config via separate read-only API
/// </summary>
[ApiController]
[Route("api/admin/store-config")]
[Authorize(Roles = "Owner,Admin")]
public class AdminStoreConfigController : ControllerBase
{
    private readonly IAdminStoreConfigService _storeConfigService;
    private readonly IStoreConfigAuditService _auditService;
    private readonly ITenantContext _tenantContext;
    private readonly IStorefrontThemePreviewTokenService? _themePreviewTokenService;
    private readonly ILogger<AdminStoreConfigController> _logger;

    public AdminStoreConfigController(
        IAdminStoreConfigService storeConfigService,
        IStoreConfigAuditService auditService,
        ITenantContext tenantContext,
        ILogger<AdminStoreConfigController> logger,
        IStorefrontThemePreviewTokenService? themePreviewTokenService = null)
    {
        _storeConfigService = storeConfigService;
        _auditService = auditService;
        _tenantContext = tenantContext;
        _themePreviewTokenService = themePreviewTokenService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the store configuration for the tenant.
    /// Creates a default configuration if none exists.
    /// 
    /// GET /api/admin/store-config
    /// X-Tenant-Id: {tenant-guid}
    /// </summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AdminStoreConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminStoreConfigApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStoreConfig(CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);

        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "GetStoreConfig | TenantId: {TenantId} | TraceId: {TraceId}",
            validation.TenantId, traceId);

        try
        {
            var result = await _storeConfigService.GetStoreConfigAsync(
                validation.TenantId!.Value,
                traceId,
                cancellationToken);

            ApplyConcurrencyHeaders(result.RowVersion);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetStoreConfig | TenantId: {TenantId} | Error | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AdminStoreConfigApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Updates the store configuration for the tenant.
    /// Creates if not exists, replaces if exists.
    /// 
    /// PUT /api/admin/store-config
    /// X-Tenant-Id: {tenant-guid}
    /// 
    /// Request Body:
    /// {
    ///   "storeName": "My Store",
    ///   "storeDescription": "Description",
    ///   "themeId": "default",
    ///   "theme": { ... },
    ///   "behavior": { ... }
    /// }
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(AdminStoreConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminStoreConfigApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateStoreConfig(
        [FromBody] AdminStoreConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);

        if (validation.Error != null)
        {
            return validation.Error;
        }

        // Validate request
        var validationError = request.Validate();
        if (validationError != null)
        {
            _logger.LogWarning(
                "UpdateStoreConfig validation failed | TenantId: {TenantId} | Error: {Error} | TraceId: {TraceId}",
                validation.TenantId, validationError, traceId);
            return BadRequest(AdminStoreConfigApiErrorResponse.InvalidConfig(validationError, traceId));
        }

        var precondition = RequireExpectedRowVersion(traceId, request.RowVersion);
        if (precondition.Error != null)
        {
            return precondition.Error;
        }

        _logger.LogInformation(
            "UpdateStoreConfig | TenantId: {TenantId} | StoreName: {StoreName} | ThemeId: {ThemeId} | TraceId: {TraceId}",
            validation.TenantId, request.StoreName, request.ThemeId, traceId);

        try
        {
            var result = await _storeConfigService.UpdateStoreConfigAsync(
                validation.TenantId!.Value,
                request,
                traceId,
                cancellationToken,
                expectedRowVersion: precondition.RowVersion);

            ApplyConcurrencyHeaders(result.RowVersion);
            return Ok(result);
        }
        catch (StorefrontVerificationException ex)
        {
            // Phase 8.4: Email verification required for storefront publishing
            _logger.LogWarning(
                "UpdateStoreConfig | TenantId: {TenantId} | Blocked: Email not verified | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return BadRequest(new AdminStoreConfigApiErrorResponse
            {
                Code = "EMAIL_NOT_VERIFIED",
                Message = ex.Message,
                TraceId = traceId
            });
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(
                ex,
                "UpdateStoreConfig concurrency conflict | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId,
                traceId);
            RecordAdminConflict("store-config", "stale_row_version");
            return Conflict(AdminStoreConfigApiErrorResponse.ConcurrencyConflict(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "UpdateStoreConfig | TenantId: {TenantId} | Error | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AdminStoreConfigApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Updates only the storefront operational status for the tenant.
    /// This endpoint intentionally does not bind unrelated StoreConfig domains such as Commerce AI.
    /// </summary>
    [HttpPut("status")]
    [ProducesResponseType(typeof(AdminStoreConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminStoreConfigApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateStorefrontStatus(
        [FromBody] AdminStorefrontStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);

        if (validation.Error != null)
        {
            return validation.Error;
        }

        var validationError = request.Validate();
        if (validationError != null)
        {
            _logger.LogWarning(
                "UpdateStorefrontStatus validation failed | TenantId: {TenantId} | Error: {Error} | TraceId: {TraceId}",
                validation.TenantId, validationError, traceId);
            return BadRequest(AdminStoreConfigApiErrorResponse.InvalidConfig(validationError, traceId));
        }

        var precondition = RequireExpectedRowVersion(traceId, request.RowVersion);
        if (precondition.Error != null)
        {
            return precondition.Error;
        }

        try
        {
            var result = await _storeConfigService.UpdateStorefrontStatusAsync(
                validation.TenantId!.Value,
                request,
                traceId,
                cancellationToken,
                expectedRowVersion: precondition.RowVersion);

            ApplyConcurrencyHeaders(result.RowVersion);
            return Ok(result);
        }
        catch (StorefrontVerificationException ex)
        {
            _logger.LogWarning(
                "UpdateStorefrontStatus | TenantId: {TenantId} | Blocked: Email not verified | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return BadRequest(new AdminStoreConfigApiErrorResponse
            {
                Code = "EMAIL_NOT_VERIFIED",
                Message = ex.Message,
                TraceId = traceId
            });
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(
                ex,
                "UpdateStorefrontStatus concurrency conflict | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId,
                traceId);
            RecordAdminConflict("store-config", "stale_row_version");
            return Conflict(AdminStoreConfigApiErrorResponse.ConcurrencyConflict(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "UpdateStorefrontStatus | TenantId: {TenantId} | Error | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                AdminStoreConfigApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Updates a specific page layout (Draft mode).
    /// </summary>
    [HttpPut("pages/{pageId}")]
    [ProducesResponseType(typeof(AdminStoreConfigResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePageLayout(
        [FromRoute] string pageId,
        [FromBody] Dictionary<string, object> pageConfig,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null) return validation.Error;

        var precondition = RequireExpectedRowVersion(traceId);
        if (precondition.Error != null) return precondition.Error;

        try
        {
            var result = await _storeConfigService.UpdatePageLayoutAsync(
                validation.TenantId!.Value,
                pageId,
                pageConfig,
                traceId,
                cancellationToken,
                expectedRowVersion: precondition.RowVersion);

            ApplyConcurrencyHeaders(result.RowVersion);
            return Ok(result);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "UpdatePageLayout concurrency conflict | TenantId: {TenantId}", validation.TenantId);
            RecordAdminConflict($"page:{pageId}", "stale_row_version");
            return Conflict(AdminStoreConfigApiErrorResponse.ConcurrencyConflict(traceId));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                ex,
                "UpdatePageLayout rejected malformed section composition | TenantId: {TenantId} | PageId: {PageId} | TraceId: {TraceId}",
                validation.TenantId,
                pageId,
                traceId);
            return UnprocessableEntity(AdminStoreConfigApiErrorResponse.InvalidConfig(ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdatePageLayout failed | TenantId: {TenantId}", validation.TenantId);
            return StatusCode(500, AdminStoreConfigApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Publishes a specific page from Draft to Live.
    /// </summary>
    [HttpPost("pages/{pageId}/publish")]
    [ProducesResponseType(typeof(AdminStoreConfigResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishPage(
        [FromRoute] string pageId,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null) return validation.Error;

        var precondition = RequireExpectedRowVersion(traceId);
        if (precondition.Error != null) return precondition.Error;

        try
        {
            var result = await _storeConfigService.PublishPageAsync(
                validation.TenantId!.Value,
                pageId,
                traceId,
                cancellationToken,
                expectedRowVersion: precondition.RowVersion);

            ApplyConcurrencyHeaders(result.RowVersion);
            return Ok(result);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "PublishPage concurrency conflict | TenantId: {TenantId}", validation.TenantId);
            RecordAdminConflict($"page:{pageId}", "stale_row_version");
            return Conflict(AdminStoreConfigApiErrorResponse.ConcurrencyConflict(traceId));
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(
                ex,
                "PublishPage target not found | TenantId: {TenantId} | PageId: {PageId} | TraceId: {TraceId}",
                validation.TenantId,
                pageId,
                traceId);
            return NotFound(AdminStoreConfigApiErrorResponse.PublishTargetNotFound(ex.Message, traceId));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                ex,
                "PublishPage rejected invalid draft state | TenantId: {TenantId} | PageId: {PageId} | TraceId: {TraceId}",
                validation.TenantId,
                pageId,
                traceId);
            return UnprocessableEntity(AdminStoreConfigApiErrorResponse.InvalidConfig(ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PublishPage failed | TenantId: {TenantId}", validation.TenantId);
            return StatusCode(500, AdminStoreConfigApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Deletes a stored product template definition.
    /// </summary>
    [HttpDelete("product-templates/{templateId}")]
    [ProducesResponseType(typeof(AdminStoreConfigResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteProductTemplate(
        [FromRoute] string templateId,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null) return validation.Error;

        var precondition = RequireExpectedRowVersion(traceId);
        if (precondition.Error != null) return precondition.Error;

        try
        {
            var result = await _storeConfigService.DeleteProductTemplateAsync(
                validation.TenantId!.Value,
                templateId,
                traceId,
                cancellationToken,
                expectedRowVersion: precondition.RowVersion);

            ApplyConcurrencyHeaders(result.RowVersion);
            return Ok(result);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "DeleteProductTemplate concurrency conflict | TenantId: {TenantId} | TemplateId: {TemplateId}", validation.TenantId, templateId);
            RecordAdminConflict($"product-template:{templateId}", "stale_row_version");
            return Conflict(AdminStoreConfigApiErrorResponse.ConcurrencyConflict(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteProductTemplate failed | TenantId: {TenantId} | TemplateId: {TemplateId}", validation.TenantId, templateId);
            return StatusCode(500, AdminStoreConfigApiErrorResponse.InternalError(traceId));
        }
    }

    // ===================================
    // PHASE 9.3: Theme Presets & Switching
    // ===================================

    /// <summary>
    /// Gets available system theme presets.
    /// Internal API - No Tenant Header required.
    /// </summary>
    [HttpGet("themes/presets")]
    [ProducesResponseType(typeof(List<ThemePresetDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetThemePresets(CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        // Presets are system-level, no tenant isolation required for reading the list
        var results = await _storeConfigService.GetThemePresetsAsync(traceId, cancellationToken);
        return Ok(results);
    }

    /// <summary>
    /// Selects a theme preset for the tenant (Draft Mode).
    /// Resets draft overrides.
    /// </summary>
    [HttpPut("theme/preset/{presetKey}")]
    [ProducesResponseType(typeof(AdminStoreConfigResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SelectThemePreset(
        [FromRoute] string presetKey,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null) return validation.Error;

        var precondition = RequireExpectedRowVersion(traceId);
        if (precondition.Error != null) return precondition.Error;

        try
        {
            var result = await _storeConfigService.SelectThemePresetAsync(
                validation.TenantId!.Value,
                presetKey,
                traceId,
                cancellationToken,
                expectedRowVersion: precondition.RowVersion);
            ApplyConcurrencyHeaders(result.RowVersion);
            return Ok(result);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "SelectThemePreset concurrency conflict");
            RecordAdminConflict("theme", "stale_row_version");
            return Conflict(AdminStoreConfigApiErrorResponse.ConcurrencyConflict(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SelectThemePreset failed");
            return StatusCode(500, AdminStoreConfigApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Updates theme overrides (Draft Mode).
    /// </summary>
    [HttpPut("theme/overrides")]
    [ProducesResponseType(typeof(AdminStoreConfigResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateThemeOverrides(
        [FromBody] object overridesJson, // Receive as raw object, service handles validation
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null) return validation.Error;

        var precondition = RequireExpectedRowVersion(traceId);
        if (precondition.Error != null) return precondition.Error;

        try
        {
            var jsonString = System.Text.Json.JsonSerializer.Serialize(overridesJson);
            var result = await _storeConfigService.UpdateThemeOverridesAsync(
                validation.TenantId!.Value,
                jsonString,
                traceId,
                cancellationToken,
                expectedRowVersion: precondition.RowVersion);
            ApplyConcurrencyHeaders(result.RowVersion);
            return Ok(result);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "UpdateThemeOverrides concurrency conflict");
            RecordAdminConflict("theme", "stale_row_version");
            return Conflict(AdminStoreConfigApiErrorResponse.ConcurrencyConflict(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateThemeOverrides failed");
            return StatusCode(500, AdminStoreConfigApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Publishes the current draft theme (preset + overrides) to live.
    /// </summary>
    [HttpPost("theme/publish")]
    [ProducesResponseType(typeof(AdminStoreConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminStoreConfigApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PublishTheme(CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null) return validation.Error;

        var precondition = RequireExpectedRowVersion(traceId);
        if (precondition.Error != null) return precondition.Error;

        try
        {
            var result = await _storeConfigService.PublishThemeAsync(
                validation.TenantId!.Value,
                traceId,
                cancellationToken,
                expectedRowVersion: precondition.RowVersion);
            ApplyConcurrencyHeaders(result.RowVersion);
            return Ok(result);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict during theme publish");
            RecordAdminConflict("theme", "stale_row_version");
            return Conflict(AdminStoreConfigApiErrorResponse.ConcurrencyConflict(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PublishTheme failed");
            return StatusCode(500, AdminStoreConfigApiErrorResponse.InternalError(traceId));
        }
    }

    // ===================================
    // PHASE 9.6: Versioning & Rollback
    // ===================================

    /// <summary>
    /// Rolls back the store configuration to a specific version.
    /// Requires explicit confirmation to prevent accidental rollbacks.
    /// </summary>
    [HttpPost("rollback/{version}")]
    [ProducesResponseType(typeof(AdminStoreConfigResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RollbackStoreConfig(
        [FromRoute] int version,
        [FromBody] RollbackRequest? request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null) return validation.Error;

        var precondition = RequireExpectedRowVersion(traceId);
        if (precondition.Error != null) return precondition.Error;

        try
        {
            var result = await _storeConfigService.RollbackStoreConfigAsync(
                validation.TenantId!.Value,
                version,
                request?.Confirm ?? false,
                request?.Reason,
                traceId,
                cancellationToken,
                expectedRowVersion: precondition.RowVersion);
            ApplyConcurrencyHeaders(result.RowVersion);
            return Ok(result);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "RollbackStoreConfig concurrency conflict");
            RecordAdminConflict("rollback", "stale_row_version");
            return Conflict(AdminStoreConfigApiErrorResponse.ConcurrencyConflict(traceId));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "RollbackStoreConfig rejected");
            return BadRequest(new AdminStoreConfigApiErrorResponse
            {
                Code = "ROLLBACK_REJECTED",
                Message = ex.Message,
                TraceId = traceId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RollbackStoreConfig failed");
            return StatusCode(500, AdminStoreConfigApiErrorResponse.InternalError(traceId));
        }
    }

    public class RollbackRequest
    {
        public bool Confirm { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Gets the preview configuration (latest draft) for the tenant.
    /// Used by admin preview mode to render the storefront without affecting live.
    /// </summary>
    [HttpGet("preview")]
    [HttpGet("/api/storefront/config/preview")]
    [Authorize(Roles = "Owner,Admin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStoreConfigPreview(CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null) return validation.Error;

        try
        {
            var result = await _storeConfigService.GetStoreConfigPreviewAsync(
                validation.TenantId!.Value,
                traceId,
                cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetStoreConfigPreview failed");
            return StatusCode(500, AdminStoreConfigApiErrorResponse.InternalError(traceId));
        }
    }

    // ===================================
    // PHASE: Storefront Admin Hardening - P1
    // Version History API
    // ===================================

    /// <summary>
    /// Gets the version history for the store configuration.
    /// Returns all stored versions with metadata for display and rollback selection.
    /// </summary>
    [HttpGet("versions")]
    [ProducesResponseType(typeof(VersionHistoryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVersionHistory(CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null) return validation.Error;

        try
        {
            var result = await _storeConfigService.GetVersionHistoryAsync(
                validation.TenantId!.Value,
                traceId,
                cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetVersionHistory failed");
            return StatusCode(500, AdminStoreConfigApiErrorResponse.InternalError(traceId));
        }
    }

    // ===================================
    // PHASE P3: AUDIT TRAIL
    // ===================================

    /// <summary>
    /// Gets the audit history for the store configuration.
    /// Returns paginated audit entries with filtering support.
    /// </summary>
    /// <param name="page">Page number (1-indexed)</param>
    /// <param name="pageSize">Page size (max 100)</param>
    /// <param name="actionType">Optional filter by action type</param>
    /// <param name="fromDate">Optional filter - entries after this date</param>
    /// <param name="toDate">Optional filter - entries before this date</param>
    [HttpGet("audit")]
    [ProducesResponseType(typeof(AuditHistoryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? actionType = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null) return validation.Error;

        try
        {
            var request = new AuditHistoryRequest
            {
                Page = page,
                PageSize = pageSize,
                ActionType = actionType,
                FromDate = fromDate,
                ToDate = toDate
            };

            var result = await _auditService.GetAuditHistoryAsync(
                validation.TenantId!.Value,
                request,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetAuditHistory failed");
            return StatusCode(500, AdminStoreConfigApiErrorResponse.InternalError(traceId));
        }
    }

    #region Header Validation

    private (Guid? TenantId, IActionResult? Error) ValidateTenantHeader(string traceId)
    {
        Guid tenantId;
        try
        {
            tenantId = _tenantContext.RequiredTenantId;
        }
        catch (InvalidOperationException)
        {
            return (null, BadRequest(AdminStoreConfigApiErrorResponse.TenantHeaderMissing(traceId)));
        }

        return (tenantId, null);
    }

    [HttpPost("theme/preview-link/{themeId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminStoreConfigApiErrorResponse), StatusCodes.Status400BadRequest)]
    public IActionResult CreateThemePreviewLink([FromRoute] string themeId)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null) return validation.Error;

        try
        {
            if (_themePreviewTokenService == null)
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    AdminStoreConfigApiErrorResponse.InternalError(traceId));
            }

            var preview = _themePreviewTokenService.Create(validation.TenantId!.Value, themeId);
            return Ok(new
            {
                previewToken = preview.Token,
                expiresAtUtc = preview.ExpiresAtUtc
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(AdminStoreConfigApiErrorResponse.InvalidConfig(ex.Message, traceId));
        }
    }

    private (uint? RowVersion, IActionResult? Error) RequireExpectedRowVersion(string traceId, string? requestRowVersion = null)
    {
        var candidate = !string.IsNullOrWhiteSpace(requestRowVersion)
            ? requestRowVersion
            : Request.Headers["If-Match"].ToString();

        if (string.IsNullOrWhiteSpace(candidate) || !TryParseRowVersion(candidate!, out var rowVersion))
        {
            RecordAdminConflict("store-config", string.IsNullOrWhiteSpace(candidate) ? "precondition_missing" : "precondition_invalid");
            return (null, StatusCode(StatusCodes.Status428PreconditionRequired, AdminStoreConfigApiErrorResponse.PreconditionRequired(traceId)));
        }

        return (rowVersion, null);
    }

    private void ApplyConcurrencyHeaders(string rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            return;
        }

        Response.Headers.ETag = $"\"{rowVersion}\"";
        Response.Headers["X-Resource-Version"] = rowVersion;
    }

    private void RecordAdminConflict(string resource, string reason)
    {
        HttpContext.RequestServices.GetService<CommerceReliabilityTelemetry>()
            ?.RecordAdminConflictDetected(resource, reason);
    }

    private static bool TryParseRowVersion(string raw, out uint rowVersion)
    {
        rowVersion = 0;

        var normalized = raw.Trim();
        if (normalized.StartsWith("W/\"", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[3..];
        }

        if (normalized.StartsWith('"') && normalized.EndsWith('"') && normalized.Length >= 2)
        {
            normalized = normalized[1..^1];
        }

        return uint.TryParse(normalized, out rowVersion);
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    #endregion
}
