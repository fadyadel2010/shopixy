using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class CommerceHubRefundExecution : BaseEntity
{
    public Guid RefundId { get; set; }
    public CommerceHubRefundRequest Refund { get; set; } = null!;
    public RefundPayoutMethod Method { get; set; }
    public string AccountSnapshot { get; set; } = string.Empty;
    public string TransactionReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public Guid ExecutedBy { get; set; }
    public DateTime ExecutedAt { get; set; }
    public string? Notes { get; set; }
    public bool IsSuccessful { get; set; }
    public string? FailureReason { get; set; }
}
