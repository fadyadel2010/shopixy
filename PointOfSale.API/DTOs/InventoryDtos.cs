using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs;

public class StockAdjustmentDto
{
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public decimal QuantityChange { get; set; }
    public StockMovementType Type { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class StockTransferDto
{
    public Guid SourceWarehouseId { get; set; }
    public Guid TargetWarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public decimal Quantity { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public record SaveOpeningStockRequest
{
    public Guid WarehouseId { get; set; }
    public List<OpeningStockItem> Items { get; set; } = new();
}

public record OpeningStockItem
{
    public Guid VariantId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? CostPrice { get; set; }
    public string? Notes { get; set; }
}

public sealed record OpeningStockSaveResult(
    string Message,
    IReadOnlyList<OpeningStockItemResult> Results);

public sealed record OpeningStockItemResult(
    Guid VariantId,
    bool Success,
    string Message);
