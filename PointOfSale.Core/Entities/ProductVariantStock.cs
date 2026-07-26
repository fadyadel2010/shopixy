using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Stock quantity per warehouse per product variant.
/// 
/// PHASE 11.1 — VARIANT-LEVEL INVENTORY
/// 
/// This is the new source of truth for inventory tracking.
/// Each variant maintains separate stock levels per warehouse.
/// 
/// LEGACY COMPATIBILITY:
/// - WarehouseStock is a compatibility table only, not an inventory authority
/// - Operational reads and writes must derive stock from ProductVariantStock
/// </summary>
public class ProductVariantStock : BaseEntity, ITenantOwned, IProjectionVersioned
{
    /// <summary>
    /// The tenant that owns this stock record.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// The variant this stock belongs to.
    /// </summary>
    public Guid VariantId { get; set; }
    
    /// <summary>
    /// The warehouse where this stock is located.
    /// </summary>
    public Guid WarehouseId { get; set; }
    
    /// <summary>
    /// Current quantity in stock.
    /// </summary>
    public decimal Quantity { get; set; }
    
    /// <summary>
    /// Reserved quantity (e.g., for pending orders).
    /// </summary>
    public decimal ReservedQuantity { get; set; }

    /// <summary>
    /// Non-sellable stock physically on hand.
    /// </summary>
    public decimal DamagedQuantity { get; set; }
    
    /// <summary>
    /// Available sellable quantity. Quantity is the sellable bucket; damaged stock is tracked separately.
    /// </summary>
    public decimal AvailableQuantity => Quantity - ReservedQuantity;

    /// <summary>
    /// Total physical quantity = sellable + damaged.
    /// </summary>
    public decimal TotalQuantity => Quantity + DamagedQuantity;

    /// <summary>
    /// Monotonic source version used by downstream read models to reject stale stock projections.
    /// </summary>
    public long ProjectionMutationVersion { get; private set; }
    
    /// <summary>
    /// Advances the projection mutation version. Used by the persistence pipeline only.
    /// Enforces monotonicity - new version must be greater than current version.
    /// </summary>
    public void AdvanceProjectionMutationVersion(long newVersion)
    {
        if (newVersion <= ProjectionMutationVersion)
        {
            throw new InvalidOperationException(
                $"Cannot regress ProjectionMutationVersion from {ProjectionMutationVersion} to {newVersion}. " +
                "Projection mutation versions must be monotonically increasing.");
        }
        ProjectionMutationVersion = newVersion;
    }
    
    // Navigation properties
    public ProductVariant Variant { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
}
