using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Refresh token for Storefront Online Customer authentication.
/// 
/// ARCHITECTURAL RULES:
/// - PostgreSQL is the ONLY source of truth
/// - Tokens are NEVER stored in plain text - only SHA256 hash
/// - Tokens are NEVER deleted - only revoked (soft revoke)
/// - Token rotation with chain tracking prevents replay attacks
/// - TenantId is MANDATORY for all queries
/// 
/// SECURITY:
/// - TokenHash: SHA256 hash of the raw token
/// - Raw token returned ONCE during issuance
/// - Reuse of revoked token triggers security alert
/// - All tokens for customer revoked on reuse detection
/// 
/// Phase: 10.3.4 - Storefront Refresh Tokens & Token Revocation
/// </summary>
public class OnlineCustomerRefreshToken : ITenantOwned
{
    /// <summary>
    /// Unique identifier for this refresh token.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The tenant this token belongs to.
    /// MANDATORY - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The online customer this token belongs to.
    /// </summary>
    public Guid OnlineCustomerId { get; set; }

    /// <summary>
    /// SHA256 hash of the refresh token.
    /// The raw token is NEVER stored.
    /// </summary>
    public string TokenHash { get; set; } = null!;

    /// <summary>
    /// When this token expires.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// The currently active access token JTI associated with this session.
    /// Used to revoke the last issued access token immediately when the session
    /// is revoked or the customer is deactivated.
    /// </summary>
    public string? CurrentAccessTokenJti { get; set; }

    /// <summary>
    /// Expiration timestamp for the currently active access token JTI.
    /// </summary>
    public DateTime? CurrentAccessTokenExpiresAtUtc { get; set; }

    /// <summary>
    /// Whether this token has been revoked.
    /// Revoked tokens cannot be used.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// Whether the current access token JTI has been successfully blacklisted in Redis.
    /// Used to track if Redis blacklisting needs to be retried on subsequent calls.
    /// </summary>
    public bool IsAccessTokenBlacklisted { get; set; }

    /// <summary>
    /// When this token was revoked (if revoked).
    /// </summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>
    /// Reason for revocation.
    /// </summary>
    public string? RevokedReason { get; set; }

    /// <summary>
    /// The new token that replaced this one during rotation.
    /// Used to detect token reuse attacks.
    /// </summary>
    public Guid? ReplacedByTokenId { get; set; }

    /// <summary>
    /// When this token was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// IP address that created this token.
    /// For security auditing.
    /// </summary>
    public string CreatedByIp { get; set; } = null!;

    /// <summary>
    /// User agent that created this token.
    /// For security auditing.
    /// </summary>
    public string? CreatedByUserAgent { get; set; }

    // Navigation property
    public OnlineCustomer? OnlineCustomer { get; set; }

    /// <summary>
    /// Checks if this token is valid (not expired, not revoked).
    /// </summary>
    public bool IsValid => !IsRevoked && ExpiresAtUtc > DateTime.UtcNow;

    /// <summary>
    /// Checks if this token is expired.
    /// </summary>
    public bool IsExpired => ExpiresAtUtc <= DateTime.UtcNow;

    /// <summary>
    /// Revokes this token.
    /// </summary>
    public void Revoke(string reason, Guid? replacedByTokenId = null)
    {
        if (IsRevoked) return;

        IsRevoked = true;
        RevokedAtUtc = DateTime.UtcNow;
        RevokedReason = reason;
        ReplacedByTokenId = replacedByTokenId;
    }
}
