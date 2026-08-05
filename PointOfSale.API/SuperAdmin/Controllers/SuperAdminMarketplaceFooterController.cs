using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Admin;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/marketplace/footer")]
public sealed class SuperAdminMarketplaceFooterController : SuperAdminBaseController
{
    private readonly IFooterAuthoringService _footerService;

    public SuperAdminMarketplaceFooterController(IFooterAuthoringService footerService)
    {
        _footerService = footerService;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfiguration(CancellationToken cancellationToken)
    {
        var response = await _footerService.GetConfigurationAsync(
            FooterAuthoringScope.Marketplace,
            cancellationToken: cancellationToken);
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
            return ErrorResponse(StatusCodes.Status400BadRequest, "MARKETPLACE_FOOTER_REQUEST_INVALID", "Request body is required.");
        }

        try
        {
            var response = await _footerService.SaveConfigurationAsync(
                FooterAuthoringScope.Marketplace,
                tenantId: null,
                request,
                cancellationToken);
            return OkResponse(response);
        }
        catch (FooterAuthoringException ex)
        {
            return ErrorResponse(ex.StatusCode, ex.Code, ex.Message, new Dictionary<string, object?>
            {
                ["field"] = ex.Field
            });
        }
    }
}
