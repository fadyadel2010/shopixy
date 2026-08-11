using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Helpers;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/storefront/checkout-fields")]
[AllowAnonymous]
public sealed class StorefrontCheckoutFieldsController : ControllerBase
{
    private readonly IStorefrontCheckoutFieldDefinitionService _service;
    private readonly ILogger<StorefrontCheckoutFieldsController> _logger;

    public StorefrontCheckoutFieldsController(
        IStorefrontCheckoutFieldDefinitionService service,
        ILogger<StorefrontCheckoutFieldsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(StorefrontCheckoutFieldDefinitionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetFields(CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var validation = StorefrontTenantContextResolver.ValidateTenant(this, traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            var response = await _service.GetFieldsAsync(validation.TenantId!.Value, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Error getting storefront checkout fields | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId,
                traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }
}
