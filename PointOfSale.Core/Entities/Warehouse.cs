using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Warehouse/location entity for multi-warehouse inventory tracking
/// </summary>
public class Warehouse : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this warehouse.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    
    // Navigation properties
    public ICollection<WarehouseStock> Stock { get; set; } = new List<WarehouseStock>();
    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
}
