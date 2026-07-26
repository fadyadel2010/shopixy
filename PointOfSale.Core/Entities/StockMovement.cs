using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Stock movement audit log
/// </summary>
public class StockMovement : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this movement record.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    
    public StockMovementType MovementType { get; set; }
    
    /// <summary>
    /// Unit used for this movement (nullable for backward compatibility)
    /// </summary>
    public Guid? UnitId { get; set; }
    
    /// <summary>
    /// Conversion factor at the time of movement (snapshot)
    /// </summary>
    public decimal ConversionFactor { get; set; } = 1;
    
    /// <summary>
    /// Quantity change (positive for increase, negative for decrease)
    /// </summary>
    public decimal QuantityChange { get; set; }

    /// <summary>
    /// Reserved quantity change for this ledger entry.
    /// </summary>
    public decimal ReservedQuantityChange { get; set; }

    /// <summary>
    /// Damaged quantity change for this ledger entry.
    /// </summary>
    public decimal DamagedQuantityChange { get; set; }
    
    /// <summary>
    /// Quantity after this movement
    /// </summary>
    public decimal QuantityAfter { get; set; }

    /// <summary>
    /// Available quantity after this movement.
    /// </summary>
    public decimal AvailableAfter { get; set; }

    /// <summary>
    /// Reserved quantity after this movement.
    /// </summary>
    public decimal ReservedAfter { get; set; }

    /// <summary>
    /// Damaged quantity after this movement.
    /// </summary>
    public decimal DamagedAfter { get; set; }
    
    /// <summary>
    /// Reference to source document (invoice ID, adjustment ID, etc.)
    /// </summary>
    public Guid? ReferenceId { get; set; }

    public Guid? ReferenceLineId { get; set; }

    public Guid? ReservationId { get; set; }

    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public Guid? InventoryEventId { get; set; }
    public InventoryEventType? InventoryEventType { get; set; }
    public string? ReferenceType { get; set; }
    public string? CorrelationId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? Source { get; set; }
    
    /// <summary>
    /// Price per unit (for opening balance and valuation)
    /// </summary>
    public decimal? Price { get; set; }
    
    /// <summary>
    /// Flag to mark opening balance entries (cannot be edited)
    /// </summary>
    public bool IsOpeningBalance { get; set; }
    
    // For transfers
    public Guid? ToWarehouseId { get; set; }
    
    // Navigation properties
    public Warehouse Warehouse { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public ProductVariant? ProductVariant { get; set; }
    public Warehouse? ToWarehouse { get; set; }
    public Unit? Unit { get; set; }
}
