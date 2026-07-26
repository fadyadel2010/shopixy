using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class CommerceHubRefund : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid SubOrderId { get; set; }
    public CommerceHubSubOrder SubOrder { get; set; } = null!;
    public Guid? RefundRequestId { get; set; }
    public Guid? OrderItemId { get; set; }
    public int? Quantity { get; set; }
    public Guid? PaymentId { get; set; }
    public CommerceHubPayment? Payment { get; set; }
    public decimal Amount { get; set; }
    public decimal CommissionReversalAmount { get; set; }
    public decimal PromoDiscountReversalAmount { get; set; }
    public decimal VendorEarningReversalAmount { get; set; }
    public string Currency { get; set; } = "EGP";
    public CommerceHubRefundStatus Status { get; set; } = CommerceHubRefundStatus.Pending;
    public string Reason { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? GatewayStatus { get; set; }
    public string? FailureReason { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public bool IsFullRefund { get; set; }
    public bool IsPostPayoutAdjustment { get; set; }
    public DateTime? ProcessingStartedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
}
