using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Stock quantity per warehouse per product
/// </summary>
public class WarehouseStock : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this stock record.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    
    /// <summary>
    /// Current quantity in stock
    /// </summary>
    public decimal Quantity { get; set; }
    
    /// <summary>
    /// Reserved quantity (e.g., for pending orders)
    /// </summary>
    public decimal ReservedQuantity { get; set; }
    
    /// <summary>
    /// Available quantity = Quantity - ReservedQuantity
    /// </summary>
    public decimal AvailableQuantity => Quantity - ReservedQuantity;
    
    // Navigation properties
    public Warehouse Warehouse { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public ProductVariant? ProductVariant { get; set; }
}
