using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.CommerceHub;

public sealed class CommerceHubReturnDetailsDto
{
    public Guid ReturnId { get; init; }
    public Guid RefundId { get; init; }
    public Guid OrderItemId { get; init; }
    public Guid CustomerId { get; init; }
    public Guid VendorId { get; init; }
    public CommerceHubReturnStatus Status { get; init; }
    public string StatusDisplayName { get; init; } = string.Empty;
    public RefundExecutionTrigger RefundExecutionTrigger { get; init; }
    public DateTime? PickupScheduledAt { get; init; }
    public DateTime? PickedUpAt { get; init; }
    public DateTime? InTransitAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public DateTime? ConfirmedAt { get; init; }
    public string? TrackingNumber { get; init; }
    public string? CourierName { get; init; }
    public string? Notes { get; init; }
    public int RequestedQuantity { get; init; }
    public int ReceivedResellableQuantity { get; init; }
    public int ReceivedDamagedQuantity { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public bool CanSchedulePickup { get; init; }
    public bool CanMarkPickedUp { get; init; }
    public bool CanMarkInTransit { get; init; }
    public bool CanMarkDelivered { get; init; }
    public bool CanMarkFailed { get; init; }
    public bool CanConfirmReceipt { get; init; }
    public bool CanOverrideStatus { get; init; }
    public IReadOnlyList<CommerceHubReturnStatus> AllowedOverrideStatuses { get; init; } = Array.Empty<CommerceHubReturnStatus>();
    public bool IsExecutionTriggerReached { get; init; }

    // SLA Tracking
    public DateTime? EscalatedAtUtc { get; init; }
    public int EscalationLevel { get; init; }
    public DateTime? ExpectedCompletionAtUtc { get; init; }
}

public sealed class CustomerReturnSummaryDto
{
    public Guid ReturnId { get; init; }
    public Guid RefundId { get; init; }
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public Guid OrderItemId { get; init; }
    public Guid VendorId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? VariantName { get; init; }
    public int Quantity { get; init; }
    public decimal RefundAmount { get; init; }
    public string Currency { get; init; } = "EGP";
    public CommerceHubRefundRequestStatus RefundStatus { get; init; }
    public CommerceHubReturnDetailsDto ReturnRequest { get; init; } = new();
}

public sealed class CustomerReturnDetailsDto
{
    public Guid ReturnId { get; init; }
    public Guid RefundId { get; init; }
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public Guid OrderItemId { get; init; }
    public Guid VendorId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? VariantName { get; init; }
    public int Quantity { get; init; }
    public decimal RefundAmount { get; init; }
    public string Currency { get; init; } = "EGP";
    public CommerceHubRefundRequestStatus RefundStatus { get; init; }
    public string RefundReason { get; init; } = string.Empty;
    public CommerceHubReturnDetailsDto ReturnRequest { get; init; } = new();
}
