namespace PointOfSale.Core.Entities;

/// <summary>
/// Links a product to a unit with conversion factor and pricing.
/// The default ProductUnit is the canonical price authority for simple products.
/// </summary>
public class ProductUnit : BaseEntity
{
    public Guid ProductId { get; set; }
    public Guid UnitId { get; set; }
    
    /// <summary>
    /// Number of base units per this unit
    /// Example: If base unit is "Piece" and this is "Carton" containing 5 pieces, ConversionFactor = 5
    /// </summary>
    public decimal ConversionFactor { get; set; }
    
    /// <summary>
    /// Purchase price per this unit (nullable - may not be set)
    /// </summary>
    public decimal? PurchasePrice { get; set; }
    
    /// <summary>
    /// Selling price per this unit (nullable - may not be set)
    /// </summary>
    public decimal? SellingPrice { get; set; }
    
    /// <summary>
    /// Barcode specific to this unit (optional)
    /// Example: A carton might have a different barcode than individual pieces
    /// </summary>
    public string? Barcode { get; set; }
    
    /// <summary>
    /// Whether this is the default unit for display and transactions
    /// </summary>
    public bool IsDefault { get; set; }
    
    /// <summary>
    /// Whether this is the base unit (ConversionFactor should be 1)
    /// Each product must have exactly one base unit
    /// </summary>
    public bool IsBaseUnit { get; set; }
    
    // Navigation properties
    public Product Product { get; set; } = null!;
    public Unit Unit { get; set; } = null!;
}
