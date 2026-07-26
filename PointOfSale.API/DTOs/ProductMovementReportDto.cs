namespace PointOfSale.API.DTOs;

/// <summary>
/// Product Movement Report Response
/// </summary>
public class ProductMovementReportDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductSku { get; set; } = string.Empty;
    public string? WarehouseName { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public List<ProductMovementItemDto> Movements { get; set; } = new();
}

/// <summary>
/// Individual movement item
/// </summary>
public class ProductMovementItemDto
{
    public DateTime Date { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public decimal QuantityInBaseUnit { get; set; }
    public decimal RunningBalance { get; set; }
    public decimal? Price { get; set; }
    public decimal? TotalValue { get; set; }
    public string? WarehouseName { get; set; }
    public string? ToWarehouseName { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Request parameters for product movement report
/// </summary>
public class ProductMovementRequestDto
{
    public Guid ProductId { get; set; }
    public Guid? WarehouseId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
