using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

[Authorize(Roles = "Owner,Admin")]
[ApiController]
[Route("api/admin/commercehub/finance")]
public sealed class AdminCommerceHubFinanceController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly ICommerceHubFinancialDashboardService _financialDashboardService;

    public AdminCommerceHubFinanceController(
        ITenantContext tenantContext,
        ICommerceHubFinancialDashboardService financialDashboardService)
    {
        _tenantContext = tenantContext;
        _financialDashboardService = financialDashboardService;
    }

    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(CommerceHubVendorFinanceDashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        var dashboard = await _financialDashboardService.GetVendorDashboardAsync(
            _tenantContext.RequiredTenantId,
            cancellationToken);

        return Ok(dashboard);
    }

    [HttpGet("statement")]
    [ProducesResponseType(typeof(CommerceHubFinanceStatementDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatement(
        [FromQuery] CommerceHubFinanceStatementQueryDto query,
        CancellationToken cancellationToken)
    {
        var statement = await _financialDashboardService.GetVendorStatementAsync(
            _tenantContext.RequiredTenantId,
            query,
            cancellationToken);

        return Ok(statement);
    }

    [HttpGet("/api/commercehub/finance/vendor/export")]
    public async Task<IActionResult> ExportVendorStatement(
        [FromQuery] CommerceHubFinanceStatementQueryDto query,
        CancellationToken cancellationToken)
    {
        var export = await _financialDashboardService.ExportVendorStatementCsvAsync(
            _tenantContext.RequiredTenantId,
            query,
            cancellationToken);

        return File(export.Content, export.ContentType, export.FileName);
    }
}
