using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Append-only audit trail entry for Domain lifecycle actions.
///
/// DOMAIN-1.5 Section 4.4 — Persistence Freeze Specification.
/// The entity shape MUST match DOMAIN-1.5 exactly. No redesign.
///
/// Grounded In:
///   - DOMAIN-1 Section 3.3 (DomainAuditEntry entity definition)
///   - DOMAIN-1.5 CPD-4: Implements ITenantOwned
///   - DOMAIN-1.5 CPD-13: Written in the SAME transaction as Domain state change
///   - Mirrors StoreConfigAuditEntry pattern (StoreConfigAuditEntry.cs:14-81)
///
/// Append-only enforcement: no UPDATE or DELETE permitted at DB level.
/// </summary>
public class DomainAuditEntry : ITenantOwned
{
    /// <summary>
    /// Primary key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key to the Domain that was changed.
    /// FK → Domains.Id. NOT NULL — retained even if Domain is deleted.
    /// </summary>
    public Guid DomainId { get; set; }

    /// <summary>
    /// Tenant ID of the Domain at the time of the audit entry.
    /// Denormalized for query performance — remains queryable even if
    /// the Tenant is later deleted.
    /// FK → Tenants.Id.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Action that triggered the audit entry.
    /// String matching the domain event name (e.g., DomainRegistered,
    /// DomainVerified, SslActivated, PrimaryChanged).
    /// Max 100 characters.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// The role of the actor who performed the action.
    /// Stored as smallint.
    /// </summary>
    public ActorType ActorType { get; set; }

    /// <summary>
    /// User ID of the acting user (if ActorType is Merchant or PlatformAdmin).
    /// FK → Users.Id. Null for system actions.
    /// </summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>
    /// JSON snapshot of the Domain entity BEFORE the state change.
    /// Null for create operations (no "before" state).
    /// Stored as jsonb.
    /// </summary>
    public string? BeforeSnapshotJson { get; set; }

    /// <summary>
    /// JSON snapshot of the Domain entity AFTER the state change.
    /// Always populated.
    /// Stored as jsonb. Unique per state transition.
    /// </summary>
    public string AfterSnapshotJson { get; set; } = string.Empty;

    /// <summary>
    /// Correlation ID for request tracing.
    /// Max 128 characters.
    /// </summary>
    public string? TraceId { get; set; }

    /// <summary>
    /// IP address of the requesting client or background job.
    /// Max 45 characters (supports IPv6).
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent string of the requesting client.
    /// Max 500 characters.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// When this audit entry was recorded (server UTC).
    /// </summary>
    public DateTime TimestampUtc { get; set; }

    // =========================================================================
    // Navigation Properties
    // =========================================================================

    /// <summary>
    /// The Domain that this audit entry relates to.
    /// </summary>
    public Domain Domain { get; set; } = null!;
}
