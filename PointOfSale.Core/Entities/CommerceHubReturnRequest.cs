using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class CommerceHubReturnRequest : BaseEntity
{
    public Guid RefundId { get; set; }
    public CommerceHubRefundRequest Refund { get; set; } = null!;
    public Guid OrderItemId { get; set; }
    public CommerceHubSubOrderItem OrderItem { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public CommerceHubCustomer Customer { get; set; } = null!;
    public Guid VendorId { get; set; }
    public CommerceHubReturnStatus Status { get; set; } = CommerceHubReturnStatus.Pending;
    public DateTime? PickupScheduledAt { get; set; }
    public DateTime? PickedUpAt { get; set; }
    public DateTime? InTransitAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? TrackingNumber { get; set; }
    public string? CourierName { get; set; }
    public string? Notes { get; set; }
    public int RequestedQuantity { get; set; }
    public int ReceivedResellableQuantity { get; set; }
    public int ReceivedDamagedQuantity { get; set; }

    // SLA Tracking (Audit Improvement)
    public DateTime? EscalatedAtUtc { get; set; }
    public int EscalationLevel { get; set; }
    public string? EscalationReason { get; set; }
    public DateTime? ExpectedCompletionAtUtc { get; set; }
}
