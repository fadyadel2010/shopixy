using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.DTOs.Payments;

/// <summary>
/// Request DTO for creating a payment intent.
/// POST /api/payments/intents/{orderId}
/// </summary>
public class CreatePaymentIntentRequestDto
{
    /// <summary>
    /// Payment provider to use (e.g., stripe, paypal, tap, cod).
    /// If not provided, uses order's payment method.
    /// </summary>
    [StringLength(64)]
    public string? Provider { get; set; }

    /// <summary>
    /// Return URL after successful payment (for hosted pages).
    /// </summary>
    [StringLength(2048)]
    public string? SuccessUrl { get; set; }

    /// <summary>
    /// Return URL after cancelled payment (for hosted pages).
    /// </summary>
    [StringLength(2048)]
    public string? CancelUrl { get; set; }

    /// <summary>
    /// Manual Wallet: Wallet Type (VodafoneCash, Instapay, OrangeCash)
    /// </summary>
    [StringLength(32)]
    public string? WalletType { get; set; }

    /// <summary>
    /// Manual Wallet: Customer Phone Number
    /// </summary>
    [StringLength(32)]
    public string? WalletPhoneNumber { get; set; }

    /// <summary>
    /// Manual Wallet: Server-issued proof file key
    /// </summary>
    [StringLength(512)]
    public string? ProofFileKey { get; set; }
}

/// <summary>
/// Response DTO for payment intent creation.
/// </summary>
public class PaymentIntentResponseDto
{
    /// <summary>
    /// Payment intent ID.
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
    /// Currency code.
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Payment method.
    /// </summary>
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>
    /// Payment provider.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Payment intent status.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Internal payment reference.
    /// </summary>
    public string PaymentReference { get; set; } = string.Empty;

    /// <summary>
    /// Provider's payment intent ID (if applicable).
    /// </summary>
    public string? ProviderIntentId { get; set; }

    /// <summary>
    /// Redirect URL for hosted payment (if applicable).
    /// </summary>
    public string? RedirectUrl { get; set; }

    /// <summary>
    /// Whether this is a COD order (no payment needed).
    /// </summary>
    public bool IsCod { get; set; }

    /// <summary>
    /// When the payment intent was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Fawry Reference Code (for offline payments).
    /// </summary>
    public string? ReferenceCode { get; set; }

    /// <summary>
    /// When the payment reference expires.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; set; }
}

/// <summary>
/// Response DTO for COD confirmation.
/// POST /api/payments/cod/confirm/{orderId}
/// </summary>
public class CodConfirmResponseDto
{
    /// <summary>
    /// Order ID.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Order number.
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// New order status (Confirmed).
    /// </summary>
    public string OrderStatus { get; set; } = "Confirmed";

    /// <summary>
    /// Payment status (Pending - will be paid on delivery).
    /// </summary>
    public string PaymentStatus { get; set; } = "Pending";

    /// <summary>
    /// Order total amount.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Currency code.
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Confirmation timestamp.
    /// </summary>
    public DateTime ConfirmedAt { get; set; }

    /// <summary>
    /// Whether inventory was confirmed.
    /// </summary>
    public bool InventoryConfirmed { get; set; }
}

/// <summary>
/// Webhook payload base DTO.
/// Provider-specific DTOs inherit from this.
/// </summary>
public class WebhookPayloadDto
{
    /// <summary>
    /// Event type from provider.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Provider's event ID.
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// Event timestamp (Unix epoch or ISO 8601).
    /// </summary>
    public string? Timestamp { get; set; }

    /// <summary>
    /// Raw JSON data object (provider-specific).
    /// </summary>
    public string? Data { get; set; }
}

/// <summary>
/// Generic webhook event data for payment processing.
/// </summary>
public class PaymentWebhookEventDto
{
    /// <summary>
    /// Provider name.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Event type.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Provider's event ID.
    /// </summary>
    public string ProviderEventId { get; set; } = string.Empty;

    /// <summary>
    /// Provider's payment intent/session ID.
    /// </summary>
    public string? ProviderIntentId { get; set; }

    /// <summary>
    /// Provider's charge/payment ID.
    /// </summary>
    public string? ProviderPaymentId { get; set; }

    /// <summary>
    /// Our payment reference (passed in metadata).
    /// </summary>
    public string? PaymentReference { get; set; }

    /// <summary>
    /// Our order ID (passed in metadata).
    /// </summary>
    public Guid? OrderId { get; set; }

    /// <summary>
    /// Payment amount from provider.
    /// </summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Currency from provider.
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Payment status from provider (e.g., succeeded, failed).
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Failure reason if failed.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Failure code if failed.
    /// </summary>
    public string? FailureCode { get; set; }

    /// <summary>
    /// Event timestamp.
    /// </summary>
    public DateTime EventTimestamp { get; set; }
}

/// <summary>
/// Standard webhook response (always 200 to acknowledge receipt).
/// </summary>
public class WebhookResponseDto
{
    /// <summary>
    /// Whether the event was processed.
    /// </summary>
    public bool Processed { get; set; }

    /// <summary>
    /// Processing status message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Our event ID for tracking.
    /// </summary>
    public Guid? EventLogId { get; set; }
}

/// <summary>
/// Order status response DTO.
/// </summary>
public class OrderPaymentStatusDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public string Currency { get; set; } = string.Empty;
    public PaymentIntentSummaryDto? LatestPaymentIntent { get; set; }
}

/// <summary>
/// Payment intent summary for order status.
/// </summary>
public class PaymentIntentSummaryDto
{
    public Guid PaymentIntentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}

/// <summary>
/// Canonical payment status response for storefront payment confirmation flows.
/// Success MUST only be represented by PAID.
/// MANUAL_REVIEW means provider cash may exist but local recognition is quarantined
/// pending reconciliation or explicit operator action; the frontend must not
/// present retry-safe failure guidance for that state.
/// </summary>
public class CanonicalPaymentStatusDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentIntentStatus { get; set; }
    public string? PaymentMethod { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
