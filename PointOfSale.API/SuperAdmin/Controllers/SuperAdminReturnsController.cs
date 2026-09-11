using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/returns")]
public sealed class SuperAdminReturnsController : SuperAdminBaseController
{
    private readonly IReturnManagementService _returnManagementService;
    private readonly ILogger<SuperAdminReturnsController> _logger;

    public SuperAdminReturnsController(
        IReturnManagementService returnManagementService,
        ILogger<SuperAdminReturnsController> logger)
    {
        _returnManagementService = returnManagementService;
        _logger = logger;
    }

    [HttpGet("{returnId:guid}")]
    [ProducesResponseType(typeof(CommerceHubReturnDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReturn(
        Guid returnId,
        CancellationToken cancellationToken = default) =>
        await ExecuteActionAsync(
            returnId,
            async () =>
            {
                var result = await _returnManagementService.GetReturnDetailsAsync(returnId, cancellationToken);
                if (result == null)
                {
                    throw new CommerceHubCheckoutException(
                        "RETURN_NOT_FOUND",
                        "Return request was not found.",
                        StatusCodes.Status404NotFound);
                }

                return result;
            });

    private async Task<IActionResult> ExecuteActionAsync(
        Guid returnId,
        Func<Task<CommerceHubReturnDetailsDto>> action)
    {
        try
        {
            var result = await action();
            return OkResponse(result);
        }
        catch (CommerceHubCheckoutException ex)
        {
            _logger.LogWarning(
                "SuperAdmin return action blocked | ReturnId: {ReturnId} | Code: {Code}",
                returnId,
                ex.Code);

            return ErrorResponse(ex.StatusCode, ex.Code, ex.Message);
        }
    }
}
