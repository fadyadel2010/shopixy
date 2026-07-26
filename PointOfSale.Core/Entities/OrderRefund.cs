using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class OrderRefund : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public bool IsFullRefund { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public bool IsPostSettlementRefund { get; set; }
    public Guid? SettlementBatchId { get; set; }
    public string? SettlementType { get; set; }
    public string? SettlementId { get; set; }
    public string? PayoutBatchId { get; set; }
    public DateTime? SettledAtUtc { get; set; }
    public FinancialPostingStatus FinancialPostingStatus { get; set; }
    public DateTime? FinancialPostingPendingAtUtc { get; set; }
    public DateTime? FinancialPostedAtUtc { get; set; }
    public Guid? FinancialPostingJournalId { get; set; }
    public string? FinancialPostingFailureReason { get; set; }
    public DateTime RefundedAtUtc { get; set; }
    public ICollection<OrderRefundLine> Lines { get; set; } = new List<OrderRefundLine>();
}
