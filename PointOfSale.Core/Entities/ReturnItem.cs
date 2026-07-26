namespace PointOfSale.Core.Entities;

/// <summary>
/// Line item in return
/// </summary>
public class ReturnItem : BaseEntity
{
    public Guid ReturnId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    
    /// <summary>
    /// Unit used for this return line (nullable for backward compatibility)
    /// </summary>
    public Guid? UnitId { get; set; }
    
    /// <summary>
    /// Quantity returned in the selected unit
    /// </summary>
    public decimal Quantity { get; set; }
    
    /// <summary>
    /// Conversion factor at the time of return (snapshot for historical accuracy)
    /// </summary>
    public decimal ConversionFactor { get; set; } = 1;
    
    /// <summary>
    /// Calculated quantity in base units = Quantity * ConversionFactor
    /// </summary>
    public decimal BaseQuantity { get; set; }
    
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Immutable base-unit cost basis used for return COGS reversal.
    /// For sales returns this is prorated from the original sale line snapshot.
    /// </summary>
    public decimal? CostPriceSnapshot { get; set; }

    /// <summary>
    /// Immutable total cost basis for this return line.
    /// </summary>
    public decimal? CostAmountSnapshot { get; set; }
    
    public string? Reason { get; set; }
    
    // Navigation properties
    public Return Return { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public ProductVariant? ProductVariant { get; set; }
    public Unit? Unit { get; set; }
}
