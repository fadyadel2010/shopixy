using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Durable settlement batch header used to make payment reconciliation auditable.
/// Each batch is tenant-owned and uniquely identified by settlement type + settlement id.
/// </summary>
public class PaymentSettlementBatch : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public string SettlementType { get; set; } = string.Empty;

    public string SettlementId { get; set; } = string.Empty;

    public string? Provider { get; set; }

    public string? PayoutBatchId { get; set; }

    public decimal GrossAmount { get; set; }

    public decimal Fees { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string? BankReference { get; set; }

    public int OrderCount { get; set; }

    public DateTime SettledAtUtc { get; set; }

    public List<PaymentSettlementOrder> Orders { get; set; } = new();
    
    public List<OrderRefund> OrderRefunds { get; set; } = new();
}
