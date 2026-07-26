using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.ShippingAdministration;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.ShippingAdministration;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/shipping")]
[Authorize(Roles = "Owner,Admin,Manager")]
public sealed class AdminShippingAdministrationController : ControllerBase
{
    private readonly IShippingAdministrationApiService _service;
    private readonly ITenantContext _tenantContext;

    public AdminShippingAdministrationController(
        IShippingAdministrationApiService service,
        ITenantContext tenantContext)
    {
        _service = service;
        _tenantContext = tenantContext;
    }

    [HttpGet("supported-carriers")]
    public async Task<IActionResult> ListSupportedCarriers(CancellationToken cancellationToken)
    {
        SetNoStoreHeaders();
        return Ok(await _service.ListSupportedCarriersAsync(cancellationToken));
    }

    [HttpGet("carrier-accounts")]
    public async Task<IActionResult> ListCarrierAccounts(CancellationToken cancellationToken)
    {
        SetNoStoreHeaders();
        return Ok(await _service.ListCarrierAccountsAsync(TenantId, cancellationToken));
    }

    [HttpGet("carrier-accounts/{carrierAccountId:guid}")]
    public async Task<IActionResult> GetCarrierAccount(Guid carrierAccountId, CancellationToken cancellationToken)
    {
        SetNoStoreHeaders();
        var account = await _service.GetCarrierAccountAsync(TenantId, carrierAccountId, cancellationToken);
        return account == null ? NotFound(Error("UNKNOWN_CARRIER_ACCOUNT")) : Ok(account);
    }

    [HttpPost("carrier-accounts")]
    public async Task<IActionResult> CreateCarrierAccount([FromBody] CreateCarrierAccountCommand command, CancellationToken cancellationToken) =>
        await ExecuteMutation(() => _service.CreateCarrierAccountAsync(TenantId, command, Actor, CorrelationId, cancellationToken));

    [HttpPut("carrier-accounts/{carrierAccountId:guid}")]
    public async Task<IActionResult> UpdateCarrierAccount(Guid carrierAccountId, [FromBody] UpdateCarrierAccountCommand command, CancellationToken cancellationToken) =>
        await ExecuteMutation(() => _service.UpdateCarrierAccountAsync(TenantId, carrierAccountId, command, Actor, CorrelationId, cancellationToken));

    [HttpPut("carrier-accounts/{carrierAccountId:guid}/credential-reference")]
    public async Task<IActionResult> UpdateCredentialReference(Guid carrierAccountId, [FromBody] CredentialReferenceCommand command, CancellationToken cancellationToken) =>
        await ExecuteMutation(() => _service.UpdateCredentialReferenceAsync(TenantId, carrierAccountId, command, Actor, CorrelationId, cancellationToken));

    [HttpPut("carrier-accounts/{carrierAccountId:guid}/environment")]
    public async Task<IActionResult> SelectEnvironment(Guid carrierAccountId, [FromBody] EnvironmentCommand command, CancellationToken cancellationToken) =>
        await ExecuteMutation(() => _service.SelectEnvironmentAsync(TenantId, carrierAccountId, command, Actor, CorrelationId, cancellationToken));

    [HttpPut("carrier-accounts/{carrierAccountId:guid}/warehouse-assignments/{warehouseId:guid}")]
    public async Task<IActionResult> AssignWarehouse(Guid carrierAccountId, Guid warehouseId, [FromBody] WarehouseAssignmentCommand command, CancellationToken cancellationToken)
    {
        var normalized = command with { WarehouseId = warehouseId };
        return await ExecuteMutation(() => _service.AssignWarehouseAsync(TenantId, carrierAccountId, normalized, Actor, CorrelationId, cancellationToken));
    }

    [HttpPut("carrier-accounts/{carrierAccountId:guid}/policies/domestic")]
    public async Task<IActionResult> SetDomesticPolicy(Guid carrierAccountId, [FromBody] PolicyCommand command, CancellationToken cancellationToken) =>
        await ExecuteMutation(() => _service.SetPolicyAsync(TenantId, carrierAccountId, "Domestic", command, Actor, CorrelationId, cancellationToken));

    [HttpPut("carrier-accounts/{carrierAccountId:guid}/policies/international")]
    public async Task<IActionResult> SetInternationalPolicy(Guid carrierAccountId, [FromBody] PolicyCommand command, CancellationToken cancellationToken) =>
        await ExecuteMutation(() => _service.SetPolicyAsync(TenantId, carrierAccountId, "International", command, Actor, CorrelationId, cancellationToken));

    [HttpPut("carrier-accounts/{carrierAccountId:guid}/service-levels/{serviceLevelCode}")]
    public async Task<IActionResult> SetServiceLevel(Guid carrierAccountId, string serviceLevelCode, [FromBody] ServiceLevelCommand command, CancellationToken cancellationToken) =>
        await ExecuteMutation(() => _service.SetServiceLevelAsync(TenantId, carrierAccountId, serviceLevelCode, command, Actor, CorrelationId, cancellationToken));

    [HttpPost("carrier-accounts/{carrierAccountId:guid}/connection-tests")]
    public async Task<IActionResult> RunConnectionTest(Guid carrierAccountId, [FromBody] ConnectionTestCommand command, CancellationToken cancellationToken) =>
        await ExecuteMutation(() => _service.RunConnectionTestAsync(TenantId, carrierAccountId, command, Actor, CorrelationId, cancellationToken));

    [HttpPost("carrier-accounts/{carrierAccountId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid carrierAccountId, [FromBody] ReadinessEvidenceCommand command, CancellationToken cancellationToken) =>
        await ExecuteMutation(() => _service.ActivateAsync(TenantId, carrierAccountId, command, Actor, CorrelationId, cancellationToken));

    [HttpPost("carrier-accounts/{carrierAccountId:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid carrierAccountId, CancellationToken cancellationToken) =>
        await ExecuteMutation(() => _service.SuspendAsync(TenantId, carrierAccountId, Actor, CorrelationId, cancellationToken));

    [HttpPost("carrier-accounts/{carrierAccountId:guid}/disable")]
    public async Task<IActionResult> Disable(Guid carrierAccountId, CancellationToken cancellationToken) =>
        await ExecuteMutation(() => _service.DisableAsync(TenantId, carrierAccountId, Actor, CorrelationId, cancellationToken));

    [HttpPost("carrier-accounts/{carrierAccountId:guid}/retire")]
    public async Task<IActionResult> Retire(Guid carrierAccountId, CancellationToken cancellationToken) =>
        await ExecuteMutation(() => _service.RetireAsync(TenantId, carrierAccountId, Actor, CorrelationId, cancellationToken));

    [HttpGet("carrier-accounts/{carrierAccountId:guid}/health")]
    public async Task<IActionResult> GetHealth(Guid carrierAccountId, CancellationToken cancellationToken)
    {
        SetNoStoreHeaders();
        var account = await _service.GetCarrierAccountAsync(TenantId, carrierAccountId, cancellationToken);
        if (account == null)
        {
            return NotFound(Error("UNKNOWN_CARRIER_ACCOUNT"));
        }

        return Ok(new
        {
            account.Id,
            account.State,
            account.HealthState,
            account.HealthReason,
            account.LastHealthEvaluatedAtUtc,
            account.LastConnectionTest
        });
    }

    [HttpPost("carrier-accounts/{carrierAccountId:guid}/health/evaluate")]
    public async Task<IActionResult> EvaluateHealth(Guid carrierAccountId, [FromBody] ReadinessEvidenceCommand command, CancellationToken cancellationToken) =>
        await ExecuteMutation(() => _service.EvaluateHealthAsync(TenantId, carrierAccountId, command, Actor, CorrelationId, cancellationToken));

    [HttpGet("audit")]
    public async Task<IActionResult> GetAudit([FromQuery] Guid? carrierAccountId, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        SetNoStoreHeaders();
        return Ok(await _service.GetAuditAsync(TenantId, carrierAccountId, limit, cancellationToken));
    }

    private async Task<IActionResult> ExecuteMutation(Func<Task<ShippingAdministrationResultDto>> action)
    {
        var result = await action();
        if (result.Succeeded)
        {
            return Ok(result);
        }

        return result.FailureCode == "UNKNOWN_CARRIER_ACCOUNT"
            ? NotFound(result)
            : BadRequest(result);
    }

    private Guid TenantId => _tenantContext.RequiredTenantId;

    private CarrierAdministrationActor Actor => new(ResolveActorId());

    private string CorrelationId => HttpContext.TraceIdentifier;

    private string ResolveActorId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        User.FindFirstValue("sub") ??
        User.FindFirstValue("userId") ??
        "unknown-pos-admin";

    private void SetNoStoreHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
    }

    private static object Error(string code) => new { error = code };
}
