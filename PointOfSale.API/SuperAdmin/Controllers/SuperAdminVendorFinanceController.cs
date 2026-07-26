using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/vendors")]
[Route("api/super-admin/finance/vendors")]
public sealed class SuperAdminVendorFinanceController : SuperAdminBaseController
{
    private readonly ISuperAdminVendorFinanceService _vendorFinanceService;
    private readonly ISuperAdminAuditService _auditService;
    private readonly ILogger<SuperAdminVendorFinanceController> _logger;

    public SuperAdminVendorFinanceController(
        ISuperAdminVendorFinanceService vendorFinanceService,
        ISuperAdminAuditService auditService,
        ILogger<SuperAdminVendorFinanceController> logger)
    {
        _vendorFinanceService = vendorFinanceService;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet("finance")]
    [ProducesResponseType(typeof(SuperAdminVendorFinanceListResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetVendors(
        [FromQuery] SuperAdminVendorFinanceListQuery query,
        CancellationToken cancellationToken = default)
    {
        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "INVALID_QUERY",
                validationError);
        }

        var result = await _vendorFinanceService.GetVendorsAsync(query, cancellationToken);
        var response = new SuperAdminVendorFinanceListResponseDto
        {
            Vendors = result.Items
        };

        var meta = new Dictionary<string, object?>
        {
            ["page"] = result.Page,
            ["pageSize"] = result.PageSize,
            ["totalCount"] = result.TotalCount,
            ["totalPages"] = result.TotalPages,
            ["sortBy"] = query.SortBy.ToString(),
            ["sortDirection"] = query.SortDirection.ToString(),
            ["highRefundRatioThreshold"] = query.HighRefundRatioThreshold,
            ["inactiveDays"] = query.InactiveDays
        };

        _logger.LogInformation(
            "SuperAdmin vendor finance list retrieved | Page: {Page} | PageSize: {PageSize} | TotalCount: {TotalCount} | NegativeOnly: {NegativeOnly} | HighRefundOnly: {HighRefundOnly} | InactiveOnly: {InactiveOnly} | SortBy: {SortBy} | SortDirection: {SortDirection}",
            query.Page,
            query.PageSize,
            result.TotalCount,
            query.NegativeBalanceOnly,
            query.HighRefundRatioOnly,
            query.InactiveOnly,
            query.SortBy,
            query.SortDirection);

        await _auditService.LogActionAsync(
            action: "VENDOR_FINANCE_LIST_VIEWED",
            entityType: "CommerceHubVendorFinance",
            entityId: "vendors-finance",
            description: "Super Admin viewed the vendor finance overview.",
            success: true,
            details: new
            {
                query.Page,
                query.PageSize,
                query.Search,
                query.NegativeBalanceOnly,
                query.HighRefundRatioOnly,
                query.InactiveOnly,
                query.InactiveDays,
                query.HighRefundRatioThreshold,
                query.SortBy,
                query.SortDirection,
                result.TotalCount
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(response, meta);
    }

    [HttpGet("alerts")]
    [ProducesResponseType(typeof(SuperAdminVendorFinanceAlertsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAlerts(
        [FromQuery] SuperAdminVendorFinanceAlertsQuery query,
        CancellationToken cancellationToken = default)
    {
        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "INVALID_QUERY", validationError);
        }

        var result = await _vendorFinanceService.GetAlertsAsync(query, cancellationToken);
        var response = new SuperAdminVendorFinanceAlertsResponseDto
        {
            Alerts = result.Items
        };

        var meta = new Dictionary<string, object?>
        {
            ["page"] = result.Page,
            ["pageSize"] = result.PageSize,
            ["totalCount"] = result.TotalCount,
            ["totalPages"] = result.TotalPages,
            ["highRefundRatioThreshold"] = query.HighRefundRatioThreshold,
            ["inactiveDays"] = query.InactiveDays
        };

        await _auditService.LogActionAsync(
            action: "VENDOR_FINANCE_ALERTS_VIEWED",
            entityType: "CommerceHubVendorFinanceAlerts",
            entityId: "vendor-finance-alerts",
            description: "Super Admin viewed vendor finance alerts.",
            success: true,
            details: new
            {
                query.Page,
                query.PageSize,
                query.HighRefundRatioThreshold,
                query.InactiveDays,
                result.TotalCount
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(response, meta);
    }

    [HttpGet("{vendorId:guid}/statement")]
    [ProducesResponseType(typeof(CommerceHubFinanceStatementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatement(
        Guid vendorId,
        [FromQuery] CommerceHubFinanceStatementQueryDto query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _vendorFinanceService.GetVendorStatementAsync(vendorId, query, cancellationToken);

            await _auditService.LogActionAsync(
                action: "VENDOR_FINANCE_STATEMENT_VIEWED",
                entityType: "CommerceHubVendorFinanceStatement",
                entityId: vendorId.ToString(),
                description: "Super Admin viewed a vendor finance statement.",
                success: true,
                details: new
                {
                    VendorId = vendorId,
                    query.Page,
                    query.PageSize,
                    query.Size,
                    query.FromDateUtc,
                    query.ToDateUtc,
                    query.FromDate,
                    query.ToDate,
                    query.TransactionTypes
                },
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(response);
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            _logger.LogWarning(
                "SuperAdmin vendor statement lookup blocked | VendorId: {VendorId} | Code: {Code}",
                vendorId,
                exception.Code);

            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpGet("{vendorId:guid}/audit")]
    [ProducesResponseType(typeof(SuperAdminVendorFinanceAuditResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAudit(
        Guid vendorId,
        [FromQuery] SuperAdminVendorFinanceAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "INVALID_QUERY", validationError);
        }

        try
        {
            var result = await _vendorFinanceService.GetVendorAuditAsync(vendorId, query, cancellationToken);
            var response = new SuperAdminVendorFinanceAuditResponseDto
            {
                Timeline = result.Items
            };

            var meta = new Dictionary<string, object?>
            {
                ["page"] = result.Page,
                ["pageSize"] = result.PageSize,
                ["totalCount"] = result.TotalCount,
                ["totalPages"] = result.TotalPages
            };

            await _auditService.LogActionAsync(
                action: "VENDOR_FINANCE_AUDIT_VIEWED",
                entityType: "CommerceHubVendorFinanceAudit",
                entityId: vendorId.ToString(),
                description: "Super Admin viewed a vendor finance audit timeline.",
                success: true,
                details: new
                {
                    VendorId = vendorId,
                    query.Page,
                    query.PageSize,
                    result.TotalCount
                },
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(response, meta);
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            _logger.LogWarning(
                "SuperAdmin vendor audit lookup blocked | VendorId: {VendorId} | Code: {Code}",
                vendorId,
                exception.Code);

            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpGet("{vendorId:guid}/statement/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportStatement(
        Guid vendorId,
        [FromQuery] CommerceHubFinanceStatementQueryDto query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var export = await _vendorFinanceService.ExportVendorStatementCsvAsync(vendorId, query, cancellationToken);

            await _auditService.LogActionAsync(
                action: "VENDOR_FINANCE_STATEMENT_EXPORTED",
                entityType: "CommerceHubVendorFinanceStatement",
                entityId: vendorId.ToString(),
                description: "Super Admin exported a vendor finance statement.",
                success: true,
                details: new
                {
                    VendorId = vendorId,
                    query.FromDateUtc,
                    query.ToDateUtc,
                    query.FromDate,
                    query.ToDate,
                    query.TransactionTypes
                },
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return File(export.Content, export.ContentType, export.FileName);
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            _logger.LogWarning(
                "SuperAdmin vendor statement export blocked | VendorId: {VendorId} | Code: {Code}",
                vendorId,
                exception.Code);

            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPost("{vendorId:guid}/adjustment")]
    [ProducesResponseType(typeof(SuperAdminVendorFinancialActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> RecordAdjustment(
        Guid vendorId,
        [FromBody] SuperAdminVendorAdjustmentRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(
            vendorId,
            "VENDOR_FINANCE_ADJUSTMENT_RECORDED",
            "Super Admin recorded a vendor finance adjustment.",
            () => _vendorFinanceService.RecordAdjustmentAsync(vendorId, request, CurrentSuperAdminId, cancellationToken),
            cancellationToken);

    [HttpPost("{vendorId:guid}/settlement")]
    [ProducesResponseType(typeof(SuperAdminVendorFinancialActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> RecordSettlement(
        Guid vendorId,
        [FromBody] SuperAdminVendorSettlementRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(
            vendorId,
            "VENDOR_FINANCE_SETTLEMENT_RECORDED",
            "Super Admin recorded a vendor settlement.",
            () => _vendorFinanceService.RecordSettlementAsync(vendorId, request, CurrentSuperAdminId, cancellationToken),
            cancellationToken);

    private async Task<IActionResult> ExecuteActionAsync(
        Guid vendorId,
        string auditAction,
        string auditDescription,
        Func<Task<SuperAdminVendorFinancialActionResultDto>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await action();

            await _auditService.LogActionAsync(
                action: auditAction,
                entityType: "CommerceHubVendorFinanceAction",
                entityId: response.Action.ReferenceId.ToString(),
                description: auditDescription,
                success: true,
                details: response,
                reference: response.Action.CorrelationId,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(response);
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            _logger.LogWarning(
                "SuperAdmin vendor finance action blocked | VendorId: {VendorId} | Code: {Code}",
                vendorId,
                exception.Code);

            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }
}
