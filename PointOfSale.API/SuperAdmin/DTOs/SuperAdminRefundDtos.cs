using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminRefundsListQuery
{
    public const int MinPage = 1;
    public const int DefaultPage = 1;
    public const int MinPageSize = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = DefaultPage;
    public int PageSize { get; init; } = DefaultPageSize;
    public CommerceHubRefundRequestStatus? Status { get; init; }
    public Guid? VendorId { get; init; }
    public Guid? OrderId { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }

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

public sealed class SuperAdminRefundsListResponseDto
{
    public IReadOnlyList<SuperAdminRefundListItemDto> Refunds { get; init; } =
        Array.Empty<SuperAdminRefundListItemDto>();
}

public sealed class SuperAdminRefundListItemDto
{
    public Guid RefundId { get; init; }
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public Guid VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "EGP";
    public CommerceHubRefundRequestStatus Status { get; init; }
    public string StatusDisplayName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public sealed class SuperAdminRefundDetailsDto
{
    public Guid RefundId { get; init; }
    public CommerceHubRefundRequestStatus Status { get; init; }
    public string StatusDisplayName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public RefundReasonCode ReasonCode { get; init; }
    public string? ReasonText { get; init; }
    public SuperAdminRefundPayoutInfoDto? Payout { get; init; }
    public IReadOnlyList<string> MediaUrls { get; init; } = Array.Empty<string>();
    public IReadOnlyList<RefundEvidenceMediaDto> Media { get; init; } = Array.Empty<RefundEvidenceMediaDto>();
    public string? AdminNote { get; init; }
    public string? FailureReason { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPriceSnapshot { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "EGP";
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? ReviewedAtUtc { get; init; }
    public DateTime? ApprovedAtUtc { get; init; }
    public DateTime? RejectedAtUtc { get; init; }
    public DateTime? ProcessingStartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public DateTime? FailedAtUtc { get; init; }
    public int ExecutionAttempts { get; init; }
    public bool CanApprove { get; init; }
    public bool CanReject { get; init; }
    public bool CanExecute { get; init; }
    public bool CanRetry { get; init; }
    public SuperAdminRefundOrderInfoDto Order { get; init; } = new();
    public SuperAdminRefundCustomerInfoDto Customer { get; init; } = new();
    public SuperAdminRefundItemInfoDto Item { get; init; } = new();
    public SuperAdminRefundFinancialPreviewDto FinancialPreview { get; init; } = new();
    public CommerceHubReturnDetailsDto? ReturnRequest { get; init; }
    public SuperAdminRefundExecutionDto? Execution { get; init; }
}

public sealed class SuperAdminRefundPayoutInfoDto
{
    public RefundPayoutMethod Method { get; init; }
    public string MaskedAccountIdentifier { get; init; } = string.Empty;
    public string? AccountHolderName { get; init; }
    public bool IsVerified { get; init; }
}

public sealed class SuperAdminRefundOrderInfoDto
{
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string PaymentMethodName { get; init; } = string.Empty;
    public PaymentStatus PaymentStatus { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal ShippingTotal { get; init; }
    public string Currency { get; init; } = "EGP";
    public DateTime CreatedAt { get; init; }
}

public sealed class SuperAdminRefundCustomerInfoDto
{
    public Guid CustomerId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
}

public sealed class SuperAdminRefundItemInfoDto
{
    public Guid OrderItemId { get; init; }
    public Guid SubOrderId { get; init; }
    public Guid VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string? VariantName { get; init; }
    public string? ImageUrl { get; init; }
    public int PurchasedQuantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal TotalPrice { get; init; }
    public decimal PlatformCommissionAmount { get; init; }
    public decimal VendorEarningAmount { get; init; }
    public CommerceHubOrderItemStatus Status { get; init; }
    public DateTime? DeliveredAt { get; init; }
}

public sealed class SuperAdminRefundFinancialPreviewDto
{
    public string PaymentType { get; init; } = string.Empty;
    public bool HasPayoutOccurred { get; init; }
    public decimal CommissionReversalAmount { get; init; }
    public decimal VendorEarningChange { get; init; }
    public decimal PlatformCashDelta { get; init; }
    public decimal PlatformPayableDelta { get; init; }
    public decimal VendorDebtDelta { get; init; }
    public decimal CommissionReceivableDelta { get; init; }
    public bool GatewayRefundRequired { get; init; }
    public Guid? PaymentId { get; init; }
    public PaymentProvider? PaymentProvider { get; init; }
    public CommerceHubPaymentRecordStatus? PaymentRecordStatus { get; init; }
    public decimal PaymentRefundedAmountBefore { get; init; }
    public decimal PaymentRefundedAmountAfter { get; init; }
    public decimal VendorNetEarningsBefore { get; init; }
    public decimal VendorNetEarningsAfter { get; init; }
    public decimal VendorPayableBefore { get; init; }
    public decimal VendorPayableAfter { get; init; }
    public decimal VendorDebtBefore { get; init; }
    public decimal VendorDebtAfter { get; init; }
    public bool HasPaidOutVendorEarnings { get; init; }
    public bool HasPendingPayoutReservation { get; init; }
    public string ExecutionPath { get; init; } = string.Empty;
    public string ImpactSummary { get; init; } = string.Empty;
}

public sealed class SuperAdminRefundExecutionDto
{
    public Guid ExecutionId { get; init; }
    public RefundPayoutMethod Method { get; init; }
    public string MaskedAccountIdentifier { get; init; } = string.Empty;
    public string TransactionReference { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public Guid ExecutedBy { get; init; }
    public DateTime ExecutedAt { get; init; }
    public bool IsSuccessful { get; init; }
    public string? FailureReason { get; init; }
    public Guid FinancialRefundId { get; init; }
    public CommerceHubRefundStatus Status { get; init; }
    public Guid? PaymentId { get; init; }
    public string? GatewayStatus { get; init; }
    public string? GatewayReference { get; init; }
    public decimal CommissionReversalAmount { get; init; }
    public decimal VendorEarningReversalAmount { get; init; }
    public bool IsPostPayoutAdjustment { get; init; }
    public DateTime? ProcessingStartedAtUtc { get; init; }
    public DateTime? ProcessedAtUtc { get; init; }
}

public sealed class SuperAdminApproveRefundRequest
{
    public string? Note { get; init; }
}

public sealed class SuperAdminRejectRefundRequest
{
    public string? Reason { get; init; }
}

public sealed class SuperAdminRetryRefundRequest
{
    public string? Note { get; init; }
}

public sealed class SuperAdminExecuteRefundRequest
{
    public RefundPayoutMethod? Method { get; init; }
    public string? TransactionReference { get; init; }
    public string? Notes { get; init; }
    public bool ConfirmMoneySent { get; init; }
    public bool ConfirmMethodOverride { get; init; }
}
