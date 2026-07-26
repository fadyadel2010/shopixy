using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Represents a physical cash register or cashbox
/// </summary>
public class Cashbox : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this cashbox.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    public string Name { get; set; } = string.Empty;
    public Guid? WarehouseId { get; set; }
    
    /// <summary>
    /// Legacy shadow balance field.
    /// DO NOT use CurrentBalance - use ledger.
    /// </summary>
    [Obsolete("Legacy shadow balance field. DO NOT use CurrentBalance; use ledger APIs instead.", false)]
    public decimal CurrentBalance { get; set; }
    
    /// <summary>
    /// Whether cashbox is currently open for transactions
    /// </summary>
    public bool IsActive { get; set; }
    
    /// <summary>
    /// When cashbox was last opened
    /// </summary>
    public DateTime? OpenedAt { get; set; }
    
    /// <summary>
    /// User who opened the cashbox
    /// </summary>
    public Guid? OpenedByUserId { get; set; }
    
    /// <summary>
    /// Starting balance for current session
    /// </summary>
    public decimal OpeningBalance { get; set; }
    
    /// <summary>
    /// Legacy shadow balance field.
    /// DO NOT use ExpectedBalance - use ledger.
    /// </summary>
    [Obsolete("Legacy shadow balance field. DO NOT use ExpectedBalance; use ledger APIs instead.", false)]
    public decimal ExpectedBalance { get; set; }
    
    public string? Description { get; set; }

    /// <summary>
    /// Initial opening balance amount when setting up the cash register
    /// </summary>
    public decimal? OpeningBalanceAmount { get; set; }

    /// <summary>
    /// Date of the initial opening balance
    /// </summary>
    public DateTime? OpeningBalanceDate { get; set; }
    
    // Navigation properties
    public Warehouse Warehouse { get; set; } = null!;
    public User? OpenedByUser { get; set; }
    public ICollection<CashboxTransaction> Transactions { get; set; } = new List<CashboxTransaction>();
}
