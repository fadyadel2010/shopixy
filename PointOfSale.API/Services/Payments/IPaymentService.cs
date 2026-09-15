using PointOfSale.API.DTOs.Payments;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Payments;

/// <summary>
/// Service interface for Payment operations.
/// 
/// PHASE 4.1: PAYMENTS API
/// 
/// ARCHITECTURAL BOUNDARY:
/// - Payment owns payment intent/session orchestration, provider verification, webhook admission, status transitions, audit, and lifecycle delegation
/// - Orders provide immutable payable/order reference evidence; they do not create payment truth
/// - Provider webhooks/callbacks are signed evidence that must be admitted by Payment before SQL status changes
/// - COD has no gateway and no webhook; collection still enters the Payment lifecycle boundary
/// - Payment creation, webhook handling, lifecycle transitions, refunds, settlement fanout, and recovery are idempotent
/// - Inventory/order side effects are downstream results of admitted payment lifecycle outcomes, not Payment authority expansion
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Creates a payment intent for an order.
    /// 
    /// For COD: Status = Skipped, order confirmed immediately
    /// For Online: Status = Pending, returns redirect URL
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="orderId">Order ID</param>
    /// <param name="request">Payment intent request</param>
    /// <param name="promotionCalculationFailed">Whether promotion calculation failed during checkout</param>
    /// <param name="requestId">Idempotency key</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Payment intent response</returns>
    Task<PaymentIntentResponseDto> CreatePaymentIntentAsync(
        Guid tenantId,
        Guid orderId,
        CreatePaymentIntentRequestDto request,
        bool promotionCalculationFailed,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a durable payment intent that already exists. Recovery callers
    /// must not use this to create a competing payment authority row.
    /// </summary>
    Task<PaymentIntentResponseDto> ResumeExistingPaymentIntentAsync(
        Guid tenantId,
        Guid orderId,
        Guid paymentIntentId,
        PaymentProvider provider,
        bool promotionCalculationFailed,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a webhook event from a payment provider.
    /// 
    /// Steps:
    /// 1. Verify signature
    /// 2. Check idempotency
    /// 3. Store raw payload
    /// 4. Resolve payment intent
    /// 5. Validate amount/currency
    /// 6. Update payment & order status
    /// 7. Confirm/release inventory
    /// 8. Emit events
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="provider">Payment provider name</param>
    /// <param name="signature">Provider signature header</param>
    /// <param name="rawPayload">Raw webhook body</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="sourceIp">Source IP address</param>
    /// <param name="userAgent">User agent</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Webhook processing response</returns>
    Task<WebhookResponseDto> ProcessWebhookAsync(
        Guid tenantId,
        string provider,
        string? signature,
        string rawPayload,
        string correlationId,
        string? sourceIp,
        string? userAgent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms a COD order.
    /// 
    /// Steps:
    /// 1. Validate order exists
    /// 2. Validate PaymentMethod = COD
    /// 3. Mark order as Confirmed
    /// 4. Confirm inventory
    /// 5. Emit events
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="orderId">Order ID</param>
    /// <param name="requestId">Idempotency key</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>COD confirmation response</returns>
    Task<CodConfirmResponseDto> ConfirmCodOrderAsync(
        Guid tenantId,
        Guid orderId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the payment status of an order.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="orderId">Order ID</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Order payment status</returns>
    Task<OrderPaymentStatusDto> GetOrderPaymentStatusAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the canonical payment status for storefront confirmation flows.
    /// Only PAID may be treated as success by the UI.
    /// </summary>
    Task<CanonicalPaymentStatusDto> GetCanonicalPaymentStatusAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the ownership/session access data required to authorize payment actions for an order.
    /// </summary>
    Task<PaymentOrderAccessInfo> GetOrderAccessInfoAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // PHASE P3.1: MANUAL WALLET MANAGEMENT
    // =========================================================================

    /// <summary>
    /// Confirms a manual wallet payment.
    /// Unlocks shipment creation.
    /// </summary>
    [Obsolete("Legacy manual wallet confirmation is disabled. Use IPaymentLifecycleService.ApproveReviewedPaymentAsync.", true)]
    Task ConfirmManualPaymentAsync(
        Guid tenantId,
        Guid paymentIntentId,
        string adminId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a manual wallet payment.
    /// Blocks shipment creation.
    /// </summary>
    [Obsolete("Legacy manual wallet rejection is disabled. Use IPaymentLifecycleService.RejectReviewedPaymentAsync.", true)]
    Task RejectManualPaymentAsync(
        Guid tenantId,
        Guid paymentIntentId,
        string adminId,
        string rejectionReason,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all pending manual wallet payments for the tenant.
    /// </summary>
    Task<List<PointOfSale.API.DTOs.Admin.PendingManualPaymentDto>> GetPendingManualPaymentsAsync(
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed record PaymentOrderAccessInfo(
    Guid? CustomerId,
    Guid? OnlineCustomerId,
    string? SessionId,
    bool IsGuestCheckout)
{
    public bool MatchesCustomer(Guid customerId) =>
        customerId == CustomerId || customerId == OnlineCustomerId;

    public bool RequiresGuestSessionValidation =>
        IsGuestCheckout || (!CustomerId.HasValue && !OnlineCustomerId.HasValue);
}

#region Payment Exceptions

/// <summary>
/// Base exception for payment failures.
/// </summary>
public class PaymentException : Exception
{
    public string ErrorCode { get; }

    public PaymentException(string errorCode, string message) 
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public PaymentException(string errorCode, string message, Exception innerException) 
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Thrown when order is not found.
/// HTTP 404
/// </summary>
public class PaymentOrderNotFoundException : PaymentException
{
    public Guid OrderId { get; }

    public PaymentOrderNotFoundException(Guid orderId) 
        : base("ORDER_NOT_FOUND", $"Order {orderId} not found")
    {
        OrderId = orderId;
    }
}

/// <summary>
/// Thrown when customer attempts to access payment for an order they don't own.
/// HTTP 403 (but returned as 404 to prevent order ID enumeration)
/// </summary>
public class PaymentUnauthorizedAccessException : PaymentException
{
    public Guid OrderId { get; }
    public Guid? CustomerId { get; }

    public PaymentUnauthorizedAccessException(Guid orderId, Guid? customerId) 
        : base("UNAUTHORIZED_ACCESS", $"Customer {customerId} is not authorized to access order {orderId}")
    {
        OrderId = orderId;
        CustomerId = customerId;
    }
}

/// <summary>
/// Thrown when order is in invalid state for payment.
/// HTTP 409
/// </summary>
public class PaymentOrderInvalidStateException : PaymentException
{
    public Guid OrderId { get; }
    public string CurrentStatus { get; }
    public string ExpectedStatus { get; }

    public PaymentOrderInvalidStateException(
        Guid orderId,
        string currentStatus,
        string expectedStatus,
        string errorCode = "ORDER_INVALID_STATE") 
        : base(errorCode,
            $"Order {orderId} is in {currentStatus} state, expected {expectedStatus}")
    {
        OrderId = orderId;
        CurrentStatus = currentStatus;
        ExpectedStatus = expectedStatus;
    }
}

/// <summary>
/// Thrown when webhook signature is invalid.
/// HTTP 401
/// </summary>
public class PaymentInvalidSignatureException : PaymentException
{
    public string Provider { get; }

    public PaymentInvalidSignatureException(string provider) 
        : base("INVALID_SIGNATURE", $"Invalid webhook signature for provider {provider}")
    {
        Provider = provider;
    }
}

/// <summary>
/// Thrown when payment details don't match.
/// HTTP 409
/// </summary>
public class PaymentMismatchException : PaymentException
{
    public string Field { get; }
    public string Expected { get; }
    public string Actual { get; }

    public PaymentMismatchException(string field, string expected, string actual) 
        : base("PAYMENT_MISMATCH", 
            $"Payment {field} mismatch: expected {expected}, got {actual}")
    {
        Field = field;
        Expected = expected;
        Actual = actual;
    }
}

/// <summary>
/// Thrown when payment provider is not supported.
/// HTTP 400
/// </summary>
public class PaymentProviderNotSupportedException : PaymentException
{
    public string Provider { get; }

    public PaymentProviderNotSupportedException(string provider) 
        : base("PROVIDER_NOT_SUPPORTED", $"Payment provider {provider} is not supported")
    {
        Provider = provider;
    }
}

/// <summary>
/// Thrown when manual wallet payment is requested without a valid merchant wallet configuration.
/// HTTP 400
/// </summary>
public class PaymentManualWalletNotConfiguredException : PaymentException
{
    public Guid TenantId { get; }

    public PaymentManualWalletNotConfiguredException(Guid tenantId)
        : base(
            "MANUAL_WALLET_NOT_CONFIGURED",
            "Manual wallet payment is unavailable because the merchant wallet is not configured.")
    {
        TenantId = tenantId;
    }
}

/// <summary>
/// Thrown when manual wallet payment references an untrusted or missing proof file.
/// HTTP 400
/// </summary>
public class PaymentManualWalletProofException : PaymentException
{
    public PaymentManualWalletProofException(string message)
        : base("INVALID_MANUAL_WALLET_PROOF", message)
    {
    }
}

/// <summary>
/// Thrown when the legacy direct payment-intent API is used for manual wallet payments.
/// HTTP 400
/// </summary>
public class PaymentManualWalletCheckoutOnlyException : PaymentException
{
    public PaymentManualWalletCheckoutOnlyException()
        : base(
            "MANUAL_WALLET_CHECKOUT_ONLY",
            "Manual wallet payment intents must be created through the checkout flow.")
    {
    }
}

/// <summary>
/// Thrown when payment intent is not found.
/// HTTP 404
/// </summary>
public class PaymentIntentNotFoundException : PaymentException
{
    public string PaymentReference { get; }

    public PaymentIntentNotFoundException(string paymentReference) 
        : base("PAYMENT_INTENT_NOT_FOUND", $"Payment intent with reference {paymentReference} not found")
    {
        PaymentReference = paymentReference;
    }
}

/// <summary>
/// Thrown when COD confirmation is not applicable.
/// HTTP 400
/// </summary>
public class CodNotApplicableException : PaymentException
{
    public Guid OrderId { get; }
    public string PaymentMethod { get; }

    public CodNotApplicableException(Guid orderId, string paymentMethod) 
        : base("COD_NOT_APPLICABLE", 
            $"Order {orderId} uses {paymentMethod}, not Cash on Delivery")
    {
        OrderId = orderId;
        PaymentMethod = paymentMethod;
    }
}

/// <summary>
/// T-09: Thrown when Fawry financial activity is attempted without certified
/// clearing, accounting mapping, or settlement authority.
/// HTTP 422 - permanent authority failure, never a transient provider outage.
/// </summary>
public class PaymentFawryFinancialAuthorityException : PaymentException
{
    public PaymentFawryFinancialAuthorityException()
        : base(
            PointOfSale.Core.Payments.FawryFinancialAuthorityPolicy.Code,
            PointOfSale.Core.Payments.FawryFinancialAuthorityPolicy.Message)
    {
    }
}

/// <summary>
/// Thrown when payment lifecycle operation fails and compensation rollback also fails.
/// This indicates the payment is in an inconsistent state requiring manual reconciliation.
/// HTTP 500
/// </summary>
public class CompensationFailedException : PaymentException
{
    public Guid PaymentIntentId { get; }
    public string TraceId { get; }

    public CompensationFailedException(Guid paymentIntentId, string traceId, string message, Exception innerException) 
        : base("LIFECYCLE_COMPENSATION_FAILED", message, innerException)
    {
        PaymentIntentId = paymentIntentId;
        TraceId = traceId;
    }
}

#endregion
