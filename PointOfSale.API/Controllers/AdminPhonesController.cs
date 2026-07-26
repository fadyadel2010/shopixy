using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/phones")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminPhonesController : ControllerBase
{
    private readonly IPhoneRiskProfileService _phoneRiskProfileService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminPhonesController> _logger;

    public AdminPhonesController(
        IPhoneRiskProfileService phoneRiskProfileService,
        ITenantContext tenantContext,
        ILogger<AdminPhonesController> logger)
    {
        _phoneRiskProfileService = phoneRiskProfileService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(AdminPagedResult<AdminBlockedPhoneListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListBlockedPhones(
        [FromQuery] AdminBlockedPhoneQuery query,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validationError = query.Validate();
        if (validationError != null)
        {
            return BadRequest(new AdminApiErrorResponse("INVALID_QUERY", validationError, traceId));
        }

        try
        {
            var result = await _phoneRiskProfileService.ListBlockedAsync(
                _tenantContext.RequiredTenantId,
                query,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list blocked phones | TraceId: {TraceId}", traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpPost("block")]
    [ProducesResponseType(typeof(AdminPhoneRiskStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BlockPhone(
        [FromBody] AdminBlockPhoneRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return BadRequest(new AdminApiErrorResponse("PHONE_NUMBER_REQUIRED", "Phone number is required.", traceId));
        }

        try
        {
            var result = await _phoneRiskProfileService.BlockAsync(
                _tenantContext.RequiredTenantId,
                request.PhoneNumber,
                request.Reason,
                GetCurrentUserId(),
                cancellationToken);

            _logger.LogInformation(
                "Phone blocked | TenantId: {TenantId} | Phone: {PhoneNumber} | TraceId: {TraceId}",
                _tenantContext.RequiredTenantId,
                result.PhoneNumber,
                traceId);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new AdminApiErrorResponse("PHONE_NUMBER_INVALID", ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to block phone | TraceId: {TraceId}", traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpPost("unblock")]
    [ProducesResponseType(typeof(AdminPhoneRiskStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UnblockPhone(
        [FromBody] AdminUnblockPhoneRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return BadRequest(new AdminApiErrorResponse("PHONE_NUMBER_REQUIRED", "Phone number is required.", traceId));
        }

        try
        {
            var result = await _phoneRiskProfileService.UnblockAsync(
                _tenantContext.RequiredTenantId,
                request.PhoneNumber,
                cancellationToken);

            _logger.LogInformation(
                "Phone unblocked | TenantId: {TenantId} | Phone: {PhoneNumber} | TraceId: {TraceId}",
                _tenantContext.RequiredTenantId,
                result.PhoneNumber,
                traceId);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new AdminApiErrorResponse("PHONE_NUMBER_INVALID", ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unblock phone | TraceId: {TraceId}", traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpPost("report-spam")]
    [ProducesResponseType(typeof(AdminPhoneRiskStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReportSpam(
        [FromBody] AdminReportSpamPhoneRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return BadRequest(new AdminApiErrorResponse("PHONE_NUMBER_REQUIRED", "Phone number is required.", traceId));
        }

        if (request.OrderId == Guid.Empty)
        {
            return BadRequest(new AdminApiErrorResponse("ORDER_ID_REQUIRED", "OrderId is required.", traceId));
        }

        try
        {
            var result = await _phoneRiskProfileService.ReportSpamAsync(
                _tenantContext.RequiredTenantId,
                request.OrderId,
                request.PhoneNumber,
                GetCurrentUserId(),
                cancellationToken);

            _logger.LogInformation(
                "Phone reported as spam | TenantId: {TenantId} | Phone: {PhoneNumber} | GlobalSpamReports: {GlobalSpamReports} | TraceId: {TraceId}",
                _tenantContext.RequiredTenantId,
                result.PhoneNumber,
                result.GlobalSpamReports,
                traceId);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new AdminApiErrorResponse("PHONE_NUMBER_INVALID", ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report spam phone | TraceId: {TraceId}", traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminApiErrorResponse.InternalError(traceId));
        }
    }

    private Guid? GetCurrentUserId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(claimValue, out var userId) ? userId : null;
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;
}
