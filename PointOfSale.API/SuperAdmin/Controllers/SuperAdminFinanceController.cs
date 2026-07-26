using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/finance")]
public sealed class SuperAdminFinanceController : SuperAdminBaseController
{
    private readonly ISuperAdminFinanceOverviewService _financeOverviewService;
    private readonly ISuperAdminFinancialDashboardService _financialDashboardService;
    private readonly ISuperAdminAuditService _auditService;
    private readonly ILogger<SuperAdminFinanceController> _logger;

    public SuperAdminFinanceController(
        ISuperAdminFinanceOverviewService financeOverviewService,
        ISuperAdminFinancialDashboardService financialDashboardService,
        ISuperAdminAuditService auditService,
        ILogger<SuperAdminFinanceController> logger)
    {
        _financeOverviewService = financeOverviewService;
        _financialDashboardService = financialDashboardService;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet("overview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetOverview(
        [FromQuery] SuperAdminFinancialDashboardFilterDto query,
        CancellationToken cancellationToken)
    {
        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "INVALID_QUERY", validationError);
        }

        var response = await _financialDashboardService.GetOverviewAsync(query, cancellationToken);

        _logger.LogInformation(
            "SuperAdmin financial dashboard overview retrieved | RequestId: {RequestId} | SuperAdminId: {SuperAdminId} | Endpoint: {Endpoint} | Method: {Method} | TimestampUtc: {TimestampUtc}",
            HttpContext.TraceIdentifier,
            CurrentSuperAdminId,
            HttpContext.Request.Path.Value ?? "unknown",
            HttpContext.Request.Method,
            DateTime.UtcNow);

        await _auditService.LogActionAsync(
            action: "FINANCIAL_DASHBOARD_OVERVIEW_VIEWED",
            entityType: "Dashboard",
            entityId: "finance-overview",
            description: "Super Admin viewed financial dashboard overview.",
            success: true,
            details: new
            {
                query.FromDateUtc,
                query.ToDateUtc,
                query.FromDate,
                query.ToDate,
                query.VendorSearch,
                query.PromoCode,
                Response = response
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(response);
    }

    [HttpGet("ledger-overview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLedgerOverview(CancellationToken cancellationToken)
    {
        var response = await _financeOverviewService.GetOverviewAsync(cancellationToken);

        await _auditService.LogActionAsync(
            action: "FINANCE_LEDGER_OVERVIEW_VIEWED",
            entityType: "Dashboard",
            entityId: "finance-ledger-overview",
            description: "Super Admin viewed the legacy ledger overview.",
            success: true,
            details: response,
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(response);
    }

    [HttpGet("trends")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTrends(
        [FromQuery] SuperAdminFinancialTrendsQueryDto query,
        CancellationToken cancellationToken)
    {
        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "INVALID_QUERY", validationError);
        }

        var response = await _financialDashboardService.GetTrendsAsync(query, cancellationToken);

        await _auditService.LogActionAsync(
            action: "FINANCIAL_DASHBOARD_TRENDS_VIEWED",
            entityType: "Dashboard",
            entityId: "finance-trends",
            description: "Super Admin viewed financial trends.",
            success: true,
            details: new
            {
                query.FromDateUtc,
                query.ToDateUtc,
                query.FromDate,
                query.ToDate,
                query.VendorSearch,
                query.PromoCode,
                query.GroupBy
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(response);
    }

    [HttpGet("promos")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPromos(
        [FromQuery] SuperAdminPromoAnalyticsQueryDto query,
        CancellationToken cancellationToken)
    {
        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "INVALID_QUERY", validationError);
        }

        var result = await _financialDashboardService.GetPromoAnalyticsAsync(query, cancellationToken);
        var response = new SuperAdminPromoAnalyticsResponseDto
        {
            Items = result.Items
        };

        var meta = new Dictionary<string, object?>
        {
            ["page"] = result.Page,
            ["pageSize"] = result.PageSize,
            ["totalCount"] = result.TotalCount,
            ["totalPages"] = result.TotalPages,
            ["sortBy"] = query.SortBy.ToString(),
            ["sortDirection"] = query.SortDirection.ToString()
        };

        await _auditService.LogActionAsync(
            action: "FINANCIAL_DASHBOARD_PROMOS_VIEWED",
            entityType: "Dashboard",
            entityId: "finance-promos",
            description: "Super Admin viewed promo analytics.",
            success: true,
            details: new
            {
                query.FromDateUtc,
                query.ToDateUtc,
                query.FromDate,
                query.ToDate,
                query.VendorSearch,
                query.PromoCode,
                query.Page,
                query.PageSize,
                query.SortBy,
                query.SortDirection,
                result.TotalCount
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(response, meta);
    }

    [HttpGet("vendors")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetVendorsPerformance(
        [FromQuery] SuperAdminVendorPerformanceQueryDto query,
        CancellationToken cancellationToken)
    {
        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "INVALID_QUERY", validationError);
        }

        var result = await _financialDashboardService.GetVendorPerformanceAsync(query, cancellationToken);
        var response = new SuperAdminVendorPerformanceResponseDto
        {
            Items = result.Items
        };

        var meta = new Dictionary<string, object?>
        {
            ["page"] = result.Page,
            ["pageSize"] = result.PageSize,
            ["totalCount"] = result.TotalCount,
            ["totalPages"] = result.TotalPages,
            ["sortBy"] = query.SortBy.ToString(),
            ["sortDirection"] = query.SortDirection.ToString()
        };

        await _auditService.LogActionAsync(
            action: "FINANCIAL_DASHBOARD_VENDORS_VIEWED",
            entityType: "Dashboard",
            entityId: "finance-vendors",
            description: "Super Admin viewed vendor financial performance.",
            success: true,
            details: new
            {
                query.FromDateUtc,
                query.ToDateUtc,
                query.FromDate,
                query.ToDate,
                query.VendorSearch,
                query.PromoCode,
                query.Page,
                query.PageSize,
                query.SortBy,
                query.SortDirection,
                result.TotalCount
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(response, meta);
    }

    [HttpGet("refunds")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRefunds(
        [FromQuery] SuperAdminRefundAnalysisQueryDto query,
        CancellationToken cancellationToken)
    {
        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "INVALID_QUERY", validationError);
        }

        var response = await _financialDashboardService.GetRefundAnalysisAsync(query, cancellationToken);

        await _auditService.LogActionAsync(
            action: "FINANCIAL_DASHBOARD_REFUNDS_VIEWED",
            entityType: "Dashboard",
            entityId: "finance-refunds",
            description: "Super Admin viewed refund analysis.",
            success: true,
            details: new
            {
                query.FromDateUtc,
                query.ToDateUtc,
                query.FromDate,
                query.ToDate,
                query.VendorSearch,
                query.PromoCode,
                query.GroupBy
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(response);
    }

    [HttpGet("integrity-check")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetIntegrityCheck(
        [FromQuery] SuperAdminFinancialIntegrityCheckQueryDto query,
        CancellationToken cancellationToken)
    {
        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "INVALID_QUERY", validationError);
        }

        var response = await _financialDashboardService.GetIntegrityCheckAsync(query, cancellationToken);

        await _auditService.LogActionAsync(
            action: "FINANCIAL_DASHBOARD_INTEGRITY_VIEWED",
            entityType: "Dashboard",
            entityId: "finance-integrity",
            description: "Super Admin viewed financial integrity check results.",
            success: true,
            details: new
            {
                query.FromDateUtc,
                query.ToDateUtc,
                query.FromDate,
                query.ToDate,
                query.VendorSearch,
                query.PromoCode,
                query.MaxIssues,
                response.Status,
                response.TotalIssueCount
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(response);
    }

    [HttpGet("cashflow")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCashflow(
        [FromQuery] SuperAdminFinanceReportQueryDto query,
        CancellationToken cancellationToken)
    {
        var response = await _financeOverviewService.GetCashflowAsync(query, cancellationToken);

        await _auditService.LogActionAsync(
            action: "FINANCE_CASHFLOW_VIEWED",
            entityType: "Dashboard",
            entityId: "finance-cashflow",
            description: "Super Admin viewed finance cashflow report.",
            success: true,
            details: response,
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(response);
    }

    [HttpGet("pnl")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfitAndLoss(
        [FromQuery] SuperAdminFinanceReportQueryDto query,
        CancellationToken cancellationToken)
    {
        var response = await _financeOverviewService.GetProfitAndLossAsync(query, cancellationToken);

        await _auditService.LogActionAsync(
            action: "FINANCE_PNL_VIEWED",
            entityType: "Dashboard",
            entityId: "finance-pnl",
            description: "Super Admin viewed finance profit and loss report.",
            success: true,
            details: response,
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(response);
    }

    [HttpGet("reconciliation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReconciliation(CancellationToken cancellationToken)
    {
        var response = await _financeOverviewService.GetReconciliationAsync(cancellationToken);

        await _auditService.LogActionAsync(
            action: "FINANCE_RECONCILIATION_VIEWED",
            entityType: "Dashboard",
            entityId: "finance-reconciliation",
            description: "Super Admin viewed finance reconciliation snapshot.",
            success: true,
            details: response,
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(response);
    }
}
