using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

[ApiController]
[Authorize(Roles = "Owner,Admin")]
[Route("api/admin/storefront/checkout-fields")]
public sealed class AdminCheckoutFieldDefinitionsController : ControllerBase
{
    private readonly ICheckoutFieldDefinitionService _service;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminCheckoutFieldDefinitionsController> _logger;

    public AdminCheckoutFieldDefinitionsController(
        ICheckoutFieldDefinitionService service,
        ITenantContext tenantContext,
        ILogger<AdminCheckoutFieldDefinitionsController> logger)
    {
        _service = service;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(CheckoutFieldDefinitionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFields(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(
            "checkout_fields_get",
            cancellationToken,
            tenantId => _service.GetFieldsAsync(tenantId, cancellationToken));
    }

    [HttpPost("system-fields/initialize")]
    [ProducesResponseType(typeof(CheckoutFieldDefinitionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> InitializeSystemFields(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(
            "checkout_fields_initialize",
            cancellationToken,
            tenantId => _service.InitializeSystemFieldsAsync(tenantId, GetCurrentUserId(), cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType(typeof(CheckoutFieldDefinitionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCustomField(
        [FromBody] CreateCheckoutFieldDefinitionRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return BadRequest(Error("REQUEST_REQUIRED", "Request body is required."));
        }

        var result = await ExecuteAsync(
            "checkout_fields_create",
            cancellationToken,
            tenantId => _service.CreateCustomFieldAsync(tenantId, request, GetCurrentUserId(), cancellationToken));

        return result is ObjectResult { StatusCode: >= 400 }
            ? result
            : CreatedAtAction(nameof(GetFields), result is ObjectResult objectResult ? objectResult.Value : null);
    }

    [HttpPatch("{fieldId:guid}")]
    [ProducesResponseType(typeof(CheckoutFieldDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateField(
        Guid fieldId,
        [FromBody] UpdateCheckoutFieldDefinitionRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return BadRequest(Error("REQUEST_REQUIRED", "Request body is required."));
        }

        return await ExecuteAsync(
            "checkout_fields_update",
            cancellationToken,
            tenantId => _service.UpdateFieldAsync(tenantId, fieldId, request, GetCurrentUserId(), cancellationToken));
    }

    [HttpDelete("{fieldId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveCustomField(Guid fieldId, CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        try
        {
            await _service.ArchiveCustomFieldAsync(
                _tenantContext.RequiredTenantId,
                fieldId,
                GetCurrentUserId(),
                cancellationToken);
            return NoContent();
        }
        catch (CheckoutFieldDefinitionException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Archive checkout field failed | Operation: checkout_fields_archive");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    [HttpPut("order")]
    [ProducesResponseType(typeof(CheckoutFieldDefinitionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReorderFields(
        [FromBody] ReorderCheckoutFieldDefinitionsRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return BadRequest(Error("REQUEST_REQUIRED", "Request body is required."));
        }

        return await ExecuteAsync(
            "checkout_fields_reorder",
            cancellationToken,
            tenantId => _service.ReorderFieldsAsync(tenantId, request, GetCurrentUserId(), cancellationToken));
    }

    private async Task<IActionResult> ExecuteAsync<T>(
        string operation,
        CancellationToken cancellationToken,
        Func<Guid, Task<T>> action)
    {
        var traceId = GetTraceId();
        try
        {
            var response = await action(_tenantContext.RequiredTenantId);
            return Ok(response);
        }
        catch (CheckoutFieldDefinitionException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Checkout field operation failed | Operation: {Operation}", operation);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    private Guid? GetCurrentUserId()
    {
        if (HttpContext.Items.TryGetValue("AdminTenantAuthority.UserId", out var userIdValue) &&
            userIdValue is Guid authorityUserId &&
            authorityUserId != Guid.Empty)
        {
            return authorityUserId;
        }

        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) && userId != Guid.Empty ? userId : null;
    }

    private ApiErrorResponse Error(string code, string message) => new(code, message, GetTraceId());

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;
}
