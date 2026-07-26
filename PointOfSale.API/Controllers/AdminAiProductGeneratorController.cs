using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.AI.Admin;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/ai/products")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminAiProductGeneratorController : ControllerBase
{
    private readonly IAdminProductGeneratorService _productGeneratorService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminAiProductGeneratorController> _logger;

    public AdminAiProductGeneratorController(
        IAdminProductGeneratorService productGeneratorService,
        ITenantContext tenantContext,
        ILogger<AdminAiProductGeneratorController> logger)
    {
        _productGeneratorService = productGeneratorService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpPost("generate")]
    [ProducesResponseType(typeof(AdminAiGeneratedProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GenerateProduct(
        [FromBody] AdminAiProductGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var generatedProduct = await _productGeneratorService.GenerateProductAsync(
                tenantId,
                request,
                cancellationToken);

            return Ok(generatedProduct);
        }
        catch (AdminProductGenerationException ex)
        {
            if (ex.RetryAfterSeconds.HasValue)
            {
                Response.Headers["Retry-After"] = ex.RetryAfterSeconds.Value.ToString();
            }

            return StatusCode(ex.StatusCode, new
            {
                error = ex.ErrorCode,
                message = ex.UserMessage,
                retryAfterSeconds = ex.RetryAfterSeconds
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                error = "AI timeout",
                message = "The AI request took too long. Please try again."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while generating admin AI product content");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Generation failed",
                message = "Unable to generate product content right now. Please try again."
            });
        }
    }

}
