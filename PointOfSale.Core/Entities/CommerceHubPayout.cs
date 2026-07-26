using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class CommerceHubPayout : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid? PayoutMethodId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public CommerceHubPayoutStatus Status { get; set; } = CommerceHubPayoutStatus.Pending;
    public CommerceHubPayoutMethod Method { get; set; } = CommerceHubPayoutMethod.Manual;
    public string? DestinationValueSnapshot { get; set; }
    public string? Reference { get; set; }
    public string? ExternalReferenceId { get; set; }
    public string? BankTransactionId { get; set; }
    public string? ExternalTransferIdempotencyKey { get; set; }
    public string? ExternalTransferHandoffToken { get; set; }
    public DateTime? ExternalTransferHandoffCommittedAtUtc { get; set; }
    public string? ProcessedBy { get; set; }
    public string? FailureReason { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? CompletionEvidenceFingerprint { get; set; }

    public ICollection<CommerceHubPayoutItem> Items { get; set; } = new List<CommerceHubPayoutItem>();
}
