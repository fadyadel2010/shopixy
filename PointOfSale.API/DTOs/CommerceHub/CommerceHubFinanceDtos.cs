using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.CommerceHub;

public class CommerceHubCreatePayoutRequestDto
{
    public List<Guid> EarningIds { get; set; } = new();
    public CommerceHubPayoutMethod Method { get; set; } = CommerceHubPayoutMethod.Manual;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

/// <summary>
/// Request DTO for updating a CommerceHub payout.
/// Used to record external transfer details, failure reasons, and control state transitions.
/// </summary>
public class CommerceHubUpdatePayoutRequestDto
{
    public string? Reference { get; set; }
    public string? ExternalReferenceId { get; set; }
    public string? BankTransactionId { get; set; }
    
    /// <summary>
    /// POS-issued transfer intent key returned on payout creation.
    /// Must be echoed when moving a payout to Processing so external money movement is bound
    /// to the durable local payout record.
    /// </summary>
    public string? ExternalTransferIdempotencyKey { get; set; }

    /// <summary>
    /// POS-issued handoff token returned only after the Processing state is durably committed.
    /// External executors must echo it when completing or failing a processing payout so
    /// reconciliation cannot be accepted for pre-commit or stale transfer attempts.
    /// </summary>
    public string? ExternalTransferHandoffToken { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>
    /// Safety gate property that controls the Processing-to-Completed state transition.
    /// Must be explicitly set to true to allow a payout currently in Processing status to transition to Completed status.
    /// This confirms that the external bank/provider transfer was verified as executed before POS ledger cash/payable truth is mutated.
    /// Default value is false, which blocks payout completion even when external reference strings are supplied.
    /// </summary>
    public bool ExternalTransferConfirmedExecuted { get; set; }
    
    /// <summary>
    /// Safety gate property that controls the Processing-to-Failed state transition.
    /// Must be explicitly set to true to allow a payout currently in Processing status to transition to Failed status.
    /// This confirms that an external transfer was verified as NOT executed before marking the payout as failed.
    /// Prevents accidental double-payment scenarios where a payout is marked failed but the transfer actually succeeded.
    /// Default value is false, which blocks the Processing-to-Failed transition unless explicitly confirmed.
    /// This enforcement applies ONLY to payouts in Processing status; other status transitions are not affected.
    /// </summary>
    public bool ExternalTransferConfirmedNotExecuted { get; set; }
}

public class CommerceHubPayoutDto
{
    public Guid PayoutId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? PayoutMethodId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
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
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public List<CommerceHubPayoutItemDto> Items { get; set; } = new();
}

public class CommerceHubPayoutItemDto
{
    public Guid TenantId { get; set; }
    public Guid PayoutId { get; set; }
    public Guid PayoutItemId { get; set; }
    public Guid EarningId { get; set; }
    public Guid SubOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class CommerceHubCreateRefundRequestDto
{
    public Guid SubOrderId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class CommerceHubRefundDto
{
    public Guid RefundId { get; set; }
    public Guid TenantId { get; set; }
    public Guid SubOrderId { get; set; }
    public Guid? PaymentId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal CommissionReversalAmount { get; set; }
    public decimal PromoDiscountReversalAmount { get; set; }
    public decimal VendorEarningReversalAmount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? GatewayStatus { get; set; }
    public string? FailureReason { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public bool IsFullRefund { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessingStartedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
}

public class CommerceHubConfirmRefundRequestDto
{
    public string? Reference { get; set; }
    public string? GatewayStatus { get; set; }
}

public class CommerceHubFailRefundRequestDto
{
    public string? Reference { get; set; }
    public string? GatewayStatus { get; set; }
    public string? FailureReason { get; set; }
}

public class CommerceHubFinanceOverviewDto
{
    public decimal TotalRevenue { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal TotalPaidToVendors { get; set; }
    public decimal PendingPayouts { get; set; }
    public decimal RefundTotals { get; set; }
    public decimal PendingEarnings { get; set; }
    public decimal AvailableEarnings { get; set; }
    public decimal PaidOutEarnings { get; set; }
}

public class CommerceHubAdminPaymentDto
{
    public Guid PaymentId { get; set; }
    public Guid MarketplaceOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal RefundedAmount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
}

public class CommerceHubAdminEarningDto
{
    public Guid EarningId { get; set; }
    public Guid TenantId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public Guid SubOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? OriginalEarningId { get; set; }
    public Guid? RefundId { get; set; }
    public Guid? PayoutId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AvailableAtUtc { get; set; }
    public DateTime? PaidOutAtUtc { get; set; }
    public DateTime? ReversedAtUtc { get; set; }
}

public class CommerceHubAdminPayoutDto
{
    public Guid PayoutId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? PayoutMethodId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? DestinationValueSnapshot { get; set; }
    public string? Reference { get; set; }
    public string? ExternalReferenceId { get; set; }
    public string? BankTransactionId { get; set; }
    public string? ExternalTransferIdempotencyKey { get; set; }
    public string? ProcessedBy { get; set; }
    public string? FailureReason { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public int ItemsCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
}

public class CommerceHubAdminRefundDto
{
    public Guid RefundId { get; set; }
    public Guid TenantId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public Guid SubOrderId { get; set; }
    public Guid? PaymentId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal CommissionReversalAmount { get; set; }
    public decimal PromoDiscountReversalAmount { get; set; }
    public decimal VendorEarningReversalAmount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? GatewayStatus { get; set; }
    public string? FailureReason { get; set; }
    public bool IsFullRefund { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessingStartedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
}

public class CommerceHubAdminAuditLogDto
{
    public Guid AuditLogId { get; set; }
    public Guid? TenantId { get; set; }
    public string? StoreName { get; set; }
    public Guid? MarketplaceOrderId { get; set; }
    public Guid? SubOrderId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? EarningId { get; set; }
    public Guid? PayoutId { get; set; }
    public Guid? RefundId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Description { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
