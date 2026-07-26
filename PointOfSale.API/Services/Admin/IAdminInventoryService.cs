using PointOfSale.API.DTOs.Admin;

namespace PointOfSale.API.Services.Admin;

/// <summary>
/// Admin Inventory Service Interface.
/// 
/// PHASE 6.3: ADMIN INVENTORY OPERATIONS
/// 
/// ARCHITECTURAL RULES (STRICT):
/// - PostgreSQL is the ONLY source of truth for inventory
/// - Tenant isolation via Warehouse.TenantId and Product.TenantId
/// - Stock adjustments are atomic and auditable
/// - StockMovements are immutable audit records
/// - Idempotency via X-Request-Id
/// </summary>
public interface IAdminInventoryService
{
    /// <summary>
    /// Lists inventory (warehouse stock) with pagination and filtering.
    /// 
    /// Filters:
    /// - WarehouseId (optional)
    /// - ProductId (optional)
    /// - LowStockOnly (optional)
    /// - Search (optional, product name/SKU)
    /// </summary>
    Task<AdminPagedResult<AdminInventoryListItemDto>> ListInventoryAsync(
        Guid tenantId,
        AdminInventoryListQuery query,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adjusts stock for a product in a warehouse.
    /// 
    /// Rules:
    /// - Adjustment must be non-zero
    /// - Resulting quantity must not be negative
    /// - Creates immutable StockMovement record
    /// - Emits StockAdjustedEvent
    /// - Idempotent via requestId
    /// </summary>
    Task<AdminStockAdjustmentResponse> AdjustStockAsync(
        Guid tenantId,
        AdminStockAdjustmentRequest request,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists stock movements (audit log) with pagination and filtering.
    /// 
    /// Filters:
    /// - WarehouseId (optional)
    /// - ProductId (optional)
    /// - MovementType (optional)
    /// - FromDate/ToDate (optional)
    /// </summary>
    Task<AdminPagedResult<AdminStockMovementDto>> ListMovementsAsync(
        Guid tenantId,
        AdminStockMovementListQuery query,
        string correlationId,
        CancellationToken cancellationToken = default);
}

#region Admin Inventory Exceptions

/// <summary>
/// Base exception for admin inventory operations.
/// </summary>
public class AdminInventoryException : Exception
{
    public string ErrorCode { get; }

    public AdminInventoryException(string errorCode, string message) 
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public AdminInventoryException(string errorCode, string message, Exception innerException) 
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Thrown when product is not found or not accessible.
/// HTTP 404
/// </summary>
public class AdminProductNotFoundException : AdminInventoryException
{
    public Guid ProductId { get; }

    public AdminProductNotFoundException(Guid productId) 
        : base("PRODUCT_NOT_FOUND", $"Product {productId} not found or not accessible")
    {
        ProductId = productId;
    }
}

/// <summary>
/// Thrown when warehouse is not found or not accessible.
/// HTTP 404
/// </summary>
public class AdminWarehouseNotFoundException : AdminInventoryException
{
    public Guid WarehouseId { get; }

    public AdminWarehouseNotFoundException(Guid warehouseId) 
        : base("WAREHOUSE_NOT_FOUND", $"Warehouse {warehouseId} not found or not accessible")
    {
        WarehouseId = warehouseId;
    }
}

/// <summary>
/// Thrown when adjustment is invalid.
/// HTTP 400
/// </summary>
public class InvalidAdjustmentException : AdminInventoryException
{
    public InvalidAdjustmentException(string message) 
        : base("INVALID_ADJUSTMENT", message)
    {
    }
}

/// <summary>
/// Thrown when adjustment would result in negative stock.
/// HTTP 400
/// </summary>
public class InsufficientStockException : AdminInventoryException
{
    public Guid ProductId { get; }
    public Guid WarehouseId { get; }
    public decimal CurrentQuantity { get; }
    public decimal RequestedAdjustment { get; }

    public InsufficientStockException(Guid productId, Guid warehouseId, decimal currentQuantity, decimal requestedAdjustment) 
        : base("INSUFFICIENT_STOCK", 
            $"Adjustment would result in negative stock. Current: {currentQuantity}, Requested: {requestedAdjustment}")
    {
        ProductId = productId;
        WarehouseId = warehouseId;
        CurrentQuantity = currentQuantity;
        RequestedAdjustment = requestedAdjustment;
    }
}

/// <summary>
/// Thrown when request has already been processed (idempotency).
/// HTTP 409
/// </summary>
public class DuplicateAdjustmentException : AdminInventoryException
{
    public string RequestId { get; }

    public DuplicateAdjustmentException(string requestId) 
        : base("DUPLICATE_REQUEST", $"Request {requestId} has already been processed")
    {
        RequestId = requestId;
    }
}

/// <summary>
/// Thrown when adjustment reason is missing.
/// HTTP 400
/// </summary>
public class AdjustmentReasonRequiredException : AdminInventoryException
{
    public AdjustmentReasonRequiredException() 
        : base("REASON_REQUIRED", "Adjustment reason is required")
    {
    }
}

#endregion
