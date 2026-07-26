using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class CommerceHubVendorEarning : BaseEntity
{
    public Guid SubOrderId { get; set; }
    public CommerceHubSubOrder SubOrder { get; set; } = null!;
    public Guid? PaymentId { get; set; }
    public CommerceHubPayment? Payment { get; set; }
    public Guid? PayoutId { get; set; }
    public CommerceHubPayout? Payout { get; set; }
    public Guid? RefundId { get; set; }
    public CommerceHubRefund? Refund { get; set; }
    public Guid? OriginalEarningId { get; set; }
    public CommerceHubVendorEarning? OriginalEarning { get; set; }
    public Guid TenantId { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidOutAmount { get; set; }
    public decimal PayoutReservedAmount { get; set; }
    public CommerceHubVendorEarningStatus Status { get; set; } = CommerceHubVendorEarningStatus.Pending;
    public DateTime? AvailableAtUtc { get; set; }
    public DateTime? PaidOutAtUtc { get; set; }
    public DateTime? ReversedAtUtc { get; set; }
}
