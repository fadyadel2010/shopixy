using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Durable compensation command for provider payments that were captured externally
/// but could not be accepted by the CommerceHub order lifecycle.
/// </summary>
public class CommerceHubPaymentCompensationOutboxMessage : BaseEntity
{
    public Guid PaymentId { get; set; }
    public Guid OrderId { get; set; }
    public Guid? TenantId { get; set; }

    public string AffectedStoreIdsJson { get; set; } = "[]";
    public string StoreAllocationSnapshotJson { get; set; } = "[]";
    public string RefundAllocationSnapshotJson { get; set; } = "[]";

    public PaymentProvider Provider { get; set; }

    [MaxLength(200)]
    public string? ProviderIntentId { get; set; }

    [MaxLength(200)]
    public string? ProviderTransactionId { get; set; }

    public decimal Amount { get; set; }

    [Required]
    [MaxLength(10)]
    public string Currency { get; set; } = "EGP";

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? CorrelationId { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }
    public int AttemptCount { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    public DateTime? NextRetryAtUtc { get; set; }
    public Guid? ProcessingLeaseId { get; set; }
    public DateTime? ProcessingLeaseExpiresAtUtc { get; set; }
    public bool IsFailed { get; set; }

    [MaxLength(200)]
    public string? GatewayReference { get; set; }

    [MaxLength(100)]
    public string? GatewayStatus { get; set; }

    public DateTime? GatewayRequestStartedAtUtc { get; set; }
    public DateTime? GatewayOutcomeUncertainAtUtc { get; set; }
    public bool RequiresOperatorReconciliation { get; set; }
}
