using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class CommerceHubPaymentAttempt : BaseEntity
{
    public Guid PaymentId { get; set; }
    public CommerceHubPayment Payment { get; set; } = null!;
    public PaymentProvider Provider { get; set; } = PaymentProvider.Paymob;
    public int AttemptNumber { get; set; }
    public string ProviderReference { get; set; } = string.Empty;
    public string? ProviderIntentId { get; set; }
    public string? ProviderTransactionId { get; set; }
    public CommerceHubPaymentRecordStatus Status { get; set; } = CommerceHubPaymentRecordStatus.Pending;
    public string? FailureReason { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
