using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB Read Model for Online Customer Sessions (Admin View).
/// Maps to 'online_customer_sessions' collection.
/// 
/// ARCHITECTURAL RULES:
/// - Read-only projection for POS Admin dashboard
/// - PostgreSQL (OnlineCustomerRefreshToken) is the ONLY source of truth
/// - Projected from PostgreSQL via refresh token events
/// - Session = Refresh Token (one-to-one mapping)
/// - TenantId is MANDATORY - strict tenant isolation
/// - NO sensitive data (no token hash)
/// - Soft delete only - IsRevoked = true, never deleted
/// 
/// SECURITY NOTES:
/// - IP addresses are masked in display (last octet replaced)
/// - User agents are sanitized/truncated
/// - Admin can view but cannot modify sessions here (write via service)
/// - All access logged for audit
/// 
/// INDEXES:
/// - (TenantId, CustomerId) - list sessions per customer
/// - (TenantId, IsRevoked) - filter active/revoked
/// - (TenantId, CreatedAtUtc) - sort by creation
/// - (TenantId, IsSuspicious) - filter suspicious sessions
/// 
/// Phase: Admin Online Session Management (Production-Grade, Tenant-Safe)
/// </summary>
[BsonIgnoreExtraElements]
public class OnlineCustomerSessionDocument
{
    /// <summary>
    /// MongoDB document ID.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>
    /// The tenant this session belongs to.
    /// MANDATORY - enforced at all layers.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// Unique session identifier (maps to RefreshToken.Id).
    /// This IS the session - one refresh token = one session.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid SessionId { get; set; }

    /// <summary>
    /// The online customer who owns this session.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Customer email (masked for display: u***@example.com).
    /// Cached here to avoid joins in admin dashboard.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// IP address that created this session.
    /// Stored masked (e.g., 192.168.1.xxx) for privacy.
    /// </summary>
    public string? Ip { get; set; }

    /// <summary>
    /// User agent that created this session.
    /// Parsed/simplified for display (e.g., "Chrome / Windows").
    /// Truncated to 512 chars max.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// UTC timestamp when this session was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when this session expires.
    /// After this time, the session cannot be used even if not revoked.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp of last activity via token refresh.
    /// Updated when the session is used to refresh tokens.
    /// Null if never refreshed after initial creation.
    /// </summary>
    public DateTime? LastSeenAtUtc { get; set; }

    /// <summary>
    /// Whether this session has been revoked.
    /// Revoked sessions cannot be used.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// UTC timestamp when this session was revoked.
    /// Null if not revoked.
    /// </summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>
    /// Reason for revocation (e.g., "Admin revoked", "Token rotation", "Password change").
    /// Null if not revoked.
    /// </summary>
    public string? RevokedReason { get; set; }

    /// <summary>
    /// Whether this session has been flagged as suspicious.
    /// Set to true on token reuse detection or other anomalies.
    /// </summary>
    public bool IsSuspicious { get; set; }

    /// <summary>
    /// Reason why this session was flagged as suspicious.
    /// Null if not suspicious.
    /// </summary>
    public string? SuspiciousReason { get; set; }

    /// <summary>
    /// UTC timestamp when this document was last updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Last session lifecycle/security event applied to this projection.
    /// Prevents older issue/revocation events from resurrecting or downgrading sessions.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? LastEventId { get; set; }

    /// <summary>
    /// UTC timestamp of the last session lifecycle/security event applied.
    /// </summary>
    public DateTime? LastEventOccurredAtUtc { get; set; }

    /// <summary>
    /// Version tie-breaker for session lifecycle/security events with the same timestamp.
    /// </summary>
    public int LastEventVersion { get; set; }

    /// <summary>
    /// Schema version for document migration.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;
}
