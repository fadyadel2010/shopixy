using PointOfSale.API.DTOs.Payments;
using System.Text.Json.Serialization;
using PaymentException = PointOfSale.API.Services.Payments.PaymentException;

namespace PointOfSale.API.Services.Payments.Paymob;

/// <summary>
/// Paymob Payment Service interface.
/// 
/// PHASE 10.4.D5: Paymob Integration (Production-Grade, Stripe-Level)
/// 
/// ARCHITECTURAL RULES (STRICT):
/// - Uses Paymob Intention API (modern approach)
/// - Order is created BEFORE payment confirmation
/// - Paymob confirms payment against existing Order
/// - Webhooks are source of truth for final payment status
/// - Secrets are encrypted via Data Protection API
/// - Tenant-aware (each tenant has their own Paymob config)
/// 
/// FORBIDDEN PATTERNS:
/// - ❌ Hardcoding Paymob keys
/// - ❌ Storing secrets in MongoDB or frontend
/// - ❌ Recalculating totals in Paymob flow
/// - ❌ Creating orders after payment without idempotency
/// - ❌ Mixing Paymob logic with Cart or Coupon services
/// - ❌ Assuming a single Paymob account globally
/// - ❌ Trusting frontend payment confirmation
/// </summary>
public interface IPaymobPaymentService
{
    /// <summary>
    /// Creates a Paymob payment intention for an existing order.
    /// 
    /// Prerequisites:
    /// - Order must exist with PaymentMethodCode = "PAYMOB"
    /// - Order must be in PendingPayment status
    /// - Tenant must have Paymob configured and enabled
    /// 
    /// Flow:
    /// 1. Authenticate with Paymob API
    /// 2. Create payment intention
    /// 3. Get payment key
    /// 4. Store PaymentIntent record locally
    /// 
    /// Returns:
    /// - iframeUrl: For redirecting customer to Paymob payment page
    /// - paymentKey: For embedding in iframe
    /// - intentionId: For tracking
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="orderId">Order ID</param>
    /// <param name="customerId">Optional online customer ID</param>
    /// <param name="promotionCalculationFailed">Whether promotion calculation failed during checkout</param>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paymob payment intention response with iframe URL</returns>
    Task<PaymobPaymentIntentResponseDto> CreatePaymentIntentAsync(
        Guid tenantId,
        Guid orderId,
        Guid? customerId,
        bool promotionCalculationFailed,
        string correlationId,
        CancellationToken cancellationToken = default,
        string? idempotencyKey = null);

    /// <summary>
    /// Retrieves a Paymob payment status.
    /// 
    /// Used for:
    /// - Polling payment status from frontend
    /// - Verifying payment before showing success
    /// 
    /// SECURITY: Validates customer ownership before returning status.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="orderId">Order ID</param>
    /// <param name="customerId">Optional online customer ID for ownership validation</param>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Payment status</returns>
    Task<PaymobPaymentStatusDto> GetPaymentStatusAsync(
        Guid tenantId,
        Guid orderId,
        Guid? customerId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a Paymob webhook/callback event.
    /// 
    /// Handles:
    /// - Transaction processed → Mark order as Paid (if success)
    /// - Transaction failed → Mark payment failed
    /// 
    /// Rules:
    /// - Validate HMAC signature using webhook secret
    /// - Idempotent processing
    /// - Load order using order_id from metadata
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="hmacSignature">HMAC signature from Paymob</param>
    /// <param name="rawPayload">Raw webhook body</param>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Webhook processing result</returns>
    Task<PaymobWebhookResultDto> ProcessWebhookAsync(
        Guid tenantId,
        string hmacSignature,
        string rawPayload,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates callback parameters from Paymob redirect.
    /// 
    /// Called when customer returns from Paymob payment page.
    /// Frontend should then poll for payment status.
    /// </summary>
    /// <param name="callbackParams">Callback query parameters</param>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Callback validation result</returns>
    Task<PaymobCallbackResultDto> ValidateCallbackAsync(
        Dictionary<string, string> callbackParams,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates if Paymob is configured and enabled for a tenant.
    /// 
    /// Used by:
    /// - Checkout flow to determine if Paymob is available
    /// - Admin UI to show configuration status
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paymob configuration status</returns>
    Task<PaymobConfigurationStatusDto> GetConfigurationStatusAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

#region DTOs

/// <summary>
/// Paymob payment intention creation response.
/// </summary>
public class PaymobPaymentIntentResponseDto
{
    /// <summary>
    /// URL to redirect customer to Paymob payment page.
    /// </summary>
    public string IframeUrl { get; set; } = string.Empty;

    /// <summary>
    /// Payment key for embedding in iframe.
    /// </summary>
    [JsonIgnore]
    public string PaymentKey { get; set; } = string.Empty;

    /// <summary>
    /// Paymob intention ID.
    /// </summary>
    public string IntentionId { get; set; } = string.Empty;

    /// <summary>
    /// Internal payment intent ID.
    /// </summary>
    public Guid PaymentIntentId { get; set; }

    /// <summary>
    /// Absolute expiry of the storefront Paymob handoff.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

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
    /// Currency code (e.g., EGP, SAR).
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Paymob transaction ID (if available).
    /// </summary>
    public string? TransactionId { get; set; }
}

/// <summary>
/// Paymob payment status response.
/// </summary>
public class PaymobPaymentStatusDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string PaymobStatus { get; set; } = string.Empty;
    public bool IsPaid { get; set; }
    public bool IsPending { get; set; }
    public bool CanRetryPayment { get; set; }
    public string? FailureReason { get; set; }
    public string? TransactionId { get; set; }
}

/// <summary>
/// Paymob webhook processing result.
/// </summary>
public class PaymobWebhookResultDto
{
    public bool Processed { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? EventLogId { get; set; }
    public string? OrderId { get; set; }
    public string? TransactionId { get; set; }
    public string? EventType { get; set; }
}

/// <summary>
/// Paymob callback validation result.
/// </summary>
public class PaymobCallbackResultDto
{
    public bool IsValid { get; set; }
    public bool IsSuccess { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? PaymentIntentId { get; set; }
    public string? StorefrontBaseUrl { get; set; }
    public string? TransactionId { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Paymob configuration status for a tenant.
/// </summary>
public class PaymobConfigurationStatusDto
{
    public bool IsEnabled { get; set; }
    public bool IsConfigured { get; set; }
    public bool IsSandbox { get; set; }
    public string? ValidationMessage { get; set; }
}

/// <summary>
/// Paymob configuration stored in TenantPaymentMethod.ConfigEncrypted.
/// </summary>
public class TenantPaymobConfiguration
{
    /// <summary>
    /// API Key from Paymob dashboard (Settings > Account Info > API Key).
    /// Used for server-side authentication.
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Intention API Secret Key. Server-side only.
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Integration ID from Paymob dashboard (Developers > Payment Integrations).
    /// Identifies the specific payment integration (e.g., card payments).
    /// </summary>
    public string IntegrationId { get; set; } = string.Empty;

    /// <summary>
    /// iFrame ID from Paymob dashboard (Developers > iFrames).
    /// Used for embedding payment form.
    /// </summary>
    public string? IframeId { get; set; }

    /// <summary>
    /// HMAC Secret from Paymob dashboard (Settings > Account Info > HMAC).
    /// Used for webhook signature verification.
    /// </summary>
    public string HmacSecret { get; set; } = string.Empty;

    /// <summary>
    /// Optional account metadata. The Intention API does not require it.
    /// </summary>
    public string? MerchantId { get; set; }

    /// <summary>
    /// Optional legacy API key for dormant legacy-only operations.
    /// </summary>
    public string? ApiKey { get; set; }
}

#endregion

#region Exceptions

/// <summary>
/// Thrown when Paymob is not configured for a tenant.
/// HTTP 400
/// </summary>
public class PaymobNotConfiguredException : PaymentException
{
    public Guid TenantId { get; }

    public PaymobNotConfiguredException(Guid tenantId)
        : base("PAYMOB_NOT_CONFIGURED", $"Paymob is not configured for tenant {tenantId}")
    {
        TenantId = tenantId;
    }
}

/// <summary>
/// Thrown when Paymob API call fails.
/// HTTP 502
/// </summary>
public class PaymobApiException : PaymentException
{
    public string? PaymobErrorCode { get; }
    public string? PaymobErrorType { get; }

    public PaymobApiException(string message, string? errorCode = null, string? errorType = null)
        : base("PAYMOB_API_ERROR", message)
    {
        PaymobErrorCode = errorCode;
        PaymobErrorType = errorType;
    }

    public PaymobApiException(string message, Exception innerException)
        : base("PAYMOB_API_ERROR", message, innerException)
    {
    }
}

/// <summary>
/// Thrown when Paymob webhook signature is invalid.
/// HTTP 401
/// </summary>
public class PaymobWebhookSignatureException : PaymentException
{
    public PaymobWebhookSignatureException()
        : base("PAYMOB_INVALID_SIGNATURE", "Invalid Paymob webhook signature")
    {
    }
}

/// <summary>
/// Thrown when Paymob authentication fails.
/// HTTP 401
/// </summary>
public class PaymobAuthenticationException : PaymentException
{
    public PaymobAuthenticationException(string message)
        : base("PAYMOB_AUTH_FAILED", message)
    {
    }
}

#endregion
