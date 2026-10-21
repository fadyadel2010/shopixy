using System.Security.Claims;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Services;

namespace PointOfSale.API.Controllers;

[ApiController, Authorize, Route("api/qa/affiliate-certification")]
public sealed class SyntheticQaController(ApplicationDbContext db, SyntheticQaCertificationAuthority authority,
    ITenantContext tenant, IPaymentLifecycleService payments, IOrderFulfillmentService fulfillment) : ControllerBase
{
    public sealed record RunRequest(Guid PromotionLinkId, Guid CustomerId);
    public sealed record EvidenceRequest(Guid RunId, Guid OrderId, Guid? ShipmentId, string Kind);
    public sealed record ConsumeRequest(Guid OrderId, Guid EvidenceId);
    private Guid VerifierId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    private async Task<IActionResult> Guard(Func<Task<IActionResult>> action, CancellationToken ct)
    {
        try { await authority.EnsureVerifierAsync(tenant.RequiredTenantId, VerifierId, ct); return await action(); }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { code = "SYNTHETIC_QA_VERIFIER_REQUIRED" }); }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("SYNTHETIC_QA_UNAVAILABLE") || ex.Message.StartsWith("SYNTHETIC_QA_DATABASE"))
        { return NotFound(new { code = "SYNTHETIC_QA_UNAVAILABLE" }); }
        catch (InvalidOperationException ex) { return Conflict(new { code = ex.Message }); }
    }

    [HttpGet("status")]
    public Task<IActionResult> Status(CancellationToken ct) => Guard(() => Task.FromResult<IActionResult>(Ok(new { enabled = true,
        provenance = Core.Entities.SyntheticQaEvidence.ProvenanceLabel })), ct);

    [HttpPost("runs")]
    public Task<IActionResult> Run(RunRequest request, CancellationToken ct) => Guard(async () => {
        var run = await authority.CreateRunAsync(tenant.RequiredTenantId, VerifierId, request.PromotionLinkId, request.CustomerId, ct);
        return Ok(new { run = new { run.Id, run.TenantId, run.VerifierId, run.ProductId, run.PromotionLinkId,
            run.CustomerId, run.CreatedAtUtc, run.ExpiresAtUtc, run.Provenance },
            orderNotes = SyntheticQaCertificationAuthority.OrderMarker(run.Id) });
    }, ct);

    [HttpPost("evidence")]
    public Task<IActionResult> Evidence(EvidenceRequest request, CancellationToken ct) => Guard(async () => {
        // Serialize issuance using the same order-first lock as consumption. No caller-selected historical enrollment.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var order = await db.Orders.FromSqlInterpolated($@"SELECT *, xmin FROM ""Orders"" WHERE ""Id"" = {request.OrderId}
            AND ""TenantId"" = {tenant.RequiredTenantId} AND NOT ""IsDeleted"" FOR UPDATE").IgnoreQueryFilters().SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("SYNTHETIC_QA_ORDER_NOT_FOUND");
        var evidence = await authority.IssueAsync(order, request.RunId, VerifierId, request.ShipmentId ?? Guid.Empty, request.Kind, ct);
        await transaction.CommitAsync(ct);
        return Ok(new { evidence.Id, evidence.RunId, evidence.OrderId, evidence.ShipmentId, evidence.Kind,
            evidence.IssuedAtUtc, evidence.ConsumedAtUtc, evidence.Amount, evidence.Currency, evidence.Provenance });
    }, ct);

    [HttpPost("collect")]
    public Task<IActionResult> Collect(ConsumeRequest request, CancellationToken ct) => Guard(async () =>
        Ok(await payments.RecordSyntheticQaCollectionAsync(tenant.RequiredTenantId, request.OrderId, VerifierId, request.EvidenceId, ct)), ct);

    [HttpPost("deliver")]
    public Task<IActionResult> Deliver(ConsumeRequest request, CancellationToken ct) => Guard(async () => {
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == request.OrderId && x.TenantId == tenant.RequiredTenantId, ct)
            ?? throw new InvalidOperationException("SYNTHETIC_QA_ORDER_NOT_FOUND");
        var evidence = await authority.RequireEvidenceAsync(order, request.EvidenceId, "Delivery", ct);
        var result = await fulfillment.MarkDeliveredAsync(order.Id, FulfillmentRequestSource.Manual,
            evidence.DeliveryReference, evidence.ProofUrl, deliveredBy: $"SYNTHETIC-QA-VERIFIER:{VerifierId}",
            idempotencyKey: evidence.Id.ToString("N"), ct: ct);
        return Ok(result);
    }, ct);
}
