using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Attributes;
using PointOfSale.API.Development.CommerceHub;
using PointOfSale.API.SuperAdmin.Models;

namespace PointOfSale.API.Controllers;

[DevelopmentOnlyController]
[AllowAnonymousTenant]
[ApiController]
[Route("api/dev/commercehub")]
[ApiExplorerSettings(IgnoreApi = true)]
[Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]
public class DevelopmentCommerceHubController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;
    private readonly IDevelopmentCommerceHubSeedService _seedService;

    public DevelopmentCommerceHubController(
        IWebHostEnvironment environment,
        IDevelopmentCommerceHubSeedService seedService)
    {
        _environment = environment;
        _seedService = seedService;
    }

    [HttpPost("seed")]
    public async Task<ActionResult<CommerceHubSeedResult>> Seed(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var result = await _seedService.SeedAsync(cancellationToken);
        return Ok(result);
    }

    [HttpDelete("seed")]
    public async Task<ActionResult<CommerceHubSeedResetResult>> Reset(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var result = await _seedService.ResetAsync(cancellationToken);
        return Ok(result);
    }
}
