using PointOfSale.Core.Enums;
using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Interfaces.Payments;

/// <summary>
/// PHASE: Payment Lifecycle Architecture
/// 
/// Service that manages payment lifecycle state transitions.
/// 
/// CRITICAL RULES:
/// 1. This is the ONLY way to change PaymentStatus
/// 2. Every collected/settled transition creates a journal entry via outbox
/// 3. Direct PaymentStatus changes are BLOCKED
/// </summary>
public interface IPaymentLifecycleService
{
    /// <summary>
    /// Canonical payment lifecycle entry point for all payment outcomes.
    ///
    /// SUCCESS:
    /// Payment confirmed -> Commit reservation -> Update order/payment state -> Publish events
    ///
    /// FAILURE:
    /// Payment failed -> Release reservation -> Update order/payment state -> Publish events
    /// 
    /// SUPPORTED OUTCOMES:
    /// - Authorized: Payment authorized but not yet captured
    /// - Succeeded: Payment successfully captured/collected
    /// - Failed: Payment failed
    /// - Cancelled: Payment cancelled by user or system
    /// - Rejected: Payment rejected (e.g., wallet payment rejection)
    /// 
    /// UNSUPPORTED OUTCOMES:
    /// - AmountMismatch: This is a SYSTEM-ONLY outcome. Do not pass this value.
    ///   Amount mismatches are automatically detected and quarantined by the service.
    /// </summary>
    Task<PaymentTransitionResult> HandlePaymentResultAsync(
        HandlePaymentResultRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Creates the durable reviewable payment intent authority row.
    /// Provider application orchestration may prepare facts, but durable
    /// lifecycle creation must converge through this service.
    /// </summary>
    Task<PaymentIntent> CreateReviewablePaymentIntentAsync(
        ReviewablePaymentIntentCreateRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Admits a Runtime-approved payment proof into the pending-review
    /// lifecycle state. This is lifecycle mutation only; Runtime execution
    /// remains application-owned and provider-neutral.
    /// </summary>
    Task<PaymentTransitionResult> AdmitPaymentReviewAsync(
        PaymentReviewAdmissionRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Mark an offline-collection order as delivered and collected.
    /// 
    /// TRANSITIONS: Pending → Collected
    /// JOURNAL: DEBIT offline clearing, CREDIT E-commerce Sales
    /// </summary>
    Task<PaymentTransitionResult> MarkOfflineCollectionDeliveredAsync(
        Guid tenantId,
        Guid orderId,
        string? deliveryReference = null,
        string? confirmedBy = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Record an offline collection settlement from a collector.
    /// 
    /// TRANSITIONS: Collected → Settled (for included orders)
    /// JOURNAL: DEBIT Bank (net), DEBIT collection fees, CREDIT offline clearing (gross)
    /// </summary>
    Task<PaymentTransitionResult> RecordOfflineSettlementAsync(
        OfflineSettlementRequest request,
        CancellationToken ct = default);
    
    /// <summary>
    /// Capture an online payment (called by gateway webhook handler).
    /// 
    /// TRANSITIONS: Pending/Authorized → Collected
    /// JOURNAL: DEBIT Gateway Clearing, CREDIT E-commerce Sales
    /// 
    /// PARAMETERS:
    /// - providerAmount: The actual amount charged by the payment provider (AUTHORITATIVE).
    /// - providerCurrency: The currency code used by the payment provider (AUTHORITATIVE).
    /// These provider fields represent the source of truth for reconciliation and must match
    /// the gateway's transaction records.
    /// </summary>
    Task<PaymentTransitionResult> CaptureOnlinePaymentAsync(
        Guid orderId,
        string gateway,
        string paymentIntentId,
        decimal providerAmount,
        string providerCurrency,
        CancellationToken ct = default);
    
    /// <summary>
    /// Record a gateway settlement.
    /// 
    /// TRANSITIONS: Collected → Settled (for affected orders)
    /// JOURNAL: DEBIT Bank (net), DEBIT Gateway Fees, CREDIT Gateway Clearing (gross)
    /// </summary>
    Task<PaymentTransitionResult> RecordGatewaySettlementAsync(
        GatewaySettlementRequest request,
        CancellationToken ct = default);
    
    /// <summary>
    /// Approve a reviewed payment.
    /// 
    /// TRANSITIONS: AwaitingVerification -> Collected
    /// JOURNAL: DEBIT provider clearing, CREDIT E-commerce Sales
    /// </summary>
    Task<PaymentTransitionResult> ApproveReviewedPaymentAsync(
        Guid orderId,
        string approvedBy,
        string? transactionReference = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Reject a reviewed payment.
    /// 
    /// TRANSITIONS: AwaitingVerification -> Rejected
    /// NO JOURNAL (money never recognized)
    /// </summary>
    Task<PaymentTransitionResult> RejectReviewedPaymentAsync(
        Guid orderId,
        string rejectedBy,
        string reason,
        CancellationToken ct = default);
    
    /// <summary>
    /// Record a refund.
    /// 
    /// TRANSITIONS: Collected/Settled → Refunded/PartiallyRefunded
    /// JOURNAL: DEBIT E-commerce Sales, CREDIT Clearing
    /// </summary>
    Task<PaymentTransitionResult> RecordRefundAsync(
        Guid orderId,
        decimal refundAmount,
        string refundId,
        string reason,
        CancellationToken ct = default);
    
    /// <summary>
    /// Mark a payment as failed.
    /// 
    /// TRANSITIONS: Pending/Authorized → Failed
    /// NO JOURNAL (money never recognized)
    /// </summary>
    Task<PaymentTransitionResult> MarkPaymentFailedAsync(
        Guid orderId,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Mark a payment window as expired after its inventory reservation can no longer be honored.
    ///
    /// TRANSITIONS: Pending/Authorized/AwaitingVerification -> Expired
    /// NO JOURNAL (money never recognized)
    /// </summary>
    Task<PaymentTransitionResult> MarkPaymentExpiredAsync(
        Guid tenantId,
        Guid orderId,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Reopens a failed payment aggregate for a new provider attempt.
    /// The caller must restore inventory reservation authority before requesting the transition.
    ///
    /// TRANSITIONS: Failed -> Pending
    /// NO JOURNAL (no money is recognized)
    /// </summary>
    Task<PaymentTransitionResult> ResetFailedPaymentForRetryAsync(
        Guid orderId,
        Guid failedPaymentIntentId,
        CancellationToken ct = default);
    
    /// <summary>
    /// Get the allowed actions for an order based on its current state.
    /// Used by UI to show appropriate buttons.
    /// </summary>
    Task<PaymentAllowedActions> GetAllowedActionsAsync(Guid orderId, CancellationToken ct = default);
}

/// <summary>
/// Payment lifecycle outcome values for HandlePaymentResultAsync.
/// 
/// IMPORTANT: AmountMismatch is a SYSTEM-ONLY outcome and must NOT be passed by external callers.
/// The payment lifecycle service automatically detects and quarantines amount mismatches internally.
/// </summary>
public enum PaymentLifecycleOutcome
{
    Authorized = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,
    Rejected = 4,
    
    /// <summary>
    /// SYSTEM-ONLY: Do not use this outcome in HandlePaymentResultRequest.
    /// Amount mismatches are detected and handled automatically by the payment lifecycle service.
    /// This value exists for internal state tracking only.
    /// </summary>
    AmountMismatch = 5,

    /// <summary>
    /// Payment window expired before money could be safely recognized.
    /// No journal entry is created; inventory and coupon reservations are released.
    /// </summary>
    Expired = 6
}

public enum PaymentLifecycleSource
{
    GatewayWebhook = 0,
    OfflineCollection = 3,
    PaymentReviewApproval = 4,
    PaymentReviewRejection = 5,
    AdminCancellation = 6,
    ReservationCleanup = 7,

    /// <summary>
    /// Internal SQL lifecycle fanout for accepted refund mutations.
    /// Provider refund evidence must first converge through the lifecycle owner.
    /// </summary>
    RefundAuthority = 8,

    /// <summary>
    /// Internal SQL lifecycle fanout for accepted settlement mutations.
    /// Settlement/reconciliation retries must not reconstruct payment visibility locally.
    /// </summary>
    SettlementAuthority = 9,

    /// <summary>
    /// Internal admission of a new attempt after a terminal failed payment.
    /// </summary>
    PaymentRetry = 10
}

public enum PaymentInventoryAction
{
    None = 0,
    Commit = 1,
    Release = 2
}

/// <summary>
/// Request for handling payment lifecycle transitions.
/// 
/// PROVIDER FIELD REQUIREMENTS BY SOURCE:
/// 
/// WEBHOOK SOURCES (GatewayWebhook):
/// - ProviderAmount and ProviderCurrency are REQUIRED for Succeeded outcomes
/// - Missing or invalid provider fields will cause the payment to be QUARANTINED with status AmountMismatch
/// - The payment will be placed on hold for manual review
/// - Amount/Currency fields are NOT used as fallback for webhook sources
/// - Provider identity is carried as Gateway data, not as a lifecycle source
/// 
/// NON-WEBHOOK SOURCES (OfflineCollection, PaymentReviewApproval, PaymentReviewRejection, AdminCancellation, ReservationCleanup):
/// - ProviderAmount and ProviderCurrency are optional
/// - Amount/Currency fields are used when provider fields are not provided
/// - Provider fields take precedence when both are present
///
/// INTERNAL FANOUT SOURCES (RefundAuthority, SettlementAuthority):
/// - Reserved for PaymentLifecycleService visibility events after SQL refund/settlement mutation
/// - Do not pass them to HandlePaymentResultAsync from API/provider/recovery callers
/// 
/// RATIONALE:
/// Provider fields represent the actual amount charged by the payment gateway in the gateway's
/// native currency, which is the source of truth for reconciliation and accounting. For webhook
/// sources, missing provider data indicates a potential integration issue or fraud risk, requiring
/// manual review. Legacy Amount/Currency fields are retained for internal callers where provider authority is not applicable.
/// where provider authority is not applicable.
/// </summary>
public sealed class HandlePaymentResultRequest
{
    /// <summary>
    /// Explicit tenant authority for system/background flows that bypass global
    /// query filters. Required whenever IgnoreQueryFilters is true.
    /// </summary>
    public Guid? TenantId { get; set; }

    public Guid OrderId { get; set; }
    public Guid? PaymentIntentId { get; set; }
    public PaymentLifecycleOutcome Outcome { get; set; }
    public PaymentLifecycleSource Source { get; set; } = PaymentLifecycleSource.GatewayWebhook;
    public string? Gateway { get; set; }
    public string? PaymentReference { get; set; }
    public string? ProviderPaymentId { get; set; }
    
    /// <summary>
    /// Payment amount for non-webhook sources.
    /// For webhook sources, use ProviderAmount instead - this field is ignored.
    /// See class-level documentation for provider field requirements by source.
    /// </summary>
    public decimal? Amount { get; set; }
    
    /// <summary>
    /// Payment currency code for non-webhook sources.
    /// For webhook sources, use ProviderCurrency instead - this field is ignored.
    /// See class-level documentation for provider field requirements by source.
    /// </summary>
    public string? Currency { get; set; }
    
    /// <summary>
    /// REQUIRED for webhook sources: Actual amount charged by the payment provider.
    /// Missing or invalid values for webhook sources will quarantine the payment for manual review.
    /// Optional for non-webhook sources.
    /// See class-level documentation for provider field requirements by source.
    /// </summary>
    public decimal? ProviderAmount { get; set; }
    
    /// <summary>
    /// REQUIRED for webhook sources: Currency code used by the payment provider.
    /// Missing or invalid values for webhook sources will quarantine the payment for manual review.
    /// Optional for non-webhook sources.
    /// See class-level documentation for provider field requirements by source.
    /// </summary>
    public string? ProviderCurrency { get; set; }
    
    public string? Reason { get; set; }
    public string? FailureCode { get; set; }
    public string? Actor { get; set; }
    public string? WebhookEventId { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime? ReviewProofRetentionExpiresAtUtc { get; set; }
    public bool IgnoreQueryFilters { get; set; }
    
    /// <summary>
    /// When true, skips payment intent resolution entirely and operates only on order-level state.
    /// Used when no payment intents exist or when order-only operations are required.
    /// </summary>
    public bool SkipPaymentIntentResolution { get; set; }
}

public sealed class ReviewablePaymentIntentCreateRequest
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public PaymentMethod PaymentMethod { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string? WalletType { get; set; }
    public string? WalletPhoneNumber { get; set; }
    public string? ProofImageUrl { get; set; }
    public DateTime? ProofImageRetentionExpiresAtUtc { get; set; }
    public string? ProviderMetadata { get; set; }
}

public sealed class PaymentReviewAdmissionRequest
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid PaymentIntentId { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string ProviderIntentId { get; set; } = string.Empty;
    public DateTime ProofImageRetentionExpiresAtUtc { get; set; }
    public string? Actor { get; set; }
}

/// <summary>
/// Result of a payment lifecycle transition.
/// </summary>
public class PaymentTransitionResult
{
    public bool Success { get; init; }
    
    /// <summary>
    /// Alias for Success for better readability
    /// </summary>
    public bool IsSuccess => Success;
    
    public PaymentStatus? NewStatus { get; init; }
    public string? JournalNumber { get; init; }
    public string? ErrorMessage { get; init; }

    public string? FailureCode { get; init; }

    public bool RequiresManualReview { get; init; }
    
    /// <summary>
    /// Status of the outbox message for accounting.
    /// Values: CREATED, PENDING, FAILED, WAITING_RETRY, PROCESSED, null
    /// </summary>
    public string? OutboxStatus { get; init; }
    
    /// <summary>
    /// ID of the outbox message created (if any)
    /// </summary>
    public Guid? OutboxMessageId { get; init; }

    public bool WasIdempotent { get; init; }

    /// <summary>
    /// True when this call performed a real state mutation.
    /// False for idempotent replays or stale outcomes that were safely ignored.
    /// </summary>
    public bool StateChanged { get; init; }

    public PaymentInventoryAction InventoryAction { get; init; } = PaymentInventoryAction.None;
    
    public static PaymentTransitionResult Succeeded(
        PaymentStatus newStatus,
        string? journalNumber = null,
        string? outboxStatus = null,
        Guid? outboxMessageId = null,
        bool wasIdempotent = false,
        bool stateChanged = true,
        PaymentInventoryAction inventoryAction = PaymentInventoryAction.None)
        => new()
        {
            Success = true,
            NewStatus = newStatus,
            JournalNumber = journalNumber,
            OutboxStatus = outboxStatus,
            OutboxMessageId = outboxMessageId,
            WasIdempotent = wasIdempotent,
            StateChanged = stateChanged,
            InventoryAction = inventoryAction
        };
    
    public static PaymentTransitionResult Failed(string errorMessage, string? failureCode = null) 
        => new() { Success = false, ErrorMessage = errorMessage, FailureCode = failureCode };

    public static PaymentTransitionResult ManualReviewRequired(
        PaymentStatus newStatus,
        string errorMessage,
        string failureCode)
        => new()
        {
            Success = false,
            NewStatus = newStatus,
            ErrorMessage = errorMessage,
            FailureCode = failureCode,
            RequiresManualReview = true,
            StateChanged = true
        };
}

/// <summary>
/// Allowed payment actions for UI display.
/// </summary>
public class PaymentAllowedActions
{
    public Guid OrderId { get; set; }
    public PaymentStatus CurrentStatus { get; set; }
    public string PaymentMethodCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Can show an offline collection confirmation button.
    /// </summary>
    public bool CanMarkOfflineCollectionDelivered { get; set; }
    
    /// <summary>
    /// Can show payment review approval button.
    /// </summary>
    public bool CanApproveReviewedPayment { get; set; }
    
    /// <summary>
    /// Can show payment review rejection button.
    /// </summary>
    public bool CanRejectReviewedPayment { get; set; }
    
    /// <summary>
    /// Can issue a refund
    /// </summary>
    public bool CanRefund { get; set; }
    
    /// <summary>
    /// UI message to display (e.g., "Waiting for gateway confirmation")
    /// </summary>
    public string StatusMessage { get; set; } = string.Empty;
}

/// <summary>
/// Request for offline collection settlement recording.
/// </summary>
public class OfflineSettlementRequest
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// Unique settlement ID from shipping company
    /// </summary>
    public string SettlementId { get; set; } = string.Empty;
    
    /// <summary>
    /// Gross amount collected from customers
    /// </summary>
    public decimal GrossAmount { get; set; }
    
    /// <summary>
    /// Shipping company fees deducted
    /// </summary>
    public decimal Fees { get; set; }

    public string Currency { get; set; } = string.Empty;
    
    /// <summary>
    /// Shipping company name
    /// </summary>
    public string ShippingCompany { get; set; } = string.Empty;
    
    /// <summary>
    /// Order IDs included in this settlement (for reconciliation)
    /// </summary>
    public List<Guid> OrderIds { get; set; } = new();
    
    /// <summary>
    /// Bank reference for the deposit
    /// </summary>
    public string? BankReference { get; set; }
}

/// <summary>
/// Request for gateway settlement recording.
/// </summary>
public class GatewaySettlementRequest
{
    public Guid TenantId { get; set; }
    public string SettlementId { get; set; } = string.Empty;
    public string Gateway { get; set; } = string.Empty;
    public string? PayoutBatchId { get; set; }
    public List<Guid> OrderIds { get; set; } = new();
    public List<string> PaymentIntentIds { get; set; } = new();
    public decimal GrossAmount { get; set; }
    public decimal Fees { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? BankReference { get; set; }
}
