using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class CommerceHubPayment : BaseEntity
{
    public Guid OrderId { get; set; }
    public CommerceHubMarketplaceOrder Order { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public CommerceHubPaymentRecordStatus Status { get; set; } = CommerceHubPaymentRecordStatus.Pending;
    public PaymentProvider Provider { get; set; } = PaymentProvider.Paymob;
    public string ProviderReference { get; set; } = string.Empty;
    public string? ProviderIntentId { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? PaymentToken { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal ProvisionalRefundedAmount { get; set; }
    public string? FailureReason { get; set; }
    public bool IsProcessed { get; set; }
    public bool NeedsManualCurrencyReview { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    public ICollection<CommerceHubPaymentItem> Items { get; set; } = new List<CommerceHubPaymentItem>();
    public ICollection<CommerceHubPaymentLog> Logs { get; set; } = new List<CommerceHubPaymentLog>();
    public ICollection<CommerceHubPaymentAttempt> Attempts { get; set; } = new List<CommerceHubPaymentAttempt>();
    public ICollection<CommerceHubRefund> Refunds { get; set; } = new List<CommerceHubRefund>();
}
