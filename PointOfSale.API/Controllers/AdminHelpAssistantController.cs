using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.AI.Admin;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/ai/help-assistant")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminHelpAssistantController : ControllerBase
{
    private readonly IAdminHelpAssistantService _helpAssistantService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminHelpAssistantController> _logger;

    public AdminHelpAssistantController(
        IAdminHelpAssistantService helpAssistantService,
        ITenantContext tenantContext,
        ILogger<AdminHelpAssistantController> logger)
    {
        _helpAssistantService = helpAssistantService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpPost("ask")]
    [ProducesResponseType(typeof(AdminHelpAssistantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ask(
        [FromBody] AdminHelpAssistantRequest request,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString();
        var tenantId = _tenantContext.RequiredTenantId;

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var response = await _helpAssistantService.AskAsync(tenantId, request, cancellationToken);
            return Ok(response);
        }
        catch (AdminHelpAssistantException ex)
        {
            if (ex.RetryAfterSeconds.HasValue)
            {
                Response.Headers.RetryAfter = ex.RetryAfterSeconds.Value.ToString();
            }

            return StatusCode(ex.StatusCode, new
            {
                error = ex.ErrorCode,
                message = ex.UserMessage
            });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                error = "Request timeout",
                message = "The AI request took too long. Please try again."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin help assistant request failed | TenantId: {TenantId} | CorrelationId: {CorrelationId}", tenantId, correlationId);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Contextual help failed",
                message = "Unable to generate contextual help right now. Please try again."
            });
        }
    }

}
