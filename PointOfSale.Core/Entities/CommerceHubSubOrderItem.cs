using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class CommerceHubSubOrderItem : BaseEntity
{
    public Guid SubOrderId { get; set; }
    public CommerceHubSubOrder SubOrder { get; set; } = null!;
    public Guid ListingId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public Guid CategoryId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string? VariantAttributesJson { get; set; }
    public string? ImageUrl { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>
    /// T-08: Immutable merchandise-cost authority for this marketplace line.
    /// Frozen from tenant-scoped ProductVariant.CostPrice (moving weighted average)
    /// at checkout commit. Never populated for pre-remediation rows (they remain
    /// cost-unknown historical data and must NOT be backfilled).
    /// Guarded immutable after creation by the CommerceHub order-lifecycle boundary.
    /// </summary>
    public decimal UnitCostSnapshot { get; set; }
    /// <summary>
    /// T-08: UnitCostSnapshot × Quantity at checkout commit. Reversals must reuse
    /// this frozen basis, never live ProductVariant.CostPrice.
    /// </summary>
    public decimal CostAmountSnapshot { get; set; }
    /// <summary>
    /// T-08: When the cost snapshot was captured. Null means cost-unknown
    /// (pre-remediation historical row): COGS consumers must fail closed.
    /// </summary>
    public DateTime? CostSnapshotCapturedAtUtc { get; set; }
    public decimal PlatformCommissionRate { get; set; }
    public decimal PlatformCommissionAmount { get; set; }
    public decimal FinalPlatformCommissionAmount { get; set; }
    public decimal PromoDiscountAmount { get; set; }
    public decimal Total { get; set; }
    public CommerceHubOrderItemStatus Status { get; set; } = CommerceHubOrderItemStatus.Pending;
    public DateTime? DeliveredAt { get; set; }
}
