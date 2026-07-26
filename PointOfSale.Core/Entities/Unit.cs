using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Unit of measure (e.g., Piece, Carton, Kg, Liter)
/// </summary>
public class Unit : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this unit.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Unit name (e.g., "Piece", "Carton", "Kilogram")
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Short symbol (e.g., "pc", "ctn", "kg")
    /// </summary>
    public string? Symbol { get; set; }
    
    /// <summary>
    /// Description of the unit
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Whether this unit is active and can be used
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public ICollection<ProductUnit> ProductUnits { get; set; } = new List<ProductUnit>();
}
