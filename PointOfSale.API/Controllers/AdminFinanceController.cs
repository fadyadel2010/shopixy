using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Attributes;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Core.Pagination;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/finance")]
[AllowAnonymousTenant]
[Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]
public class AdminFinanceController : ControllerBase
{
    private readonly ICommerceHubFinanceReadService _financeReadService;

    public AdminFinanceController(ICommerceHubFinanceReadService financeReadService)
    {
        _financeReadService = financeReadService;
    }

    [HttpGet("overview")]
    [ProducesResponseType(typeof(CommerceHubFinanceOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview(CancellationToken cancellationToken)
    {
        var overview = await _financeReadService.GetOverviewAsync(cancellationToken);
        return Ok(overview);
    }

    [HttpGet("payments")]
    [ProducesResponseType(typeof(IReadOnlyList<CommerceHubAdminPaymentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayments([FromQuery] int take = PaginationGuard.MaxPageSize, CancellationToken cancellationToken = default)
    {
        var payments = await _financeReadService.GetPaymentsAsync(take, cancellationToken);
        return Ok(payments);
    }

    [HttpGet("earnings")]
    [ProducesResponseType(typeof(IReadOnlyList<CommerceHubAdminEarningDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEarnings([FromQuery] int take = PaginationGuard.MaxPageSize, CancellationToken cancellationToken = default)
    {
        var earnings = await _financeReadService.GetEarningsAsync(take, cancellationToken);
        return Ok(earnings);
    }

    [HttpGet("payouts")]
    [ProducesResponseType(typeof(IReadOnlyList<CommerceHubAdminPayoutDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayouts([FromQuery] int take = PaginationGuard.MaxPageSize, CancellationToken cancellationToken = default)
    {
        var payouts = await _financeReadService.GetPayoutsAsync(take, cancellationToken);
        return Ok(payouts);
    }

    [HttpGet("refunds")]
    [ProducesResponseType(typeof(IReadOnlyList<CommerceHubAdminRefundDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRefunds([FromQuery] int take = PaginationGuard.MaxPageSize, CancellationToken cancellationToken = default)
    {
        var refunds = await _financeReadService.GetRefundsAsync(take, cancellationToken);
        return Ok(refunds);
    }

    [HttpGet("audit-log")]
    [ProducesResponseType(typeof(IReadOnlyList<CommerceHubAdminAuditLogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditLog([FromQuery] int take = PaginationGuard.MaxPageSize, CancellationToken cancellationToken = default)
    {
        var logs = await _financeReadService.GetAuditLogAsync(take, cancellationToken);
        return Ok(logs);
    }
}
