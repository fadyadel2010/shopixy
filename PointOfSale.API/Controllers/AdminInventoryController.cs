using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin Inventory Controller - Admin/Ops inventory management.
/// 
/// PHASE 6.3: ADMIN INVENTORY OPERATIONS
/// 
/// Base Path: /api/admin/inventory
/// 
/// Endpoints:
/// - GET /api/admin/inventory (list inventory)
/// - POST /api/admin/inventory/adjust (adjust stock)
/// - GET /api/admin/inventory/movements (list movements)
/// 
/// Required Headers:
/// - X-Tenant-Id: GUID (required)
/// - X-Request-Id: string (required for adjust)
/// 
/// ARCHITECTURAL RULES:
/// - PostgreSQL is the ONLY source of truth
/// - Tenant isolation via Warehouse.TenantId and Product.TenantId
/// - Stock adjustments are atomic and auditable
/// - StockMovements are immutable
/// </summary>
[ApiController]
[Route("api/admin/inventory")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminInventoryController : ControllerBase
{
    private readonly IAdminInventoryService _adminInventoryService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminInventoryController> _logger;

    private const string RequestIdHeader = "X-Request-Id";

    public AdminInventoryController(
        IAdminInventoryService adminInventoryService,
        ITenantContext tenantContext,
        ILogger<AdminInventoryController> logger)
    {
        _adminInventoryService = adminInventoryService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// List inventory (warehouse stock) with filtering and pagination.
    /// 
    /// GET /api/admin/inventory
    /// X-Tenant-Id: {tenant-guid}
    /// 
    /// Query Parameters:
    /// - page: int (default 1)
    /// - pageSize: int (default 20, max 100)
    /// - warehouseId: Guid? (optional)
    /// - productId: Guid? (optional)
    /// - lowStockOnly: bool? (optional)
    /// - search: string? (optional, product name/SKU)
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminPagedResult<AdminInventoryListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminInventoryApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListInventory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] Guid? productId = null,
        [FromQuery] bool? lowStockOnly = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        var query = new AdminInventoryListQuery
        {
            Page = page,
            PageSize = pageSize,
            WarehouseId = warehouseId,
            ProductId = productId,
            LowStockOnly = lowStockOnly,
            Search = search
        };

        // Validate query
        var queryError = query.Validate();
        if (queryError != null)
        {
            return BadRequest(new AdminInventoryApiErrorResponse("INVALID_QUERY", queryError, traceId));
        }

        _logger.LogInformation(
            "ListInventory | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | TraceId: {TraceId}",
            validation.TenantId, page, pageSize, traceId);

        try
        {
            var result = await _adminInventoryService.ListInventoryAsync(
                validation.TenantId!.Value,
                query,
                traceId,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListInventory | TenantId: {TenantId} | Error | TraceId: {TraceId}", 
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                AdminInventoryApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Adjust stock for a product in a warehouse.
    /// 
    /// POST /api/admin/inventory/adjust
    /// X-Tenant-Id: {tenant-guid}
    /// X-Request-Id: {unique-request-id}
    /// 
    /// Request Body:
    /// {
    ///   "productId": "guid",
    ///   "warehouseId": "guid",
    ///   "adjustment": 10,
    ///   "reason": "Physical count correction",
    ///   "notes": "Optional notes"
    /// }
    /// </summary>
    [HttpPost("adjust")]
    [ProducesResponseType(typeof(AdminStockAdjustmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminInventoryApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminInventoryApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AdminInventoryApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AdjustStock(
        [FromBody] AdminStockAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        // Validate X-Request-Id header for idempotency
        if (!Request.Headers.TryGetValue(RequestIdHeader, out var requestIdValue) ||
            string.IsNullOrWhiteSpace(requestIdValue))
        {
            return BadRequest(new AdminInventoryApiErrorResponse(
                "REQUEST_ID_MISSING", 
                "X-Request-Id header is required for idempotency", 
                traceId));
        }

        var requestId = requestIdValue.ToString();

        _logger.LogInformation(
            "AdjustStock | TenantId: {TenantId} | ProductId: {ProductId} | WarehouseId: {WarehouseId} | Adjustment: {Adjustment} | RequestId: {RequestId} | TraceId: {TraceId}",
            validation.TenantId, request.ProductId, request.WarehouseId, request.Adjustment, requestId, traceId);

        try
        {
            var result = await _adminInventoryService.AdjustStockAsync(
                validation.TenantId!.Value,
                request,
                requestId,
                traceId,
                cancellationToken);

            return Ok(result);
        }
        catch (AdminProductNotFoundException)
        {
            return NotFound(AdminInventoryApiErrorResponse.ProductNotFound(traceId));
        }
        catch (AdminWarehouseNotFoundException)
        {
            return NotFound(AdminInventoryApiErrorResponse.WarehouseNotFound(traceId));
        }
        catch (InvalidAdjustmentException ex)
        {
            return BadRequest(new AdminInventoryApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (AdjustmentReasonRequiredException ex)
        {
            return BadRequest(new AdminInventoryApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (InsufficientStockException)
        {
            return BadRequest(AdminInventoryApiErrorResponse.InsufficientStock(traceId));
        }
        catch (DuplicateAdjustmentException)
        {
            return Conflict(AdminInventoryApiErrorResponse.DuplicateRequest(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdjustStock | TenantId: {TenantId} | ProductId: {ProductId} | WarehouseId: {WarehouseId} | Error | TraceId: {TraceId}", 
                validation.TenantId, request.ProductId, request.WarehouseId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                AdminInventoryApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// List stock movements (audit log) with filtering and pagination.
    /// 
    /// GET /api/admin/inventory/movements
    /// X-Tenant-Id: {tenant-guid}
    /// 
    /// Query Parameters:
    /// - page: int (default 1)
    /// - pageSize: int (default 20, max 100)
    /// - warehouseId: Guid? (optional)
    /// - productId: Guid? (optional)
    /// - movementType: StockMovementType? (optional)
    /// - fromDate: DateTime? (optional)
    /// - toDate: DateTime? (optional)
    /// </summary>
    [HttpGet("movements")]
    [ProducesResponseType(typeof(AdminPagedResult<AdminStockMovementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminInventoryApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListMovements(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] Guid? productId = null,
        [FromQuery] StockMovementType? movementType = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        var query = new AdminStockMovementListQuery
        {
            Page = page,
            PageSize = pageSize,
            WarehouseId = warehouseId,
            ProductId = productId,
            MovementType = movementType,
            FromDate = fromDate,
            ToDate = toDate
        };

        // Validate query
        var queryError = query.Validate();
        if (queryError != null)
        {
            return BadRequest(new AdminInventoryApiErrorResponse("INVALID_QUERY", queryError, traceId));
        }

        _logger.LogInformation(
            "ListMovements | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | TraceId: {TraceId}",
            validation.TenantId, page, pageSize, traceId);

        try
        {
            var result = await _adminInventoryService.ListMovementsAsync(
                validation.TenantId!.Value,
                query,
                traceId,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListMovements | TenantId: {TenantId} | Error | TraceId: {TraceId}", 
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                AdminInventoryApiErrorResponse.InternalError(traceId));
        }
    }

    #region Header Validation

    private (Guid? TenantId, IActionResult? Error) ValidateTenantHeader(string traceId)
    {
        try
        {
            return (_tenantContext.RequiredTenantId, null);
        }
        catch (InvalidOperationException)
        {
            return (null, BadRequest(AdminInventoryApiErrorResponse.TenantHeaderMissing(traceId)));
        }
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    #endregion
}
