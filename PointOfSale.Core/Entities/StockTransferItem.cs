namespace PointOfSale.Core.Entities;

public class StockTransferItem : BaseEntity
{
    public Guid StockTransferId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    
    public decimal Quantity { get; set; }
    
    public string? Notes { get; set; }
    
    // Navigation properties
    public StockTransfer StockTransfer { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public ProductVariant? ProductVariant { get; set; }
}
