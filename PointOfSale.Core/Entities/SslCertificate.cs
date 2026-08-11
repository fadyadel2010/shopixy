using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// SSL/TLS certificate entity. Owns safe certificate metadata, lifecycle state,
/// opaque provider/edge references, generation, idempotency correlation, and
/// concurrency/fencing metadata for a single domain certificate generation.
///
/// Private keys, PEM/PFX/PKCS12 material, ACME secrets, challenge credentials,
/// provider credentials, and raw Caddy storage contents must never be persisted
/// here or in history/audit snapshots.
/// </summary>
public class SslCertificate : ITenantOwned
{
    public Guid Id { get; set; }

    /// <summary>
    /// Tenant ownership is explicit so certificate metadata is never safely
    /// addressable by certificate id alone.
    /// </summary>
    public Guid TenantId { get; set; }

    public Guid DomainId { get; set; }
    public string NormalizedHostName { get; set; } = string.Empty;

    /// <summary>
    /// Legacy provider enum retained for existing read DTOs.
    /// </summary>
    public ProviderType Provider { get; set; }

    public string? ProviderImplementation { get; set; }
    public string? ProviderAccountReference { get; set; }
    public string? ProviderEnvironment { get; set; }

    /// <summary>
    /// Single authoritative business lifecycle status.
    /// </summary>
    public CertificateStatus Status { get; set; }

    /// <summary>
    /// Optional progress phase. This is not an alternate lifecycle authority.
    /// </summary>
    public CertificateOperationPhase OperationPhase { get; set; }

    /// <summary>
    /// Distinct certificate generation for a domain. Renewals/replacements
    /// create a new row with a higher generation instead of overwriting active
    /// certificate metadata.
    /// </summary>
    public int Generation { get; set; }

    public Guid? OperationId { get; set; }
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Legacy opaque provider certificate reference. Do not store SAN JSON or
    /// certificate/private-key material in this field.
    /// </summary>
    public string? CertificateArn { get; set; }

    public string? ProviderRequestReference { get; set; }
    public string? OrderReference { get; set; }
    public string? AuthorizationReference { get; set; }
    public string? ChallengeReference { get; set; }
    public string? EdgeCertificateReference { get; set; }
    public string? CaddyStorageReference { get; set; }

    public string? Issuer { get; set; }
    public string? Subject { get; set; }
    public string SubjectAlternativeNamesJson { get; set; } = "[]";
    public string? Thumbprint { get; set; }
    public string? Fingerprint { get; set; }
    public string? SerialNumber { get; set; }
    public string? SignatureAlgorithm { get; set; }
    public DateTime? NotBeforeUtc { get; set; }
    public DateTime? NotAfterUtc { get; set; }
    public DateTime? IssuedAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? ExpiredAtUtc { get; set; }

    public string? EdgeCluster { get; set; }
    public string? EdgeIdentifier { get; set; }
    public int? DeployedGeneration { get; set; }
    public int? ObservedGeneration { get; set; }
    public DateTime? LastEdgeProbeAtUtc { get; set; }
    public string? LastObservedThumbprint { get; set; }
    public DateTime? ActivationStartedAtUtc { get; set; }
    public string? ExpectedThumbprint { get; set; }
    public string? ActivationFailureCode { get; set; }
    public string? ActivationFailureReason { get; set; }
    public DateTime? ActivationFailedAtUtc { get; set; }

    public bool RenewalEligible { get; set; }
    public DateTime? LastRenewalAttemptUtc { get; set; }
    public DateTime? NextRenewalUtc { get; set; }
    public int RenewalAttemptCount { get; set; }
    public DateTime? RetryAfterUtc { get; set; }
    public string? BackoffReason { get; set; }
    public Guid? ReplacementCertificateId { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
    public ActorType? RevokedByActorType { get; set; }
    public Guid? RevokedByActorId { get; set; }
    public string? RevocationReason { get; set; }
    public string? ProviderRevocationReference { get; set; }
    public DateTime? EdgeDeactivationAtUtc { get; set; }

    public string? FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public string? SafeFailureReason { get; set; }
    public string? ProviderErrorClass { get; set; }
    public bool Retryable { get; set; }
    public DateTime? LastFailureAtUtc { get; set; }
    public string? CorrelationId { get; set; }

    public long FencingToken { get; set; }
    public Guid? LockOwnerReference { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public uint RowVersion { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? LastOperationAuditId { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>
    /// Safe metadata-only history. No private material or secret-bearing
    /// provider payloads may be stored in this JSON.
    /// </summary>
    public string SslCertificateHistoryJson { get; set; } = "[]";

    public Domain Domain { get; set; } = null!;
    public SslCertificate? ReplacementCertificate { get; set; }
    public ICollection<SslCertificateOperation> Operations { get; set; } = new List<SslCertificateOperation>();

    public static bool IsTerminal(CertificateStatus status) =>
        status is CertificateStatus.Revoked or CertificateStatus.Expired or CertificateStatus.Retired;

    public bool CanTransitionTo(CertificateStatus nextStatus)
    {
        if (Status == nextStatus)
        {
            return true;
        }

        if (IsTerminal(Status))
        {
            return false;
        }

        if (Status == CertificateStatus.Failed && nextStatus == CertificateStatus.Active && !Retryable)
        {
            return false;
        }

        return nextStatus switch
        {
            CertificateStatus.Pending => Status == CertificateStatus.Failed && Retryable,
            CertificateStatus.Requested => Status is CertificateStatus.Pending or CertificateStatus.Failed,
            CertificateStatus.Authorizing => Status == CertificateStatus.Requested,
            CertificateStatus.Issued => Status is CertificateStatus.Authorizing or CertificateStatus.Renewing,
            CertificateStatus.Activating => Status == CertificateStatus.Issued,
            CertificateStatus.Active => Status == CertificateStatus.Activating,
            CertificateStatus.Renewing => Status == CertificateStatus.Active,
            CertificateStatus.Retired => Status == CertificateStatus.Renewing,
            CertificateStatus.Revoked => true,
            CertificateStatus.Expired => true,
            CertificateStatus.Failed => true,
            _ => false,
        };
    }

    public void TransitionTo(CertificateStatus nextStatus)
    {
        if (!CanTransitionTo(nextStatus))
        {
            throw new InvalidOperationException(
                $"Certificate status transition from {Status} to {nextStatus} is not allowed.");
        }

        Status = nextStatus;
        UpdatedAt = DateTime.UtcNow;

        if (nextStatus == CertificateStatus.Revoked)
        {
            RevokedAtUtc ??= UpdatedAt;
        }
        else if (nextStatus == CertificateStatus.Expired)
        {
            ExpiredAtUtc ??= UpdatedAt;
        }
        else if (nextStatus == CertificateStatus.Active)
        {
            ActivatedAtUtc ??= UpdatedAt;
        }
    }
}
