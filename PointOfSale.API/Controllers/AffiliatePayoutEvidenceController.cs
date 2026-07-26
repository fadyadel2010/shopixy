using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Affiliate;

namespace PointOfSale.API.Controllers;

[Authorize(Roles = "Owner,Admin")]
[ApiController]
[Route("api/admin/affiliates/payout-evidence")]
public sealed class AffiliatePayoutEvidenceController : ControllerBase
{
    private readonly IAffiliatePayoutEvidenceService _evidenceService;
    private readonly ITenantContext _tenantContext;

    public AffiliatePayoutEvidenceController(
        IAffiliatePayoutEvidenceService evidenceService,
        ITenantContext tenantContext)
    {
        _evidenceService = evidenceService;
        _tenantContext = tenantContext;
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify(
        [FromBody] VerifyAffiliatePayoutEvidenceRequest request,
        CancellationToken ct)
    {
        try
        {
            var evidence = await _evidenceService.RegisterVerifiedEvidenceAsync(
                _tenantContext.RequiredTenantId,
                request.RailNamespace,
                request.PaymentReference,
                request.Amount,
                request.Currency,
                request.VerifiedAtUtc,
                request.Signature,
                ct);

            return Ok(new
            {
                evidence.Id,
                evidence.StoreTenantId,
                evidence.RailNamespace,
                evidence.PaymentReference,
                evidence.ExpectedAmount,
                evidence.Currency,
                evidence.VerifiedAtUtc,
                evidence.VerificationSource
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public sealed class VerifyAffiliatePayoutEvidenceRequest
{
    public string RailNamespace { get; set; } = "AFFILIATE_PAYOUT";
    public string PaymentReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset VerifiedAtUtc { get; set; }
    public string Signature { get; set; } = string.Empty;
}
