using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// PaymentIntent entity - child of Order.
/// Tracks payment attempts and their status.
/// 
/// Phase 4.1: Payments API
/// 
/// For online payments: Status transitions via webhooks
/// For COD: Status is immediately Skipped
/// </summary>
public class PaymentIntent : BaseEntity, ITenantOwned
{
    /// <summary>
    /// PostgreSQL xmin-backed optimistic concurrency token.
    /// Prevents silent overwrites when multiple admins act on the same intent.
    /// </summary>
    public uint RowVersion { get; set; }

    /// <summary>
    /// Parent order
    /// </summary>
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    
    /// <summary>
    /// Tenant isolation
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Payment amount (should match order total at creation)
    /// </summary>
    public decimal Amount { get; set; }
    
    /// <summary>
    /// Currency code (e.g., USD, SAR)
    /// </summary>
    public string Currency { get; set; } = string.Empty;
    
    /// <summary>
    /// Payment method used
    /// </summary>
    public PaymentMethod PaymentMethod { get; set; }
    
    /// <summary>
    /// Payment intent status
    /// </summary>
    public PaymentIntentStatus Status { get; set; } = PaymentIntentStatus.Pending;
    
    /// <summary>
    /// Payment provider (e.g., Stripe, Paymob, Fawry)
    /// </summary>
    public PaymentProvider Provider { get; set; }

    /// <summary>
    /// Fawry Reference Code (for offline payments)
    /// </summary>
    public string? ReferenceCode { get; set; }

    /// <summary>
    /// When the payment reference/code expires
    /// </summary>
    public DateTime? ExpiresAtUtc { get; set; }
    
    /// <summary>
    /// Provider's payment intent/reference ID
    /// </summary>
    public string? ProviderIntentId { get; set; }
    
    /// <summary>
    /// Provider's charge/payment ID after capture
    /// </summary>
    public string? ProviderPaymentId { get; set; }

    /// <summary>
    /// Provider payout batch that settled this payment intent.
    /// Populated from gateway payout/reconciliation feeds and used for exact settlement matching.
    /// </summary>
    public string? PayoutBatchId { get; set; }
    
    /// <summary>
    /// Internal payment reference (PAY-YYYYMMDD-XXXXXXXX)
    /// </summary>
    public string PaymentReference { get; set; } = string.Empty;
    
    /// <summary>
    /// Redirect URL for hosted payment pages (e.g., Paymob iframe URL)
    /// NOTE: This field should NOT be used for sensitive credentials like Stripe client_secret
    /// </summary>
    public string? RedirectUrl { get; set; }
    
    /// <summary>
    /// Encrypted client secret for payment providers (e.g., Stripe client_secret)
    /// Stored encrypted using ASP.NET Core Data Protection API
    /// </summary>
    public string? ClientSecretEncrypted { get; set; }
    
    /// <summary>
    /// Webhook/callback URL for this payment
    /// </summary>
    public string? WebhookUrl { get; set; }
    
    /// <summary>
    /// Idempotency key for creation
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    
    /// <summary>
    /// When payment was authorized (for 2-step payments)
    /// </summary>
    public DateTime? AuthorizedAt { get; set; }
    
    /// <summary>
    /// When payment was captured/completed
    /// </summary>
    public DateTime? CapturedAt { get; set; }
    
    /// <summary>
    /// When payment failed
    /// </summary>
    public DateTime? FailedAt { get; set; }
    
    /// <summary>
    /// Failure reason if status = Failed
    /// </summary>
    public string? FailureReason { get; set; }
    
    /// <summary>
    /// Failure code from provider
    /// </summary>
    public string? FailureCode { get; set; }
    
    /// <summary>
    /// Raw provider response/metadata (JSON)
    /// </summary>
    public string? ProviderMetadata { get; set; }
    
    /// <summary>
    /// Last webhook event ID processed
    /// </summary>
    public string? LastWebhookEventId { get; set; }
    
    /// <summary>
    /// Number of webhook events received
    /// </summary>
    public int WebhookEventCount { get; set; }

    // =========================================================================
    // Manual Wallet Fields
    // =========================================================================

    /// <summary>
    /// Wallet type (VodafoneCash, Instapay, etc.)
    /// </summary>
    public string? WalletType { get; set; }

    /// <summary>
    /// Receiver wallet number
    /// </summary>
    public string? WalletPhoneNumber { get; set; }

    /// <summary>
    /// Proof of payment image URL
    /// </summary>
    public string? ProofImageUrl { get; set; }

    /// <summary>
    /// When payment was manually verified
    /// </summary>
    public DateTime? VerifiedAtUtc { get; set; }

    /// <summary>
    /// Admin who verified the payment
    /// </summary>
    public string? VerifiedByAdmin { get; set; }

    /// <summary>
    /// Reason for rejection if status = Rejected
    /// </summary>
    public string? RejectionReason { get; set; }

    /// <summary>
    /// When the proof image should be deleted (cleanup policy)
    /// </summary>
    public DateTime? ProofImageExpiresAtUtc { get; set; }

    /// <summary>
    /// Whether the proof image has been deleted by the cleanup job
    /// </summary>
    public bool ProofImageDeleted { get; set; }

    /// <summary>
    /// Whether inventory has been committed for this payment.
    /// Used to ensure inventory commitment is recoverable on webhook retry.
    /// </summary>
    public bool InventoryCommitted { get; set; }
}
