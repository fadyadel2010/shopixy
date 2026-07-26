using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class CommerceHubRefundRequest : BaseEntity
{
    public Guid OrderId { get; set; }
    public CommerceHubMarketplaceOrder Order { get; set; } = null!;
    public Guid OrderItemId { get; set; }
    public CommerceHubSubOrderItem OrderItem { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public CommerceHubCustomer Customer { get; set; } = null!;
    public Guid VendorId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPriceSnapshot { get; set; }
    public decimal TotalAmount { get; set; }
    public RefundReasonCode ReasonCode { get; set; } = RefundReasonCode.Other;
    public string? ReasonText { get; set; }
    public string Reason { get; set; } = string.Empty;
    public RefundPayoutMethod? PayoutMethodSnapshot { get; set; }
    public string? PayoutAccountSnapshot { get; set; }
    public string? PayoutAccountMaskedSnapshot { get; set; }
    public string? PayoutAccountHolderNameSnapshot { get; set; }
    public Guid? ReturnRequestId { get; set; }
    public RefundExecutionTrigger RefundExecutionTrigger { get; set; } = RefundExecutionTrigger.OnPickup;
    public string? IdempotencyKey { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public DateTime? ProcessingStartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }
    public string? AdminNote { get; set; }
    public string? FailureReason { get; set; }
    public int ExecutionAttempts { get; set; }
    public CommerceHubRefundRequestStatus Status { get; set; } = CommerceHubRefundRequestStatus.Requested;
    public CommerceHubReturnRequest? ReturnRequest { get; set; }
    public ICollection<CommerceHubRefundEvidenceMedia> EvidenceMedia { get; set; } =
        new List<CommerceHubRefundEvidenceMedia>();
}
