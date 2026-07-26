using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.Customers;

public class CustomerCreateRefundRequestDto
{
    public Guid OrderItemId { get; set; }
    public int Quantity { get; set; }
    public RefundReasonCode? ReasonCode { get; set; }
    public string? ReasonText { get; set; }
    public Guid? SavedPayoutDetailsId { get; set; }
    public CustomerRefundPayoutInputDto? PayoutDetails { get; set; }
    public List<string> MediaUrls { get; set; } = new();
}

public class CustomerRefundSummaryDto
{
    public Guid RefundId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid OrderItemId { get; set; }
    public Guid VendorId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceSnapshot { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public CommerceHubRefundRequestStatus Status { get; set; }
    public string StatusDisplayName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public RefundReasonCode ReasonCode { get; set; }
    public string? ReasonText { get; set; }
    public CustomerRefundPayoutSnapshotDto? Payout { get; set; }
    public CommerceHubReturnDetailsDto? ReturnRequest { get; set; }
    public List<string> MediaUrls { get; set; } = new();
    public List<RefundEvidenceMediaDto> Media { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CustomerOrderItemExistingRefundDto
{
    public Guid RefundId { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public CommerceHubRefundRequestStatus Status { get; set; }
    public string StatusDisplayName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public RefundReasonCode ReasonCode { get; set; }
    public string? ReasonText { get; set; }
    public CustomerRefundPayoutSnapshotDto? Payout { get; set; }
    public CommerceHubReturnDetailsDto? ReturnRequest { get; set; }
    public List<string> MediaUrls { get; set; } = new();
    public List<RefundEvidenceMediaDto> Media { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
