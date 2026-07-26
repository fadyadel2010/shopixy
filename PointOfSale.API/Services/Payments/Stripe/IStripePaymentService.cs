using PointOfSale.API.DTOs.Payments;

namespace PointOfSale.API.Services.Payments.Stripe;

/// <summary>
/// Stripe Payment Service interface.
/// 
/// PHASE 10.4.D4: Stripe Integration (Global, Intent-Based, Production-Grade)
/// 
/// ARCHITECTURAL RULES (STRICT):
/// - Uses Stripe Payment Intents (NOT Charges API)
/// - Order is created BEFORE payment confirmation
/// - Stripe confirms payment against existing Order
/// - Webhooks are source of truth for final payment status
/// - Secrets are encrypted via Data Protection API
/// - Tenant-aware (each tenant has their own Stripe config)
/// 
/// FORBIDDEN PATTERNS:
/// - ❌ Hardcoding Stripe keys
/// - ❌ Storing secrets in MongoDB or frontend
/// - ❌ Creating Stripe charges directly
/// - ❌ Recalculating totals in Stripe flow
/// - ❌ Creating orders after payment without idempotency
/// - ❌ Mixing Stripe logic with Cart or Coupon services
/// - ❌ Assuming a single Stripe account globally
/// </summary>
public interface IStripePaymentService
{
    /// <summary>
    /// Creates a Stripe PaymentIntent for an existing order.
    /// 
    /// Prerequisites:
    /// - Order must exist with PaymentMethodCode = "STRIPE"
    /// - Order must be in PendingPayment status
    /// - Tenant must have Stripe configured and enabled
    /// - Checkout token must match the order's session
    /// 
    /// Returns:
    /// - clientSecret: For Stripe.js on frontend
    /// - publishableKey: For Stripe.js initialization
    /// 
    /// Metadata sent to Stripe:
    /// - orderId
    /// - tenantId
    /// - orderNumber
    /// - paymentReference
    /// - storefrontBaseUrl
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="orderId">Order ID</param>
    /// <param name="checkoutToken">Checkout session token for ownership validation</param>
    /// <param name="customerId">Optional online customer ID</param>
    /// <param name="promotionCalculationFailed">Whether promotion calculation failed during checkout</param>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="idempotencyKey">Optional idempotency key for at-most-once semantics. Callers should provide a deterministic, client-generated unique request ID (e.g., GUID or hash) when retrying or ensuring duplicate prevention. Leave null to rely on default service behavior.</param>
    /// <returns>Stripe payment intent response with client secret</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when checkout token doesn't match order session</exception>
    Task<StripePaymentIntentResponseDto> CreatePaymentIntentAsync(
        Guid tenantId,
        Guid orderId,
        string checkoutToken,
        Guid? customerId,
        bool promotionCalculationFailed,
        string correlationId,
        CancellationToken cancellationToken = default,
        string? idempotencyKey = null);

    /// <summary>
    /// Retrieves a Stripe PaymentIntent status.
    /// 
    /// Used for:
    /// - Polling payment status from frontend
    /// - Verifying payment before showing success
    /// 
    /// Requires checkout token for ownership validation.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="orderId">Order ID</param>
    /// <param name="checkoutToken">Checkout session token for ownership validation</param>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Payment intent status</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when checkout token doesn't match order session</exception>
    Task<StripePaymentStatusDto> GetPaymentIntentStatusAsync(
        Guid tenantId,
        Guid orderId,
        string checkoutToken,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a Stripe webhook event.
    /// 
    /// Handles:
    /// - payment_intent.succeeded → Mark order as Paid
    /// - payment_intent.payment_failed → Mark payment failed
    /// - payment_intent.canceled → Mark order canceled
    /// - charge.refunded → Mark order refunded (future)
    /// 
    /// Rules:
    /// - Validate signature using webhook secret
    /// - Idempotent processing
    /// - Load order using metadata.orderId
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="signature">Stripe-Signature header</param>
    /// <param name="rawPayload">Raw webhook body</param>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Webhook processing result</returns>
    Task<StripeWebhookResultDto> ProcessWebhookAsync(
        Guid tenantId,
        string signature,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates if Stripe is configured and enabled for a tenant.
    /// 
    /// Used by:
    /// - Checkout flow to determine if Stripe is available
    /// - Admin UI to show configuration status
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stripe configuration status</returns>
    Task<StripeConfigurationStatusDto> GetConfigurationStatusAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

#region DTOs

/// <summary>
/// Stripe PaymentIntent creation response.
/// </summary>
public class StripePaymentIntentResponseDto
{
    /// <summary>
    /// Client secret for Stripe.js Payment Element.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Publishable key for Stripe.js initialization.
    /// </summary>
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>
    /// Internal payment intent ID.
    /// </summary>
    public Guid PaymentIntentId { get; set; }

    /// <summary>
    /// Order ID.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Order number.
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// Payment amount.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Currency code (e.g., USD, SAR).
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Stripe PaymentIntent ID.
    /// </summary>
    public string StripePaymentIntentId { get; set; } = string.Empty;
}

/// <summary>
/// Stripe payment status response.
/// </summary>
public class StripePaymentStatusDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string StripeStatus { get; set; } = string.Empty;
    public bool IsPaid { get; set; }
    public bool RequiresAction { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>
/// Stripe webhook processing result.
/// </summary>
public class StripeWebhookResultDto
{
    public bool Processed { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? EventLogId { get; set; }
    public string? OrderId { get; set; }
    public string? EventType { get; set; }
}

/// <summary>
/// Stripe configuration status for a tenant.
/// </summary>
public class StripeConfigurationStatusDto
{
    public bool IsEnabled { get; set; }
    public bool IsConfigured { get; set; }
    public bool IsSandbox { get; set; }
    public string? ValidationMessage { get; set; }
}

/// <summary>
/// Stripe configuration stored in TenantPaymentMethod.ConfigEncrypted.
/// </summary>
public class TenantStripeConfiguration
{
    public string PublishableKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
}

#endregion

#region Exceptions

/// <summary>
/// Thrown when Stripe is not configured for a tenant.
/// HTTP 400
/// </summary>
public class StripeNotConfiguredException : PaymentException
{
    public Guid TenantId { get; }

    public StripeNotConfiguredException(Guid tenantId)
        : base("STRIPE_NOT_CONFIGURED", $"Stripe is not configured for tenant {tenantId}")
    {
        TenantId = tenantId;
    }
}

/// <summary>
/// Thrown when Stripe API call fails.
/// HTTP 502
/// </summary>
public class StripeApiException : PaymentException
{
    public string? StripeErrorCode { get; }
    public string? StripeErrorType { get; }

    public StripeApiException(string message, string? errorCode = null, string? errorType = null)
        : base("STRIPE_API_ERROR", message)
    {
        StripeErrorCode = errorCode;
        StripeErrorType = errorType;
    }

    public StripeApiException(string message, Exception innerException)
        : base("STRIPE_API_ERROR", message, innerException)
    {
    }
}

/// <summary>
/// Thrown when Stripe webhook signature is invalid.
/// HTTP 401
/// </summary>
public class StripeWebhookSignatureException : PaymentException
{
    public StripeWebhookSignatureException()
        : base("STRIPE_INVALID_SIGNATURE", "Invalid Stripe webhook signature")
    {
    }
}

#endregion
