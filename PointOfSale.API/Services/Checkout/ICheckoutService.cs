using System.Linq;
using PointOfSale.API.DTOs.Checkout;

namespace PointOfSale.API.Services.Checkout;

/// <summary>
/// Service interface for Checkout operations.
/// 
/// PHASE 3.5: CHECKOUT API (Cart → Order)
/// 
/// ARCHITECTURAL BOUNDARY:
/// - Cart is READ-ONLY during checkout
/// - PostgreSQL is Source of Truth for Orders
/// - Redis is used for: Cart snapshot and owned distributed locks
/// - Checkout idempotency is persisted in PostgreSQL
/// - MongoDB is read-only (validation only)
/// - Checkout is transactional
/// - Payment is NOT executed (intent only)
/// 
/// Checkout Flow:
/// 1. Claim durable idempotency ownership
/// 2. Acquire distributed lock
/// 3. Snapshot cart from Redis
/// 4. Recalculate prices from PostgreSQL
/// 5. Validate and reserve inventory
/// 6. Create order in PostgreSQL (transactional)
/// 7. Generate payment intent (NO payment execution)
/// 8. Persist replay-safe checkout result
/// 
/// Required Headers:
/// - X-Tenant-Id: GUID (required)
/// - X-Session-Id: string (for guest checkout)
/// - X-Customer-Id: GUID (optional)
/// - Idempotency-Key: string (required and stable across retries)
/// - X-Request-Id: optional compatibility header; must match Idempotency-Key when provided
/// </summary>
public interface ICheckoutService
{
    /// <summary>
    /// Builds an authoritative pricing preview for the current cart and checkout selections.
    /// The backend recomputes all monetary fields and returns the exact values the UI must display.
    /// </summary>
    Task<CheckoutPricingPreviewResponseDto> PreviewPricingAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        CheckoutPricingPreviewRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes checkout and creates an order.
    /// 
    /// This method is idempotent - same checkout key replays the stored response.
    /// 
    /// Steps:
    /// 1. Claim persistent checkout ownership by checkout id
    /// 2. Acquire owned lock lease for checkout + cart execution
    /// 3. Snapshot cart from Redis
    /// 4. Recalculate prices from PostgreSQL
    /// 5. Validate and reserve inventory
    /// 6. Create order (PostgreSQL transaction)
    /// 7. Generate payment intent
    /// 8. Persist replay-safe checkout result
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="ownerId">Cart owner ID (session or customer)</param>
    /// <param name="ownerType">Cart owner type ("session" or "customer")</param>
    /// <param name="customerId">Customer ID if authenticated</param>
    /// <param name="requestId">Stable checkout id / idempotency key</param>
    /// <param name="request">Checkout request</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Checkout response with order details</returns>
    /// <exception cref="CheckoutLockException">Lock acquisition failed (409)</exception>
    /// <exception cref="CheckoutCartInvalidException">Cart validation failed (400)</exception>
    /// <exception cref="CheckoutPriceChangedException">Major price change detected (409)</exception>
    /// <exception cref="CheckoutOutOfStockException">Insufficient stock (409)</exception>
    /// <exception cref="CheckoutPromotionCalculationException">Promotion calculation failed (503)</exception>
    /// <exception cref="CheckoutTotalMismatchException">Client pricing does not match backend totals (409)</exception>
    /// <exception cref="CheckoutException">General checkout failure</exception>
    Task<CheckoutResponseDto> ProcessCheckoutAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        string requestId,
        CheckoutRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default);
}

#region Checkout Exceptions

/// <summary>
/// Base exception for checkout failures.
/// </summary>
public class CheckoutException : Exception
{
    public string ErrorCode { get; }
    public string Step { get; }

    public CheckoutException(string errorCode, string message, string step) 
        : base(message)
    {
        ErrorCode = errorCode;
        Step = step;
    }

    public CheckoutException(string errorCode, string message, string step, Exception innerException) 
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Step = step;
    }
}

/// <summary>
/// Thrown when SQL StoreConfig authority says the storefront cannot accept new orders.
/// </summary>
public sealed class CheckoutStorefrontUnavailableException : CheckoutException
{
    public string? StorefrontStatus { get; }

    public CheckoutStorefrontUnavailableException(string? storefrontStatus)
        : base(
            "STOREFRONT_NOT_ACCEPTING_ORDERS",
            "This storefront is not accepting orders right now.",
            "StorefrontAvailability")
    {
        StorefrontStatus = storefrontStatus;
    }
}

/// <summary>
/// Thrown when checkout lock cannot be acquired.
/// HTTP 409 Conflict - CHECKOUT_IN_PROGRESS
/// </summary>
public class CheckoutLockException : CheckoutException
{
    public Guid CartId { get; }

    public CheckoutLockException(Guid cartId) 
        : base("CHECKOUT_IN_PROGRESS", 
            $"Another checkout is in progress for cart {cartId}", 
            "Lock")
    {
        CartId = cartId;
    }
}

/// <summary>
/// Thrown when cart is invalid for checkout.
/// HTTP 400 Bad Request - CART_INVALID
/// </summary>
public class CheckoutCartInvalidException : CheckoutException
{
    public Guid CartId { get; }

    public CheckoutCartInvalidException(Guid cartId, string reason) 
        : base("CART_INVALID", 
            $"Cart {cartId} is invalid: {reason}", 
            "CartSnapshot")
    {
        CartId = cartId;
    }
}

/// <summary>
/// Thrown when cart not found.
/// HTTP 404 Not Found - CART_NOT_FOUND
/// </summary>
public class CheckoutCartNotFoundException : CheckoutException
{
    public Guid CartId { get; }

    public CheckoutCartNotFoundException(Guid cartId) 
        : base("CART_NOT_FOUND", 
            $"Cart {cartId} not found or expired", 
            "CartSnapshot")
    {
        CartId = cartId;
    }
}

/// <summary>
/// Thrown when price recalculation detects major change.
/// HTTP 409 Conflict - PRICE_CHANGED
/// </summary>
public class CheckoutPriceChangedException : CheckoutException
{
    public decimal OriginalTotal { get; }
    public decimal NewTotal { get; }
    public decimal DifferencePercent { get; }

    public CheckoutPriceChangedException(decimal originalTotal, decimal newTotal, decimal differencePercent) 
        : base("PRICE_CHANGED", 
            $"Cart prices have changed significantly. Original: {originalTotal:C}, New: {newTotal:C} ({differencePercent:F1}% difference)", 
            "PriceRecalculation")
    {
        OriginalTotal = originalTotal;
        NewTotal = newTotal;
        DifferencePercent = differencePercent;
    }
}

/// <summary>
/// Thrown when promotion calculation fails and checkout must stop.
/// HTTP 503 Service Unavailable - PROMOTION_CALCULATION_FAILED
/// </summary>
public class CheckoutPromotionCalculationException : CheckoutException
{
    public CheckoutPromotionCalculationException(string message)
        : base("PROMOTION_CALCULATION_FAILED", message, "PromotionCalculation")
    {
    }

    public CheckoutPromotionCalculationException(string message, Exception innerException)
        : base("PROMOTION_CALCULATION_FAILED", message, "PromotionCalculation", innerException)
    {
    }
}

/// <summary>
/// Thrown when client-submitted checkout totals do not match authoritative pricing.
/// HTTP 409 Conflict - CHECKOUT_TOTAL_MISMATCH
/// </summary>
public class CheckoutTotalMismatchException : CheckoutException
{
    public IReadOnlyCollection<string> MismatchedFields { get; }

    public CheckoutTotalMismatchException(IEnumerable<string> mismatchedFields)
        : base(
            "CHECKOUT_TOTAL_MISMATCH",
            "Checkout totals changed. Please refresh pricing and review the updated total before placing the order.",
            "TotalValidation")
    {
        MismatchedFields = mismatchedFields
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

/// <summary>
/// Thrown when inventory is insufficient.
/// HTTP 409 Conflict - OUT_OF_STOCK
/// </summary>
public class CheckoutOutOfStockException : CheckoutException
{
    public Guid ProductId { get; }
    public string SKU { get; }
    public int RequestedQuantity { get; }
    public int AvailableQuantity { get; }

    public CheckoutOutOfStockException(Guid productId, string sku, int requested, int available) 
        : base("OUT_OF_STOCK", 
            $"Insufficient stock for {sku}: requested {requested}, available {available}", 
            "InventoryValidation")
    {
        ProductId = productId;
        SKU = sku;
        RequestedQuantity = requested;
        AvailableQuantity = available;
    }
}

public class CheckoutDynamicFieldValidationException : CheckoutException
{
    public CheckoutDynamicFieldValidationException(string errorCode, string message)
        : base(errorCode, message, "CheckoutDynamicFields")
    {
    }
}

/// <summary>
/// Thrown when the final authoritative checkout commit loses a serializable race.
/// HTTP 409 Conflict - CHECKOUT_CONCURRENCY_CONFLICT
/// </summary>
public sealed class CheckoutConcurrencyConflictException : CheckoutException
{
    public Guid CartId { get; }
    public string CorrelationId { get; }
    public int AttemptCount { get; }

    public CheckoutConcurrencyConflictException(
        Guid cartId,
        string correlationId,
        int attemptCount,
        Exception? innerException = null)
        : base(
            "CHECKOUT_CONCURRENCY_CONFLICT",
            "Checkout authority changed while your order was being committed. Please review the latest totals and retry.",
            "OrderCreation",
            innerException!)
    {
        CartId = cartId;
        CorrelationId = correlationId;
        AttemptCount = attemptCount;
    }
}

/// <summary>
/// Thrown when shipping method is invalid.
/// HTTP 400 Bad Request - INVALID_SHIPPING_METHOD
/// </summary>
public class CheckoutShippingMethodException : CheckoutException
{
    public Guid ShippingMethodId { get; }

    public CheckoutShippingMethodException(Guid shippingMethodId, string reason) 
        : base("INVALID_SHIPPING_METHOD", 
            $"Shipping method {shippingMethodId} is invalid: {reason}", 
            "Validation")
    {
        ShippingMethodId = shippingMethodId;
    }
}

/// <summary>
/// Thrown when order creation fails.
/// HTTP 500 Internal Server Error - ORDER_CREATION_FAILED
/// </summary>
public class CheckoutOrderCreationException : CheckoutException
{
    public CheckoutOrderCreationException(string reason, Exception? innerException = null) 
        : base("ORDER_CREATION_FAILED", 
            $"Failed to create order: {reason}", 
            "OrderCreation",
            innerException!)
    {
    }
}

/// <summary>
/// Thrown when post-commit consistency checks fail (e.g., missing outbox messages).
/// This exception is NOT caught by the OrderCreation catch block, allowing recovery/replay logic to run.
/// HTTP 500 Internal Server Error - POST_COMMIT_CONSISTENCY_FAILURE
/// </summary>
public class CheckoutPostCommitConsistencyException : Exception
{
    public Guid TenantId { get; }
    public Guid OrderId { get; }
    public string CorrelationId { get; }

    public CheckoutPostCommitConsistencyException(
        Guid tenantId,
        Guid orderId,
        string correlationId,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        TenantId = tenantId;
        OrderId = orderId;
        CorrelationId = correlationId;
    }
}

/// <summary>
/// Thrown when cart ownership doesn't match.
/// HTTP 403 Forbidden - CART_OWNERSHIP_MISMATCH
/// </summary>
public class CheckoutCartOwnershipException : CheckoutException
{
    public Guid CartId { get; }

    public CheckoutCartOwnershipException(Guid cartId) 
        : base("CART_OWNERSHIP_MISMATCH", 
            $"Cart {cartId} does not belong to this tenant/owner", 
            "CartValidation")
    {
        CartId = cartId;
    }
}

/// <summary>
/// Thrown when an authenticated storefront customer is no longer allowed to create checkout authority.
/// HTTP 401 Unauthorized - CUSTOMER_INACTIVE
/// </summary>
public class CheckoutCustomerInactiveException : CheckoutException
{
    public Guid CustomerId { get; }

    public CheckoutCustomerInactiveException(Guid customerId)
        : base(
            "CUSTOMER_INACTIVE",
            "Customer account is not active.",
            "CustomerAuthority")
    {
        CustomerId = customerId;
    }
}

/// <summary>
/// Thrown when payment method is invalid or disabled.
/// HTTP 400 Bad Request - INVALID_PAYMENT_METHOD
/// PHASE 10.4.D3: COD Payment Integration
/// </summary>
public class CheckoutPaymentMethodException : CheckoutException
{
    public string PaymentMethodCode { get; }

    public CheckoutPaymentMethodException(string paymentMethodCode, string reason) 
        : base("INVALID_PAYMENT_METHOD", 
            $"Payment method {paymentMethodCode} is invalid: {reason}", 
            "PaymentMethodValidation")
    {
        PaymentMethodCode = paymentMethodCode;
    }
}

/// <summary>
/// Thrown when a valid selected provider fails its current execution-readiness gate.
/// HTTP 503 Service Unavailable.
/// </summary>
public sealed class CheckoutPaymentRuntimeUnavailableException : CheckoutException
{
    public string PaymentMethodCode { get; }
    public string FirstFailedGate { get; }

    public CheckoutPaymentRuntimeUnavailableException(string paymentMethodCode, string firstFailedGate)
        : base(
            "PAYMENT_RUNTIME_UNAVAILABLE",
            $"Payment method {paymentMethodCode} is temporarily unavailable.",
            "PaymentMethodValidation")
    {
        PaymentMethodCode = paymentMethodCode;
        FirstFailedGate = firstFailedGate;
    }
}

/// <summary>
/// Thrown when checkout lock infrastructure is unavailable.
/// HTTP 503 Service Unavailable.
/// </summary>
public class CheckoutLockInfrastructureException : CheckoutException
{
    public Guid CartId { get; }

    public CheckoutLockInfrastructureException(Guid cartId)
        : base(
            "CHECKOUT_LOCK_INFRASTRUCTURE_FAILURE",
            "Checkout is temporarily unavailable because the locking infrastructure could not be reached.",
            "Lock")
    {
        CartId = cartId;
    }
}

/// <summary>
/// Thrown when manual wallet checkout is requested without a valid merchant configuration.
/// HTTP 400 Bad Request - MANUAL_WALLET_NOT_CONFIGURED
/// </summary>
public class CheckoutManualWalletNotConfiguredException : CheckoutException
{
    public Guid TenantId { get; }

    public CheckoutManualWalletNotConfiguredException(Guid tenantId)
        : base(
            "MANUAL_WALLET_NOT_CONFIGURED",
            "Manual wallet payment is unavailable because the merchant wallet is not configured.",
            "PaymentMethodValidation")
    {
        TenantId = tenantId;
    }
}

/// <summary>
/// Thrown when manual wallet checkout references a missing or untrusted proof file.
/// HTTP 400 Bad Request - INVALID_MANUAL_WALLET_PROOF
/// </summary>
public class CheckoutManualWalletProofException : CheckoutException
{
    public CheckoutManualWalletProofException(string message)
        : base(
            "INVALID_MANUAL_WALLET_PROOF",
            message,
            "PaymentMethodValidation")
    {
    }
}

/// <summary>
/// Thrown when the same checkout id is reused with a different payload.
/// HTTP 409 Conflict - CHECKOUT_IDEMPOTENCY_CONFLICT
/// </summary>
public class CheckoutIdempotencyConflictException : CheckoutException
{
    public string CheckoutId { get; }

    public CheckoutIdempotencyConflictException(string checkoutId, string? message = null)
        : base(
            "CHECKOUT_IDEMPOTENCY_CONFLICT",
            string.IsNullOrWhiteSpace(message)
                ? "Idempotency-Key was already used for a different checkout payload."
                : message,
            "Idempotency")
    {
        CheckoutId = checkoutId;
    }
}

/// <summary>
/// Thrown when the same checkout id is already in progress.
/// HTTP 409 Conflict - CHECKOUT_IN_PROGRESS
/// </summary>
public class CheckoutIdempotencyInProgressException : CheckoutException
{
    public string CheckoutId { get; }

    public CheckoutIdempotencyInProgressException(string checkoutId)
        : base(
            "CHECKOUT_IN_PROGRESS",
            "A checkout with the same idempotency key is already being processed.",
            "Idempotency")
    {
        CheckoutId = checkoutId;
    }
}

/// <summary>
/// Thrown when the provided phone number is blocked by the merchant.
/// HTTP 403 Forbidden - PHONE_BLOCKED
/// </summary>
public class CheckoutPhoneBlockedException : CheckoutException
{
    public string PhoneNumber { get; }

    public CheckoutPhoneBlockedException(string phoneNumber)
        : base(
            "PHONE_BLOCKED",
            "This phone number cannot place new orders.",
            "PhoneBlockValidation")
    {
        PhoneNumber = phoneNumber;
    }
}

#endregion
