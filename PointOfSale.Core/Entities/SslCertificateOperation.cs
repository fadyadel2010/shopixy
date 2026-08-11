using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Durable idempotency, lease, and fencing record for SSL/TLS certificate
/// operations. This is persistence-only foundation; provider/edge execution
/// belongs to later DOMAIN-5 waves.
/// </summary>
public class SslCertificateOperation : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DomainId { get; set; }
    public Guid? CertificateId { get; set; }
    public string NormalizedHostName { get; set; } = string.Empty;

    public CertificateOperationType OperationType { get; set; }
    public CertificateOperationStatus Status { get; set; }
    public CertificateOperationPhase Phase { get; set; }

    public Guid OperationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? RequestHash { get; set; }

    public string? ProviderRequestReference { get; set; }
    public string? EdgeCertificateReference { get; set; }

    public long FencingToken { get; set; }
    public Guid? LockOwnerReference { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }

    public int AttemptCount { get; set; }
    public DateTime? RetryAfterUtc { get; set; }
    public string? FailureCode { get; set; }
    public string? SafeFailureReason { get; set; }
    public bool Retryable { get; set; }
    public string? CorrelationId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public uint RowVersion { get; set; }

    public Domain Domain { get; set; } = null!;
    public SslCertificate? Certificate { get; set; }
}
