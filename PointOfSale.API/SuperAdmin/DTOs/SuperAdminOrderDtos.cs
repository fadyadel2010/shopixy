using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.SuperAdmin.DTOs;

public enum SuperAdminOrderPaymentFilter
{
    Paid = 0,
    Unpaid = 1,
    Failed = 2
}

public enum SuperAdminOrdersSortField
{
    CreatedAt = 0,
    OrderNumber = 1,
    Customer = 2,
    Tenant = 3,
    TotalAmount = 4,
    Status = 5,
    PaymentStatus = 6,
    VendorCount = 7
}

public enum SuperAdminSortDirection
{
    Desc = 0,
    Asc = 1
}

public sealed class SuperAdminOrdersListQuery
{
    public const int MinPage = 1;
    public const int DefaultPage = 1;
    public const int MinPageSize = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = DefaultPage;
    public int PageSize { get; init; } = DefaultPageSize;
    public string? Search { get; init; }
    public OrderStatus? Status { get; init; }
    public SuperAdminOrderPaymentFilter? PaymentStatus { get; init; }
    public Guid? TenantId { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
    public SuperAdminOrdersSortField SortBy { get; init; } = SuperAdminOrdersSortField.CreatedAt;
    public SuperAdminSortDirection SortDirection { get; init; } = SuperAdminSortDirection.Desc;

    public string? NormalizedSearch =>
        string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();

    public string? Validate()
    {
        if (Page < MinPage)
        {
            return $"Page must be at least {MinPage}.";
        }

        if (PageSize < MinPageSize || PageSize > MaxPageSize)
        {
            return $"PageSize must be between {MinPageSize} and {MaxPageSize}.";
        }

        if (FromDate.HasValue && ToDate.HasValue && FromDate.Value.Date > ToDate.Value.Date)
        {
            return "FromDate cannot be after ToDate.";
        }

        return null;
    }
}

public sealed class SuperAdminOrdersListResponseDto
{
    public IReadOnlyList<SuperAdminOrderListItemDto> Orders { get; init; } =
        Array.Empty<SuperAdminOrderListItemDto>();
}

public sealed class SuperAdminOrderListItemDto
{
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public string Currency { get; init; } = "EGP";
    public OrderStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string TenantName { get; init; } = string.Empty;
    public int VendorCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public IReadOnlyList<SuperAdminOrderSubOrderSummaryDto> SubOrders { get; init; } =
        Array.Empty<SuperAdminOrderSubOrderSummaryDto>();
}

public sealed class SuperAdminOrderSubOrderSummaryDto
{
    public Guid SubOrderId { get; init; }
    public Guid VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "EGP";
    public OrderStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
}

public sealed class SuperAdminOrderDetailsDto
{
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string? CustomerPhone { get; init; }
    public string AddressLine { get; init; } = string.Empty;
    public string? City { get; init; }
    public string? Area { get; init; }
    public string? Notes { get; init; }
    public string Currency { get; init; } = "EGP";
    public decimal Subtotal { get; init; }
    public decimal ShippingTotal { get; init; }
    public decimal TotalAmount { get; init; }
    public OrderStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string PaymentMethodCode { get; init; } = string.Empty;
    public string PaymentMethodName { get; init; } = string.Empty;
    public string? PaymentReference { get; init; }
    public string? PaymentReferenceId { get; init; }
    public string? PaymentGateway { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? PaidAt { get; init; }
    public int VendorCount { get; init; }
    public string TenantName { get; init; } = string.Empty;
    public decimal PlatformCommissionTotal { get; init; }
    public decimal VendorEarningsTotal { get; init; }
    public string? EventStatus { get; init; }
    public IReadOnlyList<SuperAdminOrderPaymentDetailsDto> Payments { get; init; } =
        Array.Empty<SuperAdminOrderPaymentDetailsDto>();
    public IReadOnlyList<SuperAdminOrderSubOrderDetailsDto> SubOrders { get; init; } =
        Array.Empty<SuperAdminOrderSubOrderDetailsDto>();
    public IReadOnlyList<SuperAdminOrderItemDto> OrderItems { get; init; } =
        Array.Empty<SuperAdminOrderItemDto>();
}

public sealed class SuperAdminOrderPaymentDetailsDto
{
    public Guid PaymentId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "EGP";
    public CommerceHubPaymentRecordStatus Status { get; init; }
    public PaymentProvider Provider { get; init; }
    public string ProviderReference { get; init; } = string.Empty;
    public string? ProviderIntentId { get; init; }
    public string? ProviderTransactionId { get; init; }
    public string? FailureReason { get; init; }
    public bool IsProcessed { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ProcessedAtUtc { get; init; }
    public DateTime? PaidAtUtc { get; init; }
}

public sealed class SuperAdminOrderSubOrderDetailsDto
{
    public Guid SubOrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public Guid VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "EGP";
    public OrderStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public decimal PlatformCommissionAmount { get; init; }
    public decimal PlatformCommissionRate { get; init; }
    public decimal VendorEarningAmount { get; init; }
    public string? ShippingMethodName { get; init; }
    public string PaymentMethodName { get; init; } = string.Empty;
    public string? PaymentReferenceId { get; init; }
    public DateTime? PaidAt { get; init; }
    public int ItemCount { get; init; }
    public string? CommissionSnapshotJson { get; init; }
}

public sealed class SuperAdminOrderItemDto
{
    public Guid ItemId { get; init; }
    public Guid SubOrderId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string? VariantName { get; init; }
    public string? ImageUrl { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal TotalPrice { get; init; }
    public IReadOnlyList<SuperAdminOrderRefundEvidenceDto> Refunds { get; init; } =
        Array.Empty<SuperAdminOrderRefundEvidenceDto>();
}

public sealed class SuperAdminOrderRefundEvidenceDto
{
    public Guid RefundId { get; init; }
    public int Quantity { get; init; }
    public decimal Amount { get; init; }
    public CommerceHubRefundRequestStatus Status { get; init; }
    public string Reason { get; init; } = string.Empty;
    public RefundReasonCode ReasonCode { get; init; }
    public string? ReasonText { get; init; }
    public IReadOnlyList<string> MediaUrls { get; init; } = Array.Empty<string>();
    public IReadOnlyList<RefundEvidenceMediaDto> Media { get; init; } = Array.Empty<RefundEvidenceMediaDto>();
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed class SuperAdminOrdersStatsDto
{
    public DateTime WindowStartUtc { get; init; }
    public DateTime WindowEndUtc { get; init; }
    public DateTime AsOfUtc { get; init; }
    public string Currency { get; init; } = "EGP";
    public long TotalOrdersToday { get; init; }
    public long PendingOrders { get; init; }
    public long FailedPayments { get; init; }
    public decimal RevenueToday { get; init; }
}

public sealed class SuperAdminPagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}
