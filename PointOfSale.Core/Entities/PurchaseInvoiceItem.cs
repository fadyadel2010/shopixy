namespace PointOfSale.Core.Entities;

/// <summary>
/// Line item in purchase invoice
/// </summary>
public class PurchaseInvoiceItem : BaseEntity
{
    public Guid PurchaseInvoiceId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    
    /// <summary>
    /// Unit used for this transaction line (nullable for backward compatibility)
    /// </summary>
    public Guid? UnitId { get; set; }
    
    /// <summary>
    /// Quantity entered in the selected unit
    /// </summary>
    public decimal Quantity { get; set; }
    
    /// <summary>
    /// Conversion factor at the time of transaction (snapshot for historical accuracy)
    /// Number of base units per selected unit
    /// </summary>
    public decimal ConversionFactor { get; set; } = 1;
    
    /// <summary>
    /// Calculated quantity in base units = Quantity * ConversionFactor
    /// This is what gets added to stock
    /// </summary>
    public decimal BaseQuantity { get; set; }
    
    /// <summary>
    /// Unit cost price
    /// </summary>
    public decimal UnitPrice { get; set; }
    
    /// <summary>
    /// Discount per item
    /// </summary>
    public decimal DiscountAmount { get; set; }
    
    /// <summary>
    /// Tax rate for this item
    /// </summary>
    public decimal TaxRate { get; set; }
    
    /// <summary>
    /// Tax amount for this line
    /// </summary>
    public decimal TaxAmount { get; set; }
    
    /// <summary>
    /// Total for this line = (Quantity * UnitPrice) - Discount + Tax
    /// </summary>
    public decimal TotalAmount { get; set; }
    
    // Navigation properties
    public PurchaseInvoice PurchaseInvoice { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public ProductVariant? ProductVariant { get; set; }
    public Unit? Unit { get; set; }
}
