using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.Admin;

/// <summary>
/// Admin inventory list item DTO.
/// Used for paginated inventory lists in admin panel.
/// 
/// PHASE 11.5: VARIANT-CENTRIC INVENTORY
/// Inventory is Variant + Warehouse ONLY.
/// Products never hold stock directly.
/// </summary>
public class AdminInventoryListItemDto
{
    // PHASE 11.5: Variant is the stock unit, not Product
    
    /// <summary>
    /// Variant ID - the actual stock unit.
    /// </summary>
    public Guid VariantId { get; set; }
    
    /// <summary>
    /// Variant display name (e.g., "Jacket H.M — Red / XL").
    /// </summary>
    public string VariantName { get; set; } = string.Empty;
    
    /// <summary>
    /// Variant SKU.
    /// </summary>
    public string? VariantSKU { get; set; }
    
    /// <summary>
    /// Whether this is the default variant for a simple product.
    /// </summary>
    public bool IsDefaultVariant { get; set; }
    
    /// <summary>
    /// Product ID (parent product).
    /// </summary>
    public Guid ProductId { get; set; }
    
    /// <summary>
    /// Product name (parent product).
    /// </summary>
    public string ProductName { get; set; } = string.Empty;
    
    /// <summary>
    /// Warehouse ID.
    /// </summary>
    public Guid WarehouseId { get; set; }
    
    /// <summary>
    /// Warehouse name.
    /// </summary>
    public string WarehouseName { get; set; } = string.Empty;
    
    /// <summary>
    /// Current quantity in stock.
    /// </summary>
    public decimal Quantity { get; set; }
    
    /// <summary>
    /// Reserved quantity (for pending orders).
    /// </summary>
    public decimal ReservedQuantity { get; set; }
    
    /// <summary>
    /// Available quantity (Quantity - Reserved).
    /// </summary>
    public decimal AvailableQuantity { get; set; }
    
    /// <summary>
    /// Minimum stock level threshold.
    /// </summary>
    public decimal MinimumStockLevel { get; set; }
    
    /// <summary>
    /// Whether stock is below minimum level.
    /// </summary>
    public bool IsLowStock { get; set; }
}

/// <summary>
/// Admin stock movement item DTO.
/// Used for viewing stock movement history.
/// Phase 6.3: Admin Inventory Operations
/// </summary>
public class AdminStockMovementDto
{
    /// <summary>
    /// Movement ID.
    /// </summary>
    public Guid MovementId { get; set; }
    
    /// <summary>
    /// Product ID.
    /// </summary>
    public Guid ProductId { get; set; }
    
    /// <summary>
    /// Product name.
    /// </summary>
    public string ProductName { get; set; } = string.Empty;
    
    /// <summary>
    /// Product SKU.
    /// </summary>
    public string? SKU { get; set; }
    
    /// <summary>
    /// Warehouse ID.
    /// </summary>
    public Guid WarehouseId { get; set; }
    
    /// <summary>
    /// Warehouse name.
    /// </summary>
    public string WarehouseName { get; set; } = string.Empty;
    
    /// <summary>
    /// Movement type.
    /// </summary>
    public StockMovementType MovementType { get; set; }
    
    /// <summary>
    /// Quantity change (positive or negative).
    /// </summary>
    public decimal QuantityChange { get; set; }
    
    /// <summary>
    /// Quantity after this movement.
    /// </summary>
    public decimal QuantityAfter { get; set; }
    
    /// <summary>
    /// Reference number (e.g., invoice number).
    /// </summary>
    public string? ReferenceNumber { get; set; }
    
    /// <summary>
    /// Notes/reason for movement.
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Whether this is an opening balance entry.
    /// </summary>
    public bool IsOpeningBalance { get; set; }
    
    /// <summary>
    /// Who created this movement.
    /// </summary>
    public Guid? CreatedBy { get; set; }
    
    /// <summary>
    /// When this movement was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Request DTO for stock adjustment.
/// 
/// PHASE 11.5: Inventory screens MUST operate on VariantId.
/// Products never hold stock directly.
/// </summary>
public class AdminStockAdjustmentRequest
{
    /// <summary>
    /// Variant ID - the stock unit to adjust.
    /// PHASE 11.5: This is the primary identifier, not ProductId.
    /// </summary>
    public Guid VariantId { get; set; }
    
    /// <summary>
    /// Product ID (legacy, optional - can be derived from Variant).
    /// </summary>
    public Guid? ProductId { get; set; }
    
    /// <summary>
    /// Warehouse ID.
    /// </summary>
    public Guid WarehouseId { get; set; }
    
    /// <summary>
    /// Adjustment amount (positive = increase, negative = decrease).
    /// Must be non-zero.
    /// </summary>
    public decimal Adjustment { get; set; }
    
    /// <summary>
    /// Reason for adjustment (required).
    /// </summary>
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional additional notes.
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Response DTO for stock adjustment.
/// </summary>
public class AdminStockAdjustmentResponse
{
    /// <summary>
    /// Movement ID created for this adjustment.
    /// </summary>
    public Guid MovementId { get; set; }
    
    /// <summary>
    /// Product ID.
    /// </summary>
    public Guid ProductId { get; set; }
    
    /// <summary>
    /// Product SKU.
    /// </summary>
    public string? SKU { get; set; }
    
    /// <summary>
    /// Warehouse ID.
    /// </summary>
    public Guid WarehouseId { get; set; }
    
    /// <summary>
    /// Warehouse name.
    /// </summary>
    public string WarehouseName { get; set; } = string.Empty;
    
    /// <summary>
    /// Quantity before adjustment.
    /// </summary>
    public decimal OldQuantity { get; set; }
    
    /// <summary>
    /// Quantity after adjustment.
    /// </summary>
    public decimal NewQuantity { get; set; }
    
    /// <summary>
    /// Adjustment amount applied.
    /// </summary>
    public decimal Adjustment { get; set; }
    
    /// <summary>
    /// When adjustment was applied.
    /// </summary>
    public DateTime AdjustedAtUtc { get; set; }
}

/// <summary>
/// Query parameters for admin inventory list.
/// 
/// PHASE 11.5: Supports filtering by VariantId.
/// </summary>
public class AdminInventoryListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public Guid? WarehouseId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? VariantId { get; set; }  // PHASE 11.5: Filter by specific variant
    public bool? LowStockOnly { get; set; }
    public string? Search { get; set; }

    public const int MinPage = 1;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public string? Validate()
    {
        if (Page < MinPage)
            return $"Page must be at least {MinPage}";
        
        if (PageSize < MinPageSize || PageSize > MaxPageSize)
            return $"PageSize must be between {MinPageSize} and {MaxPageSize}";

        return null;
    }
}

/// <summary>
/// Query parameters for stock movements list.
/// </summary>
public class AdminStockMovementListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public Guid? WarehouseId { get; set; }
    public Guid? ProductId { get; set; }
    public StockMovementType? MovementType { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    public const int MinPage = 1;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public string? Validate()
    {
        if (Page < MinPage)
            return $"Page must be at least {MinPage}";
        
        if (PageSize < MinPageSize || PageSize > MaxPageSize)
            return $"PageSize must be between {MinPageSize} and {MaxPageSize}";

        if (FromDate.HasValue && ToDate.HasValue && FromDate > ToDate)
            return "FromDate cannot be after ToDate";

        return null;
    }
}

/// <summary>
/// Admin Inventory API error response.
/// </summary>
public class AdminInventoryApiErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TraceId { get; set; }

    public AdminInventoryApiErrorResponse() { }

    public AdminInventoryApiErrorResponse(string code, string message, string? traceId = null)
    {
        Code = code;
        Message = message;
        TraceId = traceId;
    }

    // Factory methods
    public static AdminInventoryApiErrorResponse TenantHeaderMissing(string? traceId = null) =>
        new("TENANT_HEADER_MISSING", "X-Tenant-Id header is required", traceId);

    public static AdminInventoryApiErrorResponse TenantHeaderInvalid(string? traceId = null) =>
        new("TENANT_HEADER_INVALID", "X-Tenant-Id must be a valid GUID", traceId);

    public static AdminInventoryApiErrorResponse ProductNotFound(string? traceId = null) =>
        new("PRODUCT_NOT_FOUND", "Product not found or not accessible", traceId);

    public static AdminInventoryApiErrorResponse WarehouseNotFound(string? traceId = null) =>
        new("WAREHOUSE_NOT_FOUND", "Warehouse not found or not accessible", traceId);

    public static AdminInventoryApiErrorResponse InvalidAdjustment(string message, string? traceId = null) =>
        new("INVALID_ADJUSTMENT", message, traceId);

    public static AdminInventoryApiErrorResponse InsufficientStock(string? traceId = null) =>
        new("INSUFFICIENT_STOCK", "Adjustment would result in negative stock", traceId);

    public static AdminInventoryApiErrorResponse DuplicateRequest(string? traceId = null) =>
        new("DUPLICATE_REQUEST", "This request has already been processed", traceId);

    public static AdminInventoryApiErrorResponse InternalError(string? traceId = null) =>
        new("INTERNAL_ERROR", "An internal error occurred", traceId);
}
