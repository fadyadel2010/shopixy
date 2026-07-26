using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Stores unresolved payment webhooks from CommerceHub providers for manual resolution.
/// 
/// Data Retention Policy:
/// - Records should be reviewed and resolved within 30 days of creation.
/// - Resolved records should be archived or deleted after 90 days.
/// - RawPayload contains PII-redacted webhook data for debugging purposes only.
/// - PayloadHash can be used to verify payload integrity without storing sensitive data.
/// </summary>
public class CommerceHubUnresolvedPaymentWebhook : BaseEntity
{
    public PaymentProvider Provider { get; set; }

    [Required]
    [MaxLength(200)]
    public string ProviderEventId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? ProviderReference { get; set; }

    public Guid? LocalPaymentId { get; set; }

    [MaxLength(200)]
    public string? ProviderIntentId { get; set; }

    [MaxLength(200)]
    public string? ProviderTransactionId { get; set; }

    public Guid? TenantId { get; set; }
    public Guid? OrderId { get; set; }

    public string AffectedStoreIdsJson { get; set; } = "[]";

    public string SuspenseRoutedStoreIdsJson { get; set; } = "[]";

    public decimal Amount { get; set; }

    [Required]
    [MaxLength(10)]
    public string Currency { get; set; } = "EGP";

    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of the original webhook payload for integrity verification.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string PayloadHash { get; set; } = string.Empty;

    /// <summary>
    /// PII-redacted webhook payload for debugging purposes.
    /// Contains redacted customer/billing information to comply with data retention policies.
    /// Use PayloadHash to verify integrity of the original payload.
    /// </summary>
    public string RawPayload { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "PendingResolution";

    public int ResolutionAttemptCount { get; set; }
    public DateTime? LastResolutionAttemptAtUtc { get; set; }
    public DateTime? RoutedToSuspenseAtUtc { get; set; }

    [MaxLength(100)]
    public string? CorrelationId { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }
}
