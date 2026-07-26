using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class PosCart : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CashierId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? CustomerId { get; set; }
    public PosCartStatus Status { get; set; } = PosCartStatus.Active;
    public int Version { get; set; } = 1;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? HeldAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? ExpiredAtUtc { get; set; }
    public DateTime? RecoveredAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? AbandonedAtUtc { get; set; }
    public Guid? CompletedSaleId { get; set; }
    public string? CancellationReason { get; set; }
    public string? RecoveryReason { get; set; }
    public uint RowVersion { get; set; }

    public Warehouse Warehouse { get; set; } = null!;
    public Customer? Customer { get; set; }
    public User Cashier { get; set; } = null!;
    public ICollection<PosCartItem> Items { get; set; } = new List<PosCartItem>();
}
