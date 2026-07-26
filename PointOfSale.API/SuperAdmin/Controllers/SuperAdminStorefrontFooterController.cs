using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.SuperAdmin.Services;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/storefront/footer")]
public sealed class SuperAdminStorefrontFooterController : SuperAdminBaseController
{
    private readonly ISuperAdminStorefrontFooterService _footerService;

    public SuperAdminStorefrontFooterController(ISuperAdminStorefrontFooterService footerService)
    {
        _footerService = footerService;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfiguration(CancellationToken cancellationToken)
    {
        var response = await _footerService.GetGlobalConfigurationAsync(cancellationToken);
        return OkResponse(response);
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReplaceConfiguration(
        [FromBody] AdminStorefrontFooterConfigurationRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "STOREFRONT_GLOBAL_FOOTER_REQUEST_INVALID", "Request body is required.");
        }

        try
        {
            var response = await _footerService.ReplaceGlobalConfigurationAsync(request, cancellationToken);
            return OkResponse(response);
        }
        catch (ConcurrencyConflictException)
        {
            return ErrorResponse(StatusCodes.Status409Conflict, "STOREFRONT_GLOBAL_FOOTER_CONCURRENCY_CONFLICT", "Footer configuration was modified by another admin.");
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "STOREFRONT_GLOBAL_FOOTER_VALIDATION_ERROR", ex.Message);
        }
    }
}
