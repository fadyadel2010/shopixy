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
    private readonly IFooterAuthoringService _footerService;
    private readonly ITenantContext _tenantContext;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ILogger<AdminStorefrontFooterController> _logger;

    public AdminStorefrontFooterController(
        IFooterAuthoringService footerService,
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
            var response = await _footerService.GetConfigurationAsync(
                FooterAuthoringScope.Tenant,
                tenantId,
                cancellationToken);
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
        [FromBody] AdminStorefrontFooterConfigurationRequest? request,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var tenantId = _tenantContext.RequiredTenantId;

        if (request == null)
        {
            return BadRequest(new AdminCmsApiErrorResponse(
                "FOOTER_REQUEST_INVALID",
                "Request body is required.",
                traceId));
        }

        try
        {
            var response = await _footerService.SaveConfigurationAsync(
                FooterAuthoringScope.Tenant,
                tenantId,
                request,
                cancellationToken);
            return Ok(response);
        }
        catch (FooterAuthoringException ex)
        {
            if (ex.StatusCode == StatusCodes.Status409Conflict && ex.Code == "FOOTER_CONCURRENCY_CONFLICT")
            {
                _logger.LogWarning(ex, "Storefront footer configuration concurrency conflict | TenantId: {TenantId}", tenantId);
                _telemetry.RecordCmsConflictDetected("storefront_footer");
            }

            object? latestVersion = null;
            if (ex.Code == "FOOTER_CONCURRENCY_CONFLICT")
            {
                try
                {
                    latestVersion = await _footerService.GetConfigurationAsync(
                        FooterAuthoringScope.Tenant,
                        tenantId,
                        cancellationToken);
                }
                catch (Exception getEx)
                {
                    _logger.LogWarning(getEx, "Failed to fetch latest footer configuration for conflict response | TenantId: {TenantId}", tenantId);
                }
            }

            return StatusCode(ex.StatusCode, new AdminCmsApiErrorResponse(ex.Code, ex.Message, traceId)
            {
                LatestVersion = latestVersion,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Replace storefront footer configuration failed | TenantId: {TenantId}", tenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

}
