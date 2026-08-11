using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin Domains Controller — Domain Management for Storefront (DOMAIN-3.1-B + DOMAIN-3.1-C).
///
/// Route: /api/admin/storefront/domains
///
/// DOMAIN-3.1-B: Serves real backend DATA to all 9 Storefront Domain Management
/// screens (D01-D09). D03 (AddDomainWizardPage) is UI-only — no API needed.
///
/// DOMAIN-3.1-C: WRITE path — Create, Update, Archive, Restore, Delete (soft),
/// and PromotePrimary domain commands.
///
/// ARCHITECTURAL BOUNDARY (STRICT):
/// - READ endpoints: GET-only, delegated to IDomainReadService (DOMAIN-3.1-B).
/// - WRITE endpoints: POST/PUT/PATCH/DELETE, delegated to IDomainWriteService (DOMAIN-3.1-C).
/// - Tenant isolation is enforced via TenantResolutionMiddleware + ITenantContext.
/// - No business logic in this controller — only HTTP mapping and error handling.
/// - Architecture Law #18: No Direct DB Writes Outside Domain Repository.
/// - Architecture Law #19: Events via IEventPublisher Only (handled in write service).
///
/// Endpoints:
///   GET    /api/admin/storefront/domains                        (D04 - paginated list)
///   GET    /api/admin/storefront/domains/dashboard              (D01 - dashboard summary)
///   GET    /api/admin/storefront/domains/{id}                   (D02 - domain detail)
///   GET    /api/admin/storefront/domains/{id}/verification      (D05 - DNS verification)
///   GET    /api/admin/storefront/domains/{id}/ssl               (D06 - SSL management)
///   GET    /api/admin/storefront/domains/{id}/redirects         (D07 - redirects)
///   GET    /api/admin/storefront/domains/{id}/history           (D08 - audit history)
///   GET    /api/admin/storefront/domains/activity               (D09 - activity dashboard)
///
///   POST   /api/admin/storefront/domains                        (CreateDomain)
///   PATCH  /api/admin/storefront/domains/{id}                   (UpdateDomain)
///   PUT    /api/admin/storefront/domains/{id}/verification/start (StartDomainVerification)
///   PUT    /api/admin/storefront/domains/{id}/verification/verify (VerifyNow)
///   PUT    /api/admin/storefront/domains/{id}/verification/retry (RetryVerification)
///   PUT    /api/admin/storefront/domains/{id}/verification/token (RefreshVerificationToken)
///   PUT    /api/admin/storefront/domains/{id}/primary           (PromotePrimaryDomain)
///   PUT    /api/admin/storefront/domains/{id}/archive           (ArchiveDomain)
///   PUT    /api/admin/storefront/domains/{id}/restore           (RestoreDomain)
///   DELETE /api/admin/storefront/domains/{id}                   (DeleteDomain - soft)
/// </summary>
[ApiController]
[Route("api/admin/storefront/domains")]
[Authorize(Roles = "Owner,Admin")]
public class AdminDomainsController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly IDomainReadService _domainReadService;
    private readonly IDomainWriteService _domainWriteService;
    private readonly ICertificateAdminCommandService _certificateAdminCommandService;
    private readonly ILogger<AdminDomainsController> _logger;

    public AdminDomainsController(
        ITenantContext tenantContext,
        IDomainReadService domainReadService,
        IDomainWriteService domainWriteService,
        ICertificateAdminCommandService certificateAdminCommandService,
        ILogger<AdminDomainsController> logger)
    {
        _tenantContext = tenantContext;
        _domainReadService = domainReadService;
        _domainWriteService = domainWriteService;
        _certificateAdminCommandService = certificateAdminCommandService;
        _logger = logger;
    }

    // =========================================================================
    // READ ENDPOINTS (DOMAIN-3.1-B)
    // =========================================================================

    /// <summary>
    /// D04: Paginated, filtered, sorted list of all domains for the tenant.
    /// GET /api/admin/storefront/domains
    /// </summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(DomainListPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDomainList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? type = null,
        [FromQuery] string? state = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        var tenantId = validation.TenantId!.Value;

        DomainType? typeFilter = null;
        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<DomainType>(type, true, out var parsedType))
            typeFilter = parsedType;

        DomainLifecycleState? stateFilter = null;
        if (!string.IsNullOrWhiteSpace(state) && Enum.TryParse<DomainLifecycleState>(state, true, out var parsedState))
            stateFilter = parsedState;

        try
        {
            var result = await _domainReadService.GetDomainListAsync(
                tenantId, page, pageSize, search, typeFilter, stateFilter, sortBy, sortDir, cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetDomainList failed | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// D01: Dashboard summary — primary domain, counts, recent domains.
    /// GET /api/admin/storefront/domains/dashboard
    /// </summary>
    [HttpGet("dashboard")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(DomainDashboardSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDashboardSummary(
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        try
        {
            var result = await _domainReadService.GetDashboardSummaryAsync(
                validation.TenantId!.Value, cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetDashboardSummary failed | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// D02: Detailed view of a single domain.
    /// GET /api/admin/storefront/domains/{id}
    /// </summary>
    [HttpGet("{id:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(DomainDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDomainDetail(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        try
        {
            var result = await _domainReadService.GetDomainDetailAsync(
                validation.TenantId!.Value, id, cancellationToken);

            if (result == null)
            {
                return NotFound(new ApiErrorResponse(
                    "DOMAIN_NOT_FOUND",
                    "The requested domain was not found.",
                    traceId));
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetDomainDetail failed | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                id, validation.TenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// D05: DNS/HTTP verification details for a domain.
    /// GET /api/admin/storefront/domains/{id}/verification
    /// </summary>
    [HttpGet("{id:guid}/verification")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(DomainVerificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDomainVerification(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        try
        {
            var result = await _domainReadService.GetDomainVerificationAsync(
                validation.TenantId!.Value, id, cancellationToken);

            if (result == null)
            {
                return NotFound(new ApiErrorResponse(
                    "DOMAIN_NOT_FOUND",
                    "The requested domain was not found.",
                    traceId));
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetDomainVerification failed | DomainId: {DomainId} | TraceId: {TraceId}",
                id, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// D06: SSL certificate details for a domain.
    /// GET /api/admin/storefront/domains/{id}/ssl
    /// </summary>
    [HttpGet("{id:guid}/ssl")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(DomainSslDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDomainSsl(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        try
        {
            var result = await _domainReadService.GetDomainSslAsync(
                validation.TenantId!.Value, id, cancellationToken);

            if (result == null)
            {
                return NotFound(new ApiErrorResponse(
                    "DOMAIN_NOT_FOUND",
                    "The requested domain was not found.",
                    traceId));
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetDomainSsl failed | DomainId: {DomainId} | TraceId: {TraceId}",
                id, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// D07: Redirect rules for a domain (redirects FROM this domain).
    /// GET /api/admin/storefront/domains/{id}/redirects
    /// </summary>
    [HttpGet("{id:guid}/redirects")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(IEnumerable<DomainRedirectDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDomainRedirects(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        try
        {
            var result = await _domainReadService.GetDomainRedirectsAsync(
                validation.TenantId!.Value, id, cancellationToken);

            if (result == null)
            {
                return NotFound(new ApiErrorResponse(
                    "DOMAIN_NOT_FOUND",
                    "The requested domain was not found.",
                    traceId));
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetDomainRedirects failed | DomainId: {DomainId} | TraceId: {TraceId}",
                id, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// D08: Paginated audit history for a domain.
    /// GET /api/admin/storefront/domains/{id}/history
    /// </summary>
    [HttpGet("{id:guid}/history")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(DomainHistoryPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDomainHistory(
        [FromRoute] Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        try
        {
            var result = await _domainReadService.GetDomainHistoryAsync(
                validation.TenantId!.Value, id, page, pageSize, cancellationToken);

            // GetDomainHistoryAsync returns an empty page when the domain does not exist
            // (tenant-isolated 404). Check if domain exists.
            if (result.TotalCount == 0)
            {
                var domainExists = await _domainReadService.GetDomainDetailAsync(
                    validation.TenantId!.Value, id, cancellationToken);
                if (domainExists == null)
                {
                    return NotFound(new ApiErrorResponse(
                        "DOMAIN_NOT_FOUND",
                        "The requested domain was not found.",
                        traceId));
                }
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetDomainHistory failed | DomainId: {DomainId} | TraceId: {TraceId}",
                id, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// D09: Activity dashboard — recent background job activity across all domains.
    /// GET /api/admin/storefront/domains/activity
    /// </summary>
    [HttpGet("activity")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(DomainActivitySummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActivitySummary(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        try
        {
            var result = await _domainReadService.GetActivitySummaryAsync(
                validation.TenantId!.Value, Math.Clamp(limit, 1, 100), cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetActivitySummary failed | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    // =========================================================================
    // WRITE ENDPOINTS (DOMAIN-3.1-C)
    // Architecture Law: No controller may contain business rules.
    // All validation, state transitions, audit, and events are delegated
    // to IDomainWriteService. This controller only maps HTTP ↔ service.
    // =========================================================================

    /// <summary>
    /// CreateDomain command.
    /// POST /api/admin/storefront/domains
    ///
    /// Architecture Law #2: Domain Normalization Is Server-Authoritative.
    /// The server normalizes the hostname via IHostNormalizer — the client's
    /// casing/port/whitespace are stripped.
    ///
    /// Errors:
    ///   400 INVALID_HOST_FORMAT — hostname is blank or invalid
    ///   409 DOMAIN_CONFLICT — hostname already exists (global or per-tenant)
    ///   409 DUPLICATE_PRIMARY — tenant already has a Primary domain
    ///   400 INVALID_DOMAIN_TYPE — Redirect/Temporary cannot be created directly
    ///   400 RESERVED_HOST — hostname is reserved
    ///   401 UNAUTHORIZED — no tenant context
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(DomainMutationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateDomain(
        [FromBody] CreateDomainRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        var tenantId = validation.TenantId!.Value;
        var actorUserId = GetActorUserId();

        try
        {
            var response = await _domainWriteService.CreateDomainAsync(
                request, tenantId, actorUserId, traceId, cancellationToken);

            return CreatedAtAction(
                nameof(GetDomainDetail),
                new { id = response.DomainId },
                response);
        }
        catch (DomainOperationException ex) when (IsClientError(ex))
        {
            var statusCode = MapDomainOperationErrorCode(ex);
            return StatusCode(statusCode, new ApiErrorResponse(
                ex.ErrorCode.ToString(),
                ex.Message,
                traceId));
        }
        catch (DomainConflictException ex)
        {
            return Conflict(new ApiErrorResponse(
                "DOMAIN_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (DomainNotFoundException)
        {
            return NotFound(new ApiErrorResponse(
                "DOMAIN_NOT_FOUND",
                "The requested domain was not found.",
                traceId));
        }
        catch (DomainConcurrencyException ex)
        {
            return Conflict(new ApiErrorResponse(
                "CONCURRENCY_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CreateDomain failed | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    // =========================================================================
    // Command 2: UpdateDomain
    // =========================================================================

    /// <summary>
    /// UpdateDomain command (partial update).
    /// PATCH /api/admin/storefront/domains/{id}
    ///
    /// Supports updating: HostName, CanonicalPolicy, RedirectType,
    /// RedirectTargetDomainId. Optimistic concurrency via ExpectedVersion.
    ///
    /// Errors:
    ///   404 DOMAIN_NOT_FOUND
    ///   409 CONCURRENCY_CONFLICT — version mismatch
    ///   400 INVALID_HOST_FORMAT, RESERVED_HOST, DUPLICATE_HOSTNAME
    ///   400 INVALID_STATE — domain is Deleted or Archived
    ///   400 INVALID_REDIRECT_TARGET — target not found or cross-tenant loop
    ///   401 UNAUTHORIZED
    /// </summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(DomainVerificationCommandResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateDomain(
        [FromRoute] Guid id,
        [FromBody] UpdateDomainRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        var tenantId = validation.TenantId!.Value;
        var actorUserId = GetActorUserId();

        try
        {
            var response = await _domainWriteService.UpdateDomainAsync(
                id, request, tenantId, actorUserId, traceId, cancellationToken);

            return Ok(response);
        }
        catch (DomainOperationException ex) when (IsClientError(ex))
        {
            var statusCode = MapDomainOperationErrorCode(ex);
            return StatusCode(statusCode, new ApiErrorResponse(
                ex.ErrorCode.ToString(),
                ex.Message,
                traceId));
        }
        catch (DomainConflictException ex)
        {
            return Conflict(new ApiErrorResponse(
                "DOMAIN_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (DomainNotFoundException)
        {
            return NotFound(new ApiErrorResponse(
                "DOMAIN_NOT_FOUND",
                "The requested domain was not found.",
                traceId));
        }
        catch (DomainConcurrencyException ex)
        {
            return Conflict(new ApiErrorResponse(
                "CONCURRENCY_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "UpdateDomain failed | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                id, tenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    // =========================================================================
    // DOMAIN-4.3.1 Command: StartDomainVerification
    // =========================================================================

    /// <summary>
    /// StartDomainVerification command.
    /// PUT /api/admin/storefront/domains/{id}/verification/start
    ///
    /// Transitions a tenant-owned Draft domain into PendingVerification.
    /// This does not execute DNS/HTTP verification and does not publish a
    /// verification success event.
    /// </summary>
    [HttpPut("{id:guid}/verification/start")]
    [ProducesResponseType(typeof(DomainMutationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartDomainVerification(
        [FromRoute] Guid id,
        [FromBody] StartDomainVerificationRequest? request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        var tenantId = validation.TenantId!.Value;
        var actorUserId = GetActorUserId();

        try
        {
            var response = await _domainWriteService.StartDomainVerificationAsync(
                id, request, tenantId, actorUserId, traceId, cancellationToken);

            var instructions = await _domainReadService.GetDomainVerificationAsync(
                tenantId, id, cancellationToken);

            return Ok(MapMutationVerificationCommandResponse(
                "StartDomainVerification",
                response,
                instructions,
                traceId,
                outcome: "Accepted",
                nextAction: "AddVerificationRecord"));
        }
        catch (DomainOperationException ex) when (IsClientError(ex))
        {
            var statusCode = MapDomainOperationErrorCode(ex);
            return StatusCode(statusCode, new ApiErrorResponse(
                ex.ErrorCode.ToString(),
                ex.Message,
                traceId));
        }
        catch (DomainNotFoundException)
        {
            return NotFound(new ApiErrorResponse(
                "DOMAIN_NOT_FOUND",
                "The requested domain was not found.",
                traceId));
        }
        catch (DomainConcurrencyException ex)
        {
            return Conflict(new ApiErrorResponse(
                "CONCURRENCY_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (DomainConflictException ex)
        {
            return Conflict(new ApiErrorResponse(
                "DOMAIN_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "StartDomainVerification failed | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                id, tenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// VerifyNow command.
    /// PUT /api/admin/storefront/domains/{id}/verification/verify
    ///
    /// Executes exactly one verification attempt through the existing engine.
    /// </summary>
    [HttpPut("{id:guid}/verification/verify")]
    [ProducesResponseType(typeof(DomainVerificationCommandResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DomainVerificationCommandResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(DomainVerificationCommandResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(DomainVerificationCommandResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyNow(
        [FromRoute] Guid id,
        [FromBody] VerifyDomainNowRequest? request,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteVerificationCommandAsync(
            id,
            "VerifyNow",
            (tenantId, actorUserId, traceId, ct) =>
                _domainWriteService.VerifyDomainNowAsync(id, request, tenantId, actorUserId, traceId, ct),
            cancellationToken);
    }

    /// <summary>
    /// RetryVerification command.
    /// PUT /api/admin/storefront/domains/{id}/verification/retry
    ///
    /// Retry policy and attempt limits remain owned by the verification engine.
    /// </summary>
    [HttpPut("{id:guid}/verification/retry")]
    [ProducesResponseType(typeof(DomainVerificationCommandResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DomainVerificationCommandResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(DomainVerificationCommandResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(DomainVerificationCommandResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RetryVerification(
        [FromRoute] Guid id,
        [FromBody] RetryDomainVerificationRequest? request,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteVerificationCommandAsync(
            id,
            "RetryVerification",
            (tenantId, actorUserId, traceId, ct) =>
                _domainWriteService.RetryDomainVerificationAsync(id, request, tenantId, actorUserId, traceId, ct),
            cancellationToken);
    }

    /// <summary>
    /// RefreshVerificationToken command.
    /// PUT /api/admin/storefront/domains/{id}/verification/token
    ///
    /// Refresh is allowed only before ownership has been verified.
    /// </summary>
    [HttpPut("{id:guid}/verification/token")]
    [ProducesResponseType(typeof(DomainVerificationCommandResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RefreshVerificationToken(
        [FromRoute] Guid id,
        [FromBody] RefreshDomainVerificationTokenRequest? request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        var tenantId = validation.TenantId!.Value;
        var actorUserId = GetActorUserId();

        try
        {
            var response = await _domainWriteService.RefreshDomainVerificationTokenAsync(
                id, request, tenantId, actorUserId, traceId, cancellationToken);

            var instructions = await _domainReadService.GetDomainVerificationAsync(
                tenantId, id, cancellationToken);

            return Ok(MapMutationVerificationCommandResponse(
                "RefreshVerificationToken",
                response,
                instructions,
                traceId,
                outcome: "TokenRefreshed",
                nextAction: "UpdateVerificationRecord"));
        }
        catch (DomainOperationException ex) when (IsClientError(ex))
        {
            var statusCode = MapDomainOperationErrorCode(ex);
            SetRetryAfterHeader(ex.RetryAfterUtc);
            return StatusCode(statusCode, new ApiErrorResponse(
                ex.ErrorCode.ToString(),
                ex.Message,
                traceId));
        }
        catch (DomainNotFoundException)
        {
            return NotFound(new ApiErrorResponse(
                "DOMAIN_NOT_FOUND",
                "The requested domain was not found.",
                traceId));
        }
        catch (DomainConcurrencyException ex)
        {
            return Conflict(new ApiErrorResponse(
                "CONCURRENCY_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (DomainConflictException ex)
        {
            return Conflict(new ApiErrorResponse(
                "DOMAIN_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RefreshVerificationToken failed | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                id, tenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// DOMAIN-5.4 Certificate provisioning admission command.
    /// POST /api/admin/storefront/domains/{id}/ssl/provision
    /// </summary>
    [HttpPost("{id:guid}/ssl/provision")]
    [ProducesResponseType(typeof(CertificateProvisioningResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ProvisionSsl(
        [FromRoute] Guid id,
        [FromBody] CertificateProvisioningRequest? request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        var tenantId = validation.TenantId!.Value;
        var actorUserId = GetActorUserId();

        try
        {
            var response = await _certificateAdminCommandService.ProvisionAsync(
                id, request, tenantId, actorUserId, traceId, cancellationToken);

            return Accepted(response);
        }
        catch (DomainOperationException ex) when (IsClientError(ex))
        {
            var statusCode = MapDomainOperationErrorCode(ex);
            return StatusCode(statusCode, new ApiErrorResponse(
                ex.ErrorCode.ToString(),
                ex.Message,
                traceId));
        }
        catch (DomainNotFoundException)
        {
            return NotFound(new ApiErrorResponse(
                "DOMAIN_NOT_FOUND",
                "The requested domain was not found.",
                traceId));
        }
        catch (DomainConcurrencyException ex)
        {
            return Conflict(new ApiErrorResponse(
                "CONCURRENCY_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (DomainConflictException ex)
        {
            return Conflict(new ApiErrorResponse(
                "DOMAIN_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ProvisionSsl failed | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                id, tenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// DOMAIN-5.5 certificate edge activation command.
    /// POST /api/admin/storefront/domains/{id}/ssl/activate
    /// </summary>
    [HttpPost("{id:guid}/ssl/activate")]
    [ProducesResponseType(typeof(CertificateActivationResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ActivateSsl(
        [FromRoute] Guid id,
        [FromBody] CertificateActivationRequest? request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        var tenantId = validation.TenantId!.Value;
        var actorUserId = GetActorUserId();

        try
        {
            var response = await _certificateAdminCommandService.ActivateAsync(
                id, request, tenantId, actorUserId, traceId, cancellationToken);

            return Accepted(response);
        }
        catch (DomainOperationException ex) when (IsClientError(ex))
        {
            var statusCode = MapDomainOperationErrorCode(ex);
            return StatusCode(statusCode, new ApiErrorResponse(
                ex.ErrorCode.ToString(),
                ex.Message,
                traceId));
        }
        catch (DomainNotFoundException)
        {
            return NotFound(new ApiErrorResponse(
                "DOMAIN_NOT_FOUND",
                "The requested domain or certificate was not found.",
                traceId));
        }
        catch (DomainConflictException ex)
        {
            return Conflict(new ApiErrorResponse(
                "DOMAIN_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ActivateSsl failed | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                id, tenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// DOMAIN-5.8 canonical safe certificate metadata read.
    /// GET /api/admin/storefront/domains/{id}/ssl/certificate
    /// </summary>
    [HttpGet("{id:guid}/ssl/certificate")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(DomainCertificateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSslCertificate(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        try
        {
            var response = await _certificateAdminCommandService.GetCertificateAsync(
                id, validation.TenantId!.Value, cancellationToken);

            return response is null
                ? NotFound(new ApiErrorResponse("DOMAIN_NOT_FOUND", "The requested domain was not found.", traceId))
                : Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetSslCertificate failed | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                id, validation.TenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// DOMAIN-5.8 canonical certificate operation list.
    /// GET /api/admin/storefront/domains/{id}/ssl/operations
    /// </summary>
    [HttpGet("{id:guid}/ssl/operations")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(CertificateOperationPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSslOperations(
        [FromRoute] Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        try
        {
            var response = await _certificateAdminCommandService.GetOperationsAsync(
                id, validation.TenantId!.Value, page, pageSize, cancellationToken);

            return response is null
                ? NotFound(new ApiErrorResponse("DOMAIN_NOT_FOUND", "The requested domain was not found.", traceId))
                : Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetSslOperations failed | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                id, validation.TenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// DOMAIN-5.8 canonical certificate operation read.
    /// GET /api/admin/storefront/domains/{id}/ssl/operations/{operationId}
    /// </summary>
    [HttpGet("{id:guid}/ssl/operations/{operationId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(CertificateOperationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSslOperation(
        [FromRoute] Guid id,
        [FromRoute] Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        try
        {
            var response = await _certificateAdminCommandService.GetOperationAsync(
                id, operationId, validation.TenantId!.Value, cancellationToken);

            return response is null
                ? NotFound(new ApiErrorResponse("CERTIFICATE_OPERATION_NOT_FOUND", "The requested certificate operation was not found.", traceId))
                : Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetSslOperation failed | DomainId: {DomainId} | OperationId: {OperationId} | TenantId: {TenantId} | TraceId: {TraceId}",
                id, operationId, validation.TenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// DOMAIN-5.8 manual certificate renewal/reconciliation command.
    /// POST /api/admin/storefront/domains/{id}/ssl/renew
    /// </summary>
    [HttpPost("{id:guid}/ssl/renew")]
    [ProducesResponseType(typeof(CertificateCommandResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public Task<IActionResult> RenewSsl(
        [FromRoute] Guid id,
        [FromBody] CertificateRenewalCommandRequest? request,
        CancellationToken cancellationToken = default) =>
        ExecuteCertificateCommandAsync(
            id,
            "RenewSsl",
            (tenantId, actorUserId, traceId, ct) => _certificateAdminCommandService.RenewAsync(
                id, request, tenantId, actorUserId, traceId, ct),
            cancellationToken);

    /// <summary>
    /// DOMAIN-5.8 certificate revocation command.
    /// POST /api/admin/storefront/domains/{id}/ssl/revoke
    /// </summary>
    [HttpPost("{id:guid}/ssl/revoke")]
    [ProducesResponseType(typeof(CertificateCommandResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> RevokeSsl(
        [FromRoute] Guid id,
        [FromBody] CertificateRevocationCommandRequest? request,
        CancellationToken cancellationToken = default) =>
        ExecuteCertificateCommandAsync(
            id,
            "RevokeSsl",
            (tenantId, actorUserId, traceId, ct) => _certificateAdminCommandService.RevokeAsync(
                id, request, tenantId, actorUserId, traceId, ct),
            cancellationToken);

    /// <summary>
    /// DOMAIN-5.8 retry failed certificate operation command.
    /// POST /api/admin/storefront/domains/{id}/ssl/retry
    /// </summary>
    [HttpPost("{id:guid}/ssl/retry")]
    [ProducesResponseType(typeof(CertificateCommandResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public Task<IActionResult> RetrySsl(
        [FromRoute] Guid id,
        [FromBody] CertificateRetryCommandRequest? request,
        CancellationToken cancellationToken = default) =>
        ExecuteCertificateCommandAsync(
            id,
            "RetrySsl",
            (tenantId, actorUserId, traceId, ct) => _certificateAdminCommandService.RetryAsync(
                id, request, tenantId, actorUserId, traceId, ct),
            cancellationToken);

    /// <summary>
    /// DOMAIN-5.8 refresh/reconcile certificate state command.
    /// POST /api/admin/storefront/domains/{id}/ssl/refresh
    /// </summary>
    [HttpPost("{id:guid}/ssl/refresh")]
    [ProducesResponseType(typeof(CertificateCommandResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public Task<IActionResult> RefreshSsl(
        [FromRoute] Guid id,
        [FromBody] CertificateRefreshCommandRequest? request,
        CancellationToken cancellationToken = default) =>
        ExecuteCertificateCommandAsync(
            id,
            "RefreshSsl",
            (tenantId, actorUserId, traceId, ct) => _certificateAdminCommandService.RefreshAsync(
                id, request, tenantId, actorUserId, traceId, ct),
            cancellationToken);

    // =========================================================================
    // Command 3: ArchiveDomain
    // =========================================================================

    /// <summary>
    /// ArchiveDomain command.
    /// PUT /api/admin/storefront/domains/{id}/archive
    ///
    /// Validates: domain not Primary, no redirect dependents, not already archived.
    /// Errors:
    ///   404 DOMAIN_NOT_FOUND
    ///   400 CANNOT_MODIFY_PRIMARY — domain is the Primary
    ///   400 HAS_REDIRECT_DEPENDENTS
    ///   400 DOMAIN_ARCHIVED — already archived
    ///   400 DOMAIN_DELETED — already deleted
    /// </summary>
    [HttpPut("{id:guid}/archive")]
    [ProducesResponseType(typeof(DomainMutationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ArchiveDomain(
        [FromRoute] Guid id,
        [FromBody] ArchiveDomainRequest? request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        var tenantId = validation.TenantId!.Value;
        var actorUserId = GetActorUserId();

        try
        {
            var response = await _domainWriteService.ArchiveDomainAsync(
                id, request, tenantId, actorUserId, traceId, cancellationToken);

            return Ok(response);
        }
        catch (DomainOperationException ex) when (IsClientError(ex))
        {
            var statusCode = MapDomainOperationErrorCode(ex);
            return StatusCode(statusCode, new ApiErrorResponse(
                ex.ErrorCode.ToString(),
                ex.Message,
                traceId));
        }
        catch (DomainNotFoundException)
        {
            return NotFound(new ApiErrorResponse(
                "DOMAIN_NOT_FOUND",
                "The requested domain was not found.",
                traceId));
        }
        catch (DomainConcurrencyException ex)
        {
            return Conflict(new ApiErrorResponse(
                "CONCURRENCY_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (DomainConflictException ex)
        {
            return Conflict(new ApiErrorResponse(
                "DOMAIN_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ArchiveDomain failed | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                id, tenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    // =========================================================================
    // Command 4: RestoreDomain
    // =========================================================================

    /// <summary>
    /// RestoreDomain command.
    /// PUT /api/admin/storefront/domains/{id}/restore
    ///
    /// Restores a domain from Deleted, Archived, or Disabled state.
    /// 90-day retention window enforced for Deleted domains.
    /// Errors:
    ///   404 DOMAIN_NOT_FOUND
    ///   400 INVALID_STATE_TRANSITION — domain is in an active state
    ///   400 RETENTION_EXPIRED — 90-day window for Deleted domains
    ///   400 DUPLICATE_HOSTNAME — another domain now uses the hostname
    /// </summary>
    [HttpPut("{id:guid}/restore")]
    [ProducesResponseType(typeof(DomainMutationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RestoreDomain(
        [FromRoute] Guid id,
        [FromBody] RestoreDomainRequest? request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        var tenantId = validation.TenantId!.Value;
        var actorUserId = GetActorUserId();

        try
        {
            var response = await _domainWriteService.RestoreDomainAsync(
                id, request, tenantId, actorUserId, traceId, cancellationToken);

            return Ok(response);
        }
        catch (DomainOperationException ex) when (IsClientError(ex))
        {
            var statusCode = MapDomainOperationErrorCode(ex);
            return StatusCode(statusCode, new ApiErrorResponse(
                ex.ErrorCode.ToString(),
                ex.Message,
                traceId));
        }
        catch (DomainNotFoundException)
        {
            return NotFound(new ApiErrorResponse(
                "DOMAIN_NOT_FOUND",
                "The requested domain was not found.",
                traceId));
        }
        catch (DomainConcurrencyException ex)
        {
            return Conflict(new ApiErrorResponse(
                "CONCURRENCY_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (DomainConflictException ex)
        {
            return Conflict(new ApiErrorResponse(
                "DOMAIN_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RestoreDomain failed | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                id, tenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    // =========================================================================
    // Command 5: DeleteDomain (soft-delete ONLY)
    // =========================================================================

    /// <summary>
    /// DeleteDomain command — SOFT DELETE ONLY.
    /// DELETE /api/admin/storefront/domains/{id}
    ///
    /// Architecture Law #3: Soft Delete only. Never physical delete.
    ///
    /// Validates: domain not Primary, no active SSL, no redirect dependents.
    /// Errors:
    ///   404 DOMAIN_NOT_FOUND
    ///   400 CANNOT_MODIFY_PRIMARY — domain is the Primary
    ///   400 CANNOT_DELETE_WITH_SSL — SSL certificate is issued/active
    ///   400 HAS_REDIRECT_DEPENDENTS
    ///   400 DOMAIN_DELETED — already deleted
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(DomainMutationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDomain(
        [FromRoute] Guid id,
        [FromBody] DeleteDomainRequest? request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        var tenantId = validation.TenantId!.Value;
        var actorUserId = GetActorUserId();

        try
        {
            var response = await _domainWriteService.DeleteDomainAsync(
                id, request, tenantId, actorUserId, traceId, cancellationToken);

            return Ok(response);
        }
        catch (DomainOperationException ex) when (IsClientError(ex))
        {
            var statusCode = MapDomainOperationErrorCode(ex);
            return StatusCode(statusCode, new ApiErrorResponse(
                ex.ErrorCode.ToString(),
                ex.Message,
                traceId));
        }
        catch (DomainNotFoundException)
        {
            return NotFound(new ApiErrorResponse(
                "DOMAIN_NOT_FOUND",
                "The requested domain was not found.",
                traceId));
        }
        catch (DomainConcurrencyException ex)
        {
            return Conflict(new ApiErrorResponse(
                "CONCURRENCY_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (DomainConflictException ex)
        {
            return Conflict(new ApiErrorResponse(
                "DOMAIN_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DeleteDomain failed | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                id, tenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    // =========================================================================
    // Command 6: PromotePrimaryDomain
    // =========================================================================

    /// <summary>
    /// PromotePrimaryDomain command — atomically promotes a domain to Primary
    /// and demotes the current primary (if any).
    /// PUT /api/admin/storefront/domains/{id}/primary
    ///
    /// Architecture Law #2: Exactly one Primary Domain per tenant.
    /// Transaction strategy: Serializable isolation with optimistic concurrency
    /// backstop. The filtered unique index UQ_Domains_TenantId_Primary
    /// provides the final SQL-level guarantee.
    ///
    /// Errors:
    ///   404 DOMAIN_NOT_FOUND
    ///   400 INVALID_DOMAIN_TYPE_FOR_PRIMARY — not Verified/SslActive
    ///   400 CANNOT_MODIFY_PRIMARY — already primary
    ///   409 CONCURRENCY_CONFLICT — version mismatch
    ///   409 DUPLICATE_PRIMARY — concurrent promotion (race condition)
    /// </summary>
    [HttpPut("{id:guid}/primary")]
    [ProducesResponseType(typeof(DomainMutationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PromotePrimaryDomain(
        [FromRoute] Guid id,
        [FromBody] PromotePrimaryDomainRequest? request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        var tenantId = validation.TenantId!.Value;
        var actorUserId = GetActorUserId();

        try
        {
            var response = await _domainWriteService.PromotePrimaryDomainAsync(
                id, request, tenantId, actorUserId, traceId, cancellationToken);

            return Ok(response);
        }
        catch (DomainOperationException ex) when (IsClientError(ex))
        {
            var statusCode = MapDomainOperationErrorCode(ex);
            return StatusCode(statusCode, new ApiErrorResponse(
                ex.ErrorCode.ToString(),
                ex.Message,
                traceId));
        }
        catch (DomainConflictException ex)
        {
            return Conflict(new ApiErrorResponse(
                "DUPLICATE_PRIMARY",
                ex.Message,
                traceId));
        }
        catch (DomainNotFoundException)
        {
            return NotFound(new ApiErrorResponse(
                "DOMAIN_NOT_FOUND",
                "The requested domain was not found.",
                traceId));
        }
        catch (DomainConcurrencyException ex)
        {
            return Conflict(new ApiErrorResponse(
                "CONCURRENCY_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "PromotePrimaryDomain failed | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                id, tenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<IActionResult> ExecuteVerificationCommandAsync(
        Guid domainId,
        string operation,
        Func<Guid, Guid?, string, CancellationToken, Task<VerificationResult>> execute,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        var tenantId = validation.TenantId!.Value;
        var actorUserId = GetActorUserId();

        try
        {
            var result = await execute(tenantId, actorUserId, traceId, cancellationToken);
            var instructions = await _domainReadService.GetDomainVerificationAsync(
                tenantId, domainId, cancellationToken);
            var response = MapVerificationResultResponse(operation, result, instructions, traceId);
            SetRetryAfterHeader(result.RetryAfterUtc);
            return StatusCode(MapVerificationResultStatusCode(result), response);
        }
        catch (DomainOperationException ex) when (IsClientError(ex))
        {
            var statusCode = MapDomainOperationErrorCode(ex);
            SetRetryAfterHeader(ex.RetryAfterUtc);
            return StatusCode(statusCode, new ApiErrorResponse(
                ex.ErrorCode.ToString(),
                ex.Message,
                traceId));
        }
        catch (DomainNotFoundException)
        {
            return NotFound(new ApiErrorResponse(
                "DOMAIN_NOT_FOUND",
                "The requested domain was not found.",
                traceId));
        }
        catch (DomainConflictException ex)
        {
            return Conflict(new ApiErrorResponse(
                "DOMAIN_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (DomainConcurrencyException ex)
        {
            return Conflict(new ApiErrorResponse(
                "CONCURRENCY_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "{Operation} failed | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                operation, domainId, tenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    private async Task<IActionResult> ExecuteCertificateCommandAsync(
        Guid domainId,
        string operation,
        Func<Guid, Guid?, string, CancellationToken, Task<CertificateCommandResponse>> execute,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenant(traceId);
        if (validation.Error != null)
            return validation.Error;

        var tenantId = validation.TenantId!.Value;
        var actorUserId = GetActorUserId();

        try
        {
            var response = await execute(tenantId, actorUserId, traceId, cancellationToken);
            return Accepted(response);
        }
        catch (CertificateCommandRateLimitException ex)
        {
            if (ex.RetryAfterUtc.HasValue)
            {
                var retryAfterSeconds = Math.Max(
                    1,
                    (int)Math.Ceiling((ex.RetryAfterUtc.Value - DateTime.UtcNow).TotalSeconds));
                Response.Headers["Retry-After"] = retryAfterSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            }

            return StatusCode(StatusCodes.Status429TooManyRequests, new ApiErrorResponse(
                "RATE_LIMITED",
                ex.Message,
                traceId)
            {
                Details = new
                {
                    ex.OperationId,
                    ex.RetryAfterUtc,
                    Retryable = true,
                }
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiErrorResponse(
                "INVALID_CERTIFICATE_COMMAND",
                ex.Message,
                traceId));
        }
        catch (DomainOperationException ex) when (IsClientError(ex))
        {
            var statusCode = MapDomainOperationErrorCode(ex);
            return StatusCode(statusCode, new ApiErrorResponse(
                ex.ErrorCode.ToString(),
                ex.Message,
                traceId));
        }
        catch (DomainNotFoundException)
        {
            return NotFound(new ApiErrorResponse(
                "DOMAIN_NOT_FOUND",
                "The requested domain, certificate, or operation was not found.",
                traceId));
        }
        catch (DomainConflictException ex)
        {
            return Conflict(new ApiErrorResponse(
                "DOMAIN_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (DomainConcurrencyException ex)
        {
            return Conflict(new ApiErrorResponse(
                "CONCURRENCY_CONFLICT",
                ex.Message,
                traceId));
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex,
                "{Operation} lost its certificate operation claim | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                operation, domainId, tenantId, traceId);
            return Conflict(new ApiErrorResponse(
                "CONCURRENCY_CONFLICT",
                "The domain changed concurrently. Refresh its state and retry the command.",
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "{Operation} failed | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
                operation, domainId, tenantId, traceId);
            return StatusCode(500, ApiErrorResponse.InternalError(traceId));
        }
    }

    private static DomainVerificationCommandResponse MapMutationVerificationCommandResponse(
        string operation,
        DomainMutationResponse mutation,
        DomainVerificationDto? instructions,
        string traceId,
        string outcome,
        string nextAction)
    {
        return new DomainVerificationCommandResponse
        {
            DomainId = mutation.DomainId,
            Operation = operation,
            Status = instructions?.Status ?? VerificationStatus.Pending,
            LifecycleState = mutation.LifecycleState,
            Method = instructions?.Method,
            Outcome = outcome,
            FailureReason = null,
            RetryEligible = true,
            AttemptNumber = null,
            NextAction = nextAction,
            Instructions = instructions,
            CorrelationId = traceId,
        };
    }

    private static DomainVerificationCommandResponse MapVerificationResultResponse(
        string operation,
        VerificationResult result,
        DomainVerificationDto? instructions,
        string traceId)
    {
        var outcome = result.Status == VerificationStatus.Success
            ? "Verified"
            : IsInfrastructureFailure(result.FailureReason)
                ? "InfrastructureFailure"
                : "VerificationFailed";

        return new DomainVerificationCommandResponse
        {
            DomainId = result.DomainId,
            Operation = operation,
            Status = result.Status,
            LifecycleState = result.LifecycleState,
            Method = result.Method ?? instructions?.Method,
            Outcome = outcome,
            FailureReason = result.FailureReason,
            RetryEligible = result.RetryEligible,
            RetryAfterUtc = result.RetryAfterUtc,
            AttemptNumber = result.AttemptNumber,
            NextAction = GetNextVerificationAction(result),
            Instructions = instructions,
            CorrelationId = string.IsNullOrWhiteSpace(result.CorrelationId)
                ? traceId
                : result.CorrelationId,
        };
    }

    private static int MapVerificationResultStatusCode(VerificationResult result)
    {
        if (result.Status == VerificationStatus.Success)
        {
            return StatusCodes.Status200OK;
        }

        return result.FailureReason switch
        {
            DomainVerificationFailureReasons.RateLimited => StatusCodes.Status429TooManyRequests,
            DomainVerificationFailureReasons.LifecycleViolation => StatusCodes.Status409Conflict,
            DomainVerificationFailureReasons.ExpiredToken => StatusCodes.Status409Conflict,
            DomainVerificationFailureReasons.UnsupportedMethod => StatusCodes.Status409Conflict,
            _ when IsInfrastructureFailure(result.FailureReason) => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status422UnprocessableEntity,
        };
    }

    private void SetRetryAfterHeader(DateTime? retryAfterUtc)
    {
        if (!retryAfterUtc.HasValue)
            return;

        var retryAfterSeconds = Math.Max(
            1,
            (int)Math.Ceiling((retryAfterUtc.Value - DateTime.UtcNow).TotalSeconds));
        Response.Headers["Retry-After"] = retryAfterSeconds.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsInfrastructureFailure(string? failureReason)
    {
        return failureReason is
            DomainVerificationFailureReasons.Timeout or
            DomainVerificationFailureReasons.ResolverFailure or
            DomainVerificationFailureReasons.NetworkFailure;
    }

    private static string GetNextVerificationAction(VerificationResult result)
    {
        if (result.Status == VerificationStatus.Success)
        {
            return "NoActionRequired";
        }

        return result.FailureReason switch
        {
            DomainVerificationFailureReasons.RateLimited => "WaitBeforeRetry",
            DomainVerificationFailureReasons.ExpiredToken => "RefreshVerificationToken",
            DomainVerificationFailureReasons.LifecycleViolation => "RefreshDomainStatus",
            DomainVerificationFailureReasons.TokenMismatch => "UpdateVerificationRecord",
            DomainVerificationFailureReasons.RecordMissing => "AddVerificationRecord",
            _ when IsInfrastructureFailure(result.FailureReason) => "RetryLater",
            _ => result.RetryEligible ? "RetryVerification" : "ContactSupport",
        };
    }

    private (Guid? TenantId, IActionResult? Error) ValidateTenant(string traceId)
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;
            if (tenantId == Guid.Empty)
            {
                return (null, BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId)));
            }
            return (tenantId, null);
        }
        catch (InvalidOperationException)
        {
            return (null, Unauthorized(new ApiErrorResponse(
                "UNAUTHORIZED",
                "Authentication is required to access domain management.",
                traceId)));
        }
    }

    private Guid? GetActorUserId()
    {
        if (User?.Identity?.IsAuthenticated != true)
            return null;

        var userIdStr = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("UserId")?.Value;

        return Guid.TryParse(userIdStr, out var userId) ? userId : null;
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private static bool IsClientError(DomainOperationException ex)
    {
        return true; // All DomainOperationException instances are client errors (4xx)
    }

    private static int MapDomainOperationErrorCode(DomainOperationException ex)
    {
        return ex.ErrorCode switch
        {
            DomainOperationErrorCode.InvalidHostFormat => StatusCodes.Status400BadRequest,
            DomainOperationErrorCode.ReservedHost => StatusCodes.Status400BadRequest,
            DomainOperationErrorCode.DuplicateHostname => StatusCodes.Status409Conflict,
            DomainOperationErrorCode.InvalidDomainType => StatusCodes.Status400BadRequest,
            DomainOperationErrorCode.InvalidRedirectTarget => StatusCodes.Status400BadRequest,
            DomainOperationErrorCode.RedirectLoop => StatusCodes.Status400BadRequest,
            DomainOperationErrorCode.HasRedirectDependents => StatusCodes.Status400BadRequest,
            DomainOperationErrorCode.CannotDeleteWithSsl => StatusCodes.Status400BadRequest,
            DomainOperationErrorCode.AlreadyPrimary => StatusCodes.Status409Conflict,
            DomainOperationErrorCode.NotVerifiedOrSslActive => StatusCodes.Status400BadRequest,
            DomainOperationErrorCode.TenantRequired => StatusCodes.Status401Unauthorized,
            DomainOperationErrorCode.InvalidStateTransition => StatusCodes.Status400BadRequest,
            DomainOperationErrorCode.RetentionExpired => StatusCodes.Status400BadRequest,
            DomainOperationErrorCode.RateLimitExceeded => StatusCodes.Status429TooManyRequests,
            DomainOperationErrorCode.ExpectedVersionRequired => StatusCodes.Status428PreconditionRequired,
            _ => StatusCodes.Status400BadRequest,
        };
    }
}
