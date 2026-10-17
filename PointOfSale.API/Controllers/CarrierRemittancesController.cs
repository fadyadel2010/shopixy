using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Accounting;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/treasury/carrier-remittances")]
[Authorize(Roles = "Owner,Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CarrierRemittancesController(ICarrierRemittanceService service, ITenantContext tenant) : ControllerBase
{
    private Guid? Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) &&
        User.Identity?.IsAuthenticated == true && id != Guid.Empty ? id : null;

    [HttpGet("eligible-orders")]
    public async Task<IActionResult> Select([FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (Actor is not { } actor) return Forbid();
        try { return Ok(await service.SelectAsync(tenant.RequiredTenantId, actor, page, ct)); }
        catch (CarrierRemittanceException ex) { return Failure(ex); }
    }

    [HttpGet("{operationId:guid}")]
    public async Task<IActionResult> Get(Guid operationId, CancellationToken ct = default)
    {
        if (Actor is not { } actor) return Forbid();
        try
        {
            var result = await service.GetAsync(tenant.RequiredTenantId, actor, operationId, ct);
            return result == null ? NotFound() : Ok(result);
        }
        catch (CarrierRemittanceException ex) { return Failure(ex); }
    }

    [HttpPost]
    [EnableRateLimiting("FinancialWrite")]
    public async Task<IActionResult> Receive([FromBody] CarrierRemittanceRequest request, CancellationToken ct = default)
    {
        if (Actor is not { } actor) return Forbid();
        try { return Ok(await service.ReceiveAsync(tenant.RequiredTenantId, actor, request, ct)); }
        catch (CarrierRemittanceException ex) { return Failure(ex); }
    }

    private IActionResult Failure(CarrierRemittanceException ex) => ex.Code is "REMITTANCE_ACTOR_REQUIRED" or "REMITTANCE_TENANT_REQUIRED" or "REMITTANCE_TENANT_INACTIVE"
        ? Forbid() : Conflict(new { code = ex.Code, outcome = "ReconciliationRequired", accountingOutcome = "NotPosted" });
}
