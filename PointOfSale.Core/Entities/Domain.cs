using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Domain aggregate root — single source of truth for a hostname's lifecycle.
///
/// Tracks ownership verification, SSL state, primary/alias/redirect
/// classification, and canonical URL resolution for storefront custom domains.
///
/// DOMAIN-1.5 Section 4.1 — Persistence Freeze Specification.
/// The entity shape MUST match DOMAIN-1.5 exactly. No redesign.
///
/// Grounded In:
///   - DOMAIN-1 Section 2.1 (Domain entity definition)
///   - DOMAIN-0 Phase 6 (no Domain entity previously existed)
///   - CPD-5: Implements ITenantOwned with System/Development exemption
///   - CPD-11: Version is int (xmin-backed), not byte[]
///   - CPD-6: DomainHistoryJson stored as jsonb
/// </summary>
public class Domain : ITenantOwned
{
    /// <summary>
    /// Primary key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The tenant that owns this domain.
    /// FK → Tenants.Id. NOT NULL at database level.
    /// Exempted from tenant query filter for System and Development types
    /// (platform-global entities).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Original hostname as entered by the merchant (preserves case, may
    /// include port for Development domains).
    /// Max 253 characters per RFC 1035.
    /// </summary>
    public string HostName { get; set; } = string.Empty;

    /// <summary>
    /// Normalized hostname — lowercased + punycode (IDNA ToASCII).
    /// Server-authoritative normalization at write time (CPD-8).
    /// Global UNIQUE constraint enforced at DB level.
    /// Max 253 characters.
    /// </summary>
    public string NormalizedHostName { get; set; } = string.Empty;

    /// <summary>
    /// Classification of this domain's role (Primary, Alias, Redirect,
    /// Preview, Temporary, Development, System, etc.).
    /// Stored as smallint.
    /// </summary>
    public DomainType DomainType { get; set; }

    /// <summary>
    /// Current lifecycle state (Draft, PendingVerification, Verified,
    /// SslPending, SslActive, Primary, Disabled, Archived, Deleted).
    /// Stored as smallint.
    /// </summary>
    public DomainLifecycleState LifecycleState { get; set; }

    /// <summary>
    /// Foreign key to the active SSL certificate.
    /// Nullable — null until SSL is provisioned.
    /// FK → SslCertificates.Id.
    /// </summary>
    public Guid? SslCertificateId { get; set; }

    /// <summary>
    /// Monotonic tenant/domain-scoped fence for every certificate operation
    /// that can mutate edge state. This is deliberately shared across
    /// provisioning, activation, renewal, refresh, and revocation.
    /// </summary>
    public long CertificateOperationFencingToken { get; set; }

    /// <summary>
    /// Cryptographically random token for DNS/HTTP verification proof.
    /// Max 256 characters. Required for verifiable domains.
    /// </summary>
    public string VerificationToken { get; set; } = string.Empty;

    /// <summary>
    /// Authority timestamp for verification-token expiry. General domain edits
    /// must not extend ownership proof validity.
    /// </summary>
    public DateTime? VerificationTokenIssuedAtUtc { get; set; }

    /// <summary>
    /// DNS/HTTP verification method (DnsTxt, DnsCname, HttpFile).
    /// Nullable — null for non-verified or non-verifiable domain types.
    /// Stored as smallint.
    /// </summary>
    public VerificationMethod? VerificationMethod { get; set; }

    /// <summary>
    /// Redirect behavior when DomainType is Redirect.
    /// None (0) means no redirect is configured.
    /// Stored as smallint.
    /// </summary>
    public RedirectType RedirectType { get; set; }

    /// <summary>
    /// Self-referencing FK to the redirect target Domain.
    /// Null unless RedirectType != None.
    /// FK → Domains.Id. Cross-tenant redirect is forbidden at application layer.
    /// </summary>
    public Guid? RedirectTargetDomainId { get; set; }

    /// <summary>
    /// Canonical URL policy for alias domains.
    /// UseAsPrimary or CanonicalizeToPrimary.
    /// Default: UseAsPrimary (value 0).
    /// Stored as smallint.
    /// </summary>
    public CanonicalPolicy CanonicalPolicy { get; set; }

    /// <summary>
    /// When this domain record was created (server UTC).
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When the domain passed DNS/HTTP verification.
    /// Null until verification succeeds.
    /// </summary>
    public DateTime? VerifiedAtUtc { get; set; }

    /// <summary>
    /// When the SSL certificate was activated for this domain.
    /// Null until SSL is active.
    /// </summary>
    public DateTime? SslActivatedAtUtc { get; set; }

    /// <summary>
    /// Last update to this domain record (server UTC).
    /// </summary>
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>
    /// When the domain was soft-deleted (server UTC).
    /// Null if not deleted.
    /// </summary>
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>
    /// Soft-delete flag following BaseEntity.IsDeleted pattern.
    /// Default: false.
    /// Global query filter: WHERE IsDeleted = false
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Optimistic concurrency token — incrementing integer row version.
    /// Backed by PostgreSQL xmin system column (xid type).
    /// CPD-11: Uses int, not byte[], for human-readable audit sequence.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Denormalized snapshot history of domain attributes.
    /// JSON array of version snapshots, stored as jsonb.
    /// Default: '[]'.
    /// Mirrors the StoreConfig VintagesJson pattern (StoreConfig.cs:109).
    /// </summary>
    public string DomainHistoryJson { get; set; } = "[]";

    // =========================================================================
    // Navigation Properties
    // =========================================================================

    /// <summary>
    /// Legacy in-memory active certificate pointer. EF ignores this navigation;
    /// active certificate authority is the SslCertificateId column plus the
    /// SslCertificates generation/history collection.
    /// </summary>
    public SslCertificate? SslCertificate { get; set; }

    /// <summary>
    /// Certificate generation history owned by this domain.
    /// </summary>
    public ICollection<SslCertificate> SslCertificates { get; set; } = new List<SslCertificate>();

    /// <summary>
    /// Durable SSL/TLS operation claims for this domain.
    /// </summary>
    public ICollection<SslCertificateOperation> SslCertificateOperations { get; set; } = new List<SslCertificateOperation>();

    /// <summary>
    /// Verification attempt history for this domain (one-to-many).
    /// </summary>
    public ICollection<DomainVerification> Verifications { get; set; } = new List<DomainVerification>();

    /// <summary>
    /// The redirect target domain (self-referencing many-to-one).
    /// The domain that requests should be redirected to.
    /// </summary>
    public Domain? RedirectTargetDomain { get; set; }

    /// <summary>
    /// Domains that redirect to this domain (reverse navigation).
    /// </summary>
    public ICollection<Domain> RedirectSources { get; set; } = new List<Domain>();

    /// <summary>
    /// Audit trail entries for this domain (one-to-many).
    /// </summary>
    public ICollection<DomainAuditEntry> AuditEntries { get; set; } = new List<DomainAuditEntry>();

    // =========================================================================
    // Lifecycle Methods (DOMAIN-1.5 Section 5: Lifecycle Method Contracts)
    // =========================================================================

    /// <summary>
    /// Marks the domain as verified — DNS/HTTP ownership check passed.
    /// Sets LifecycleState to Verified and records VerifiedAtUtc. Primary
    /// promotion is a separate edge-proven operation and cannot occur here.
    /// </summary>
    public void MarkPendingVerification(VerificationMethod method, string verificationToken)
    {
        if (LifecycleState != DomainLifecycleState.Draft)
        {
            throw new InvalidOperationException(
                $"Only Draft domains can enter PendingVerification. Current state: {LifecycleState}.");
        }

        if (string.IsNullOrWhiteSpace(verificationToken))
        {
            throw new ArgumentException("Verification token is required.", nameof(verificationToken));
        }

        _ = method switch
        {
            PointOfSale.Core.Enums.VerificationMethod.DnsTxt => true,
            PointOfSale.Core.Enums.VerificationMethod.DnsCname => true,
            PointOfSale.Core.Enums.VerificationMethod.HttpFile => true,
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported verification method."),
        };

        VerificationMethod = method;
        VerificationToken = verificationToken;
        VerificationTokenIssuedAtUtc = DateTime.UtcNow;
        LifecycleState = DomainLifecycleState.PendingVerification;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkVerified()
    {
        if (LifecycleState != DomainLifecycleState.PendingVerification)
        {
            throw new InvalidOperationException(
                $"Only PendingVerification domains can become Verified. Current state: {LifecycleState}.");
        }

        LifecycleState = DomainLifecycleState.Verified;
        VerifiedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the domain as SslPending — certificate provisioning initiated.
    /// </summary>
    public void MarkSslPending()
    {
        if (LifecycleState != DomainLifecycleState.Verified || VerifiedAtUtc is null)
        {
            throw new InvalidOperationException(
                $"Only verified domains can enter SslPending. Current state: {LifecycleState}.");
        }

        LifecycleState = DomainLifecycleState.SslPending;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the domain as SSL active — certificate provisioned and valid.
    /// Sets LifecycleState to SslActive and records SslActivatedAtUtc.
    /// </summary>
    public void MarkSslActive()
    {
        if (LifecycleState is not (DomainLifecycleState.Verified
            or DomainLifecycleState.SslPending
            or DomainLifecycleState.SslActive
            or DomainLifecycleState.Primary) || VerifiedAtUtc is null)
        {
            throw new InvalidOperationException(
                $"Only verified domains can become SslActive. Current state: {LifecycleState}.");
        }

        // Renewal/reconciliation must not demote a proven Primary domain.
        if (LifecycleState != DomainLifecycleState.Primary)
        {
            LifecycleState = DomainLifecycleState.SslActive;
        }
        SslActivatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the domain as disabled — no longer serving traffic.
    /// Sets LifecycleState to Disabled.
    /// </summary>
    public void MarkDisabled()
    {
        LifecycleState = DomainLifecycleState.Disabled;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the domain as archived — soft-retired, retained for history.
    /// </summary>
    public void MarkArchived()
    {
        LifecycleState = DomainLifecycleState.Archived;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Soft-deletes the domain — sets IsDeleted = true and DeletedAtUtc.
    /// The row is retained for audit/history (DOMAIN-1.5 CPD-5).
    /// </summary>
    public void MarkDeleted()
    {
        IsDeleted = true;
        DeletedAtUtc = DateTime.UtcNow;
        LifecycleState = DomainLifecycleState.Deleted;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
