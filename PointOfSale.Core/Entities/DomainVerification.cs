using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Verification attempt record for a Domain — tracks individual DNS/HTTP
/// verification attempts.
///
/// DOMAIN-1.5 Section 4.2 — Persistence Freeze Specification.
/// The entity shape MUST match DOMAIN-1.5 exactly. No redesign.
///
/// Grounded In:
///   - DOMAIN-1 Section 3.1 (DomainVerification entity definition)
///   - DOMAIN-1.5 CPD-5: Soft-deleted with parent Domain
/// </summary>
public class DomainVerification
{
    /// <summary>
    /// Primary key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key to the parent Domain.
    /// FK → Domains.Id. NOT NULL — every verification belongs to exactly one domain.
    /// </summary>
    public Guid DomainId { get; set; }

    /// <summary>
    /// DNS/HTTP verification method used for this attempt.
    /// Stored as smallint.
    /// </summary>
    public VerificationMethod Method { get; set; }

    /// <summary>
    /// The verification token that was served in DNS or HTTP.
    /// Max 256 characters.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Outcome of this verification attempt.
    /// Stored as smallint.
    /// </summary>
    public VerificationStatus Status { get; set; }

    /// <summary>
    /// When this verification attempt was last checked.
    /// </summary>
    public DateTime AttemptedAtUtc { get; set; }

    /// <summary>
    /// When the verification was confirmed successful.
    /// Null until a successful verification.
    /// </summary>
    public DateTime? VerifiedAtUtc { get; set; }

    /// <summary>
    /// Reason for failure, if Status is Failed.
    /// Max 500 characters. Null on success.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Correlation ID for request/job tracing.
    /// Max 128 characters.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Resolver endpoint that answered the lookup, for audit diagnostics.
    /// Max 128 characters.
    /// </summary>
    public string? ResolverEndpoint { get; set; }

    /// <summary>
    /// Lookup latency in milliseconds.
    /// </summary>
    public int? LatencyMilliseconds { get; set; }

    /// <summary>
    /// 1-indexed verification attempt number for the domain.
    /// </summary>
    public int Attempt { get; set; }

    /// <summary>
    /// DNS TTL observed during lookup, if applicable.
    /// </summary>
    public int? Ttl { get; set; }

    /// <summary>
    /// Actual DNS/HTTP value resolved during the attempt.
    /// Max 1024 characters. Null when no value was returned.
    /// </summary>
    public string? ResolvedValue { get; set; }

    /// <summary>
    /// Record creation timestamp (server UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last update timestamp (server UTC).
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Soft-delete flag.
    /// Global query filter: WHERE IsDeleted = false
    /// (cascade soft-delete is handled at the application layer via the
    /// Domain query filter, which hides all entities belonging to a deleted Domain).
    /// </summary>
    public bool IsDeleted { get; set; }

    // =========================================================================
    // Navigation Properties
    // =========================================================================

    /// <summary>
    /// The parent Domain that this verification attempt belongs to.
    /// </summary>
    public Domain Domain { get; set; } = null!;
}
