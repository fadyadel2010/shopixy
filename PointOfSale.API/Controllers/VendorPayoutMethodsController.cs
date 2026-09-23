using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

[Authorize]
[ApiController]
[Route("api/vendors/payout-methods")]
public sealed class VendorPayoutMethodsController : ControllerBase
{
    private readonly ICommerceHubVendorPayoutMethodsService _payoutMethodsService;
    private readonly ITenantContext _tenantContext;

    private Guid TenantId => _tenantContext.RequiredTenantId;

    public VendorPayoutMethodsController(
        ICommerceHubVendorPayoutMethodsService payoutMethodsService,
        ITenantContext tenantContext)
    {
        _payoutMethodsService = payoutMethodsService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CommerceHubVendorPayoutMethodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!_tenantContext.HasTenant)
        {
            return BadRequest(new ApiErrorResponse("TENANT_REQUIRED", "Store tenant context is required.", HttpContext.TraceIdentifier));
        }

        var methods = await _payoutMethodsService.GetAsync(TenantId, cancellationToken);
        return Ok(methods);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CommerceHubVendorPayoutMethodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        [FromBody] CommerceHubCreateVendorPayoutMethodRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!_tenantContext.HasTenant)
        {
            return BadRequest(new ApiErrorResponse("TENANT_REQUIRED", "Store tenant context is required.", HttpContext.TraceIdentifier));
        }

        var tenantId = TenantId;

        try
        {
            var method = await _payoutMethodsService.CreateAsync(
                tenantId,
                request,
                ResolveActorId(),
                cancellationToken);

            return Ok(method);
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            return StatusCode(
                exception.StatusCode,
                new ApiErrorResponse(exception.Code, exception.Message, HttpContext.TraceIdentifier));
        }
    }

    private Guid? ResolveActorId()
    {
        var rawValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(rawValue, out var actorId) ? actorId : null;
    }
}
