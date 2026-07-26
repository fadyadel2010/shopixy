using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/orders")]
public sealed class SuperAdminOrdersController : SuperAdminBaseController
{
    private readonly ISuperAdminOrdersService _ordersService;
    private readonly ISuperAdminAuditService _auditService;
    private readonly ILogger<SuperAdminOrdersController> _logger;

    public SuperAdminOrdersController(
        ISuperAdminOrdersService ordersService,
        ISuperAdminAuditService auditService,
        ILogger<SuperAdminOrdersController> logger)
    {
        _ordersService = ordersService;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetOrders(
        [FromQuery] int page = SuperAdminOrdersListQuery.DefaultPage,
        [FromQuery] int pageSize = SuperAdminOrdersListQuery.DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] SuperAdminOrderPaymentFilter? paymentStatus = null,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] SuperAdminOrdersSortField sortBy = SuperAdminOrdersSortField.CreatedAt,
        [FromQuery] SuperAdminSortDirection sortDirection = SuperAdminSortDirection.Desc,
        CancellationToken cancellationToken = default)
    {
        var query = BuildListQuery(
            page,
            pageSize,
            search,
            status,
            paymentStatus,
            tenantId,
            fromDate,
            toDate,
            sortBy,
            sortDirection);

        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "INVALID_QUERY",
                validationError);
        }

        var result = await _ordersService.GetOrdersAsync(query, cancellationToken);
        var response = new SuperAdminOrdersListResponseDto
        {
            Orders = result.Items
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

        _logger.LogInformation(
            "SuperAdmin orders retrieved | Page: {Page} | PageSize: {PageSize} | TotalCount: {TotalCount} | Status: {Status} | PaymentStatus: {PaymentStatus} | TenantId: {TenantId} | SortBy: {SortBy} | SortDirection: {SortDirection}",
            page,
            pageSize,
            result.TotalCount,
            status,
            paymentStatus,
            tenantId,
            sortBy,
            sortDirection);

        await _auditService.LogActionAsync(
            action: "ORDERS_LIST_VIEWED",
            entityType: "CommerceHubOrder",
            entityId: "orders-monitoring",
            description: "Super Admin viewed the platform-wide orders monitoring list.",
            success: true,
            details: new
            {
                query.Page,
                query.PageSize,
                query.Search,
                query.Status,
                query.PaymentStatus,
                query.TenantId,
                query.FromDate,
                query.ToDate,
                query.SortBy,
                query.SortDirection,
                result.TotalCount
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(response, meta);
    }

    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrderStats(
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var stats = await _ordersService.GetOrderStatsAsync(tenantId, cancellationToken);

        _logger.LogDebug(
            "SuperAdmin order stats retrieved | TenantId: {TenantId} | TotalOrdersToday: {TotalOrdersToday} | PendingOrders: {PendingOrders} | FailedPayments: {FailedPayments}",
            tenantId,
            stats.TotalOrdersToday,
            stats.PendingOrders,
            stats.FailedPayments);

        return OkResponse(stats);
    }

    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportOrders(
        [FromQuery] string? search = null,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] SuperAdminOrderPaymentFilter? paymentStatus = null,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] SuperAdminOrdersSortField sortBy = SuperAdminOrdersSortField.CreatedAt,
        [FromQuery] SuperAdminSortDirection sortDirection = SuperAdminSortDirection.Desc,
        CancellationToken cancellationToken = default)
    {
        var query = BuildListQuery(
            SuperAdminOrdersListQuery.DefaultPage,
            SuperAdminOrdersListQuery.DefaultPageSize,
            search,
            status,
            paymentStatus,
            tenantId,
            fromDate,
            toDate,
            sortBy,
            sortDirection);

        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "INVALID_QUERY",
                validationError);
        }

        var csv = await _ordersService.ExportOrdersCsvAsync(query, cancellationToken);
        var fileName = $"commercehub_orders_{DateTime.UtcNow:yyyyMMddHHmm}.csv";

        _logger.LogInformation(
            "SuperAdmin exported CommerceHub orders | Status: {Status} | PaymentStatus: {PaymentStatus} | TenantId: {TenantId} | SortBy: {SortBy} | SortDirection: {SortDirection}",
            status,
            paymentStatus,
            tenantId,
            sortBy,
            sortDirection);

        await _auditService.LogActionAsync(
            action: "ORDERS_EXPORT_REQUESTED",
            entityType: "CommerceHubOrder",
            entityId: "orders-monitoring",
            description: "Super Admin exported filtered CommerceHub orders.",
            success: true,
            details: new
            {
                query.Search,
                query.Status,
                query.PaymentStatus,
                query.TenantId,
                query.FromDate,
                query.ToDate,
                query.SortBy,
                query.SortDirection
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return File(Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
    }

    [HttpGet("{orderId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderDetails(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _ordersService.GetOrderDetailsAsync(orderId, cancellationToken);
        if (order == null)
        {
            return ErrorResponse(
                StatusCodes.Status404NotFound,
                "ORDER_NOT_FOUND",
                "CommerceHub order was not found.");
        }

        _logger.LogInformation(
            "SuperAdmin order details retrieved | OrderId: {OrderId} | OrderNumber: {OrderNumber} | Status: {Status} | PaymentStatus: {PaymentStatus}",
            order.OrderId,
            order.OrderNumber,
            order.Status,
            order.PaymentStatus);

        await _auditService.LogActionAsync(
            action: "ORDER_DETAILS_VIEWED",
            entityType: "CommerceHubOrder",
            entityId: order.OrderId.ToString(),
            description: "Super Admin opened CommerceHub order details.",
            success: true,
            details: new
            {
                order.OrderNumber,
                order.Status,
                order.PaymentStatus,
                order.TotalAmount,
                order.VendorCount
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(order);
    }

    private static SuperAdminOrdersListQuery BuildListQuery(
        int page,
        int pageSize,
        string? search,
        OrderStatus? status,
        SuperAdminOrderPaymentFilter? paymentStatus,
        Guid? tenantId,
        DateTime? fromDate,
        DateTime? toDate,
        SuperAdminOrdersSortField sortBy,
        SuperAdminSortDirection sortDirection)
    {
        return new SuperAdminOrdersListQuery
        {
            Page = page,
            PageSize = pageSize,
            Search = search,
            Status = status,
            PaymentStatus = paymentStatus,
            TenantId = tenantId,
            FromDate = fromDate,
            ToDate = toDate,
            SortBy = sortBy,
            SortDirection = sortDirection
        };
    }
}
