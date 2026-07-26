using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Controllers;

[ApiController]
[Authorize(Roles = "Owner,Admin")]
[Route("api/admin/footer")]
public sealed class AdminStorefrontFooterController : ControllerBase
{
    private readonly IAdminStorefrontFooterService _footerService;
    private readonly ITenantContext _tenantContext;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ILogger<AdminStorefrontFooterController> _logger;

    public AdminStorefrontFooterController(
        IAdminStorefrontFooterService footerService,
        ITenantContext tenantContext,
        CommerceReliabilityTelemetry telemetry,
        ILogger<AdminStorefrontFooterController> logger)
    {
        _footerService = footerService;
        _tenantContext = tenantContext;
        _telemetry = telemetry;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(AdminStorefrontFooterConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetConfiguration(CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var tenantId = _tenantContext.RequiredTenantId;

        try
        {
            var response = await _footerService.GetConfigurationAsync(tenantId, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get storefront footer configuration failed | TenantId: {TenantId}", tenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpPut]
    [ProducesResponseType(typeof(AdminStorefrontFooterConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReplaceConfiguration(
        [FromBody] AdminStorefrontFooterConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var tenantId = _tenantContext.RequiredTenantId;

        if (!request.ExpectedUpdatedAtUtc.HasValue)
        {
            return StatusCode(
                StatusCodes.Status428PreconditionRequired,
                new AdminCmsApiErrorResponse(
                    "PRECONDITION_REQUIRED",
                    "The current footer version is required.",
                    traceId));
        }

        try
        {
            var response = await _footerService.ReplaceConfigurationAsync(tenantId, request, cancellationToken);
            return Ok(response);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "Storefront footer configuration concurrency conflict | TenantId: {TenantId}", tenantId);
            _telemetry.RecordCmsConflictDetected("storefront_footer");
            
            object? latestVersion = null;
            try
            {
                latestVersion = await _footerService.GetConfigurationAsync(tenantId, cancellationToken);
            }
            catch (Exception getEx)
            {
                _logger.LogWarning(getEx, "Failed to fetch latest footer configuration for conflict response | TenantId: {TenantId}", tenantId);
            }
            
            return Conflict(new AdminCmsApiErrorResponse(
                "CONCURRENCY_CONFLICT",
                "Footer configuration was updated by another admin. Reload the latest version before saving again.",
                traceId)
            {
                LatestVersion = latestVersion,
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(AdminCmsApiErrorResponse.ValidationError(ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Replace storefront footer configuration failed | TenantId: {TenantId}", tenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

}
