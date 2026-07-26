using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.Customers;

public class CustomerOrdersQueryDto
{
    public const int MinPage = 1;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 20;

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;

    public string? Validate()
    {
        if (Page < MinPage)
        {
            return "Page must be at least 1.";
        }

        if (PageSize < MinPageSize || PageSize > MaxPageSize)
        {
            return $"Page size must be between {MinPageSize} and {MaxPageSize}.";
        }

        return null;
    }
}

public class CustomerOrderSummaryDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "EGP";
    public OrderStatus Status { get; set; }
    public List<CustomerOrderVendorDto> SubOrders { get; set; } = new();
}

public class CustomerOrderDetailsDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Currency { get; set; } = "EGP";
    public decimal Subtotal { get; set; }
    public decimal ShippingTotal { get; set; }
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public string PaymentMethodCode { get; set; } = string.Empty;
    public string PaymentMethodName { get; set; } = string.Empty;
    public string? PaymentReference { get; set; }
    public DateTime? PaidAt { get; set; }
    public bool CanRetryPayment { get; set; }
    public string? PaymentRecoveryMessage { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Area { get; set; }
    public List<CustomerOrderVendorDto> SubOrders { get; set; } = new();
    public List<CustomerOrderTimelineEntryDto> Timeline { get; set; } = new();
}

public class CustomerOrderVendorDto
{
    public Guid SubOrderId { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public List<CustomerOrderItemDto> Items { get; set; } = new();
}

public class CustomerOrderItemDto
{
    public Guid OrderItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Variant { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Total { get; set; }
    public CommerceHubOrderItemStatus Status { get; set; }
    public bool CanRefund { get; set; }
    public int MaxRefundableQuantity { get; set; }
    public string? RefundDisabledReason { get; set; }
    public List<CustomerOrderItemExistingRefundDto> ExistingRefunds { get; set; } = new();
    public string? ImageUrl { get; set; }
}

public class CustomerOrderTimelineEntryDto
{
    public DateTime At { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? PreviousStatus { get; set; }
    public string? NewStatus { get; set; }
    public string? VendorName { get; set; }
}
