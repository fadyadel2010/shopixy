using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/payouts")]
public sealed class SuperAdminPayoutsController : SuperAdminBaseController
{
    private readonly ISuperAdminPayoutsService _payoutsService;
    private readonly ISuperAdminPayoutOrchestrationService _payoutOrchestrationService;
    private readonly ISuperAdminAuditService _auditService;

    public SuperAdminPayoutsController(
        ISuperAdminPayoutsService payoutsService,
        ISuperAdminPayoutOrchestrationService payoutOrchestrationService,
        ISuperAdminAuditService auditService)
    {
        _payoutsService = payoutsService;
        _payoutOrchestrationService = payoutOrchestrationService;
        _auditService = auditService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SuperAdminVendorPayoutQueueResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        SuperAdminVendorPayoutQueueResultDto result;
        try
        {
            result = await _payoutsService.GetQueueAsync(page, pageSize, cancellationToken);
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }

        await _auditService.LogActionAsync(
            action: "VENDOR_PAYOUT_QUEUE_VIEWED",
            entityType: "CommerceHubVendorPayoutQueue",
            entityId: "payout-queue",
            description: "Super Admin viewed the vendor payout queue.",
            success: true,
            details: new
            {
                result.Page,
                result.PageSize,
                result.TotalCount
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(
            new SuperAdminVendorPayoutQueueResponseDto
            {
                Payouts = result.Items,
                CurrencySummaries = result.CurrencySummaries,
                HasCurrencyMix = result.HasCurrencyMix,
                AggregateCurrency = result.AggregateCurrency,
                TotalPayableAmount = result.TotalPayableAmount,
                TotalEligibleAmount = result.TotalEligibleAmount,
                TotalHeldAmount = result.TotalHeldAmount
            },
            new Dictionary<string, object?>
            {
                ["page"] = result.Page,
                ["pageSize"] = result.PageSize,
                ["totalCount"] = result.TotalCount,
                ["totalPages"] = result.TotalPages
            });
    }

    [HttpPost("{vendorId:guid}/execute")]
    [ProducesResponseType(typeof(CommerceHubPayoutDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Execute(
        Guid vendorId,
        [FromBody] SuperAdminExecuteVendorPayoutRequestDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payout = await _payoutOrchestrationService.ExecuteAsync(
                vendorId,
                request,
                CurrentSuperAdminId,
                CurrentSuperAdminEmail,
                cancellationToken);

            await _auditService.LogActionAsync(
                action: "VENDOR_PAYOUT_EXECUTED",
                entityType: "CommerceHubPayout",
                entityId: payout.PayoutId.ToString(),
                description: "Super Admin executed a vendor payout.",
                success: true,
                details: payout,
                reference: payout.PayoutId.ToString(),
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(payout);
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }
}
