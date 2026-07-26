using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class StockTransfer : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this stock transfer.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    public string TransferNumber { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    
    public Guid FromWarehouseId { get; set; }
    public Guid ToWarehouseId { get; set; }
    
    public TransferStatus Status { get; set; }
    
    public string? Notes { get; set; }
    
    // Approval tracking
    public Guid? RequestedById { get; set; }
    public Guid? ApprovedById { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovalNotes { get; set; }
    
    public Guid? CompletedById { get; set; }
    public DateTime? CompletedAt { get; set; }
    
    // Navigation properties
    public Warehouse FromWarehouse { get; set; } = null!;
    public Warehouse ToWarehouse { get; set; } = null!;
    public User? RequestedBy { get; set; }
    public User? ApprovedBy { get; set; }
    public User? CompletedBy { get; set; }
    public ICollection<StockTransferItem> Items { get; set; } = new List<StockTransferItem>();
}
