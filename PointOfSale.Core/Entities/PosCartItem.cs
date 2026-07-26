namespace PointOfSale.Core.Entities;

public class PosCartItem : BaseEntity
{
    public Guid PosCartId { get; set; }
    public Guid ProductId { get; set; }
    public Guid ProductVariantId { get; set; }
    public Guid? UnitId { get; set; }
    public decimal Quantity { get; set; }
    public decimal ConversionFactor { get; set; } = 1m;
    public decimal BaseQuantity { get; set; }
    public decimal UnitPriceSnapshot { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRateSnapshot { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal CostPriceSnapshot { get; set; }
    public decimal CostAmountSnapshot { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string? VariantSkuSnapshot { get; set; }
    public int LineNumber { get; set; }

    public PosCart PosCart { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public ProductVariant ProductVariant { get; set; } = null!;
    public Unit? Unit { get; set; }
}
