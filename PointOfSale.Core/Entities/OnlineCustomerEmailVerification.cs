using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Email verification token for Online Customers.
/// 
/// ARCHITECTURAL RULES:
/// - PostgreSQL is the ONLY source of truth for verification state
/// - Tokens are NEVER stored in plain text - only SHA256 hash
/// - Each token is one-time use
/// - Expiration: 24 hours default
/// - Previous unused tokens are invalidated on new request
/// 
/// SECURITY:
/// - Token hash stored, not raw token
/// - Raw token returned ONLY ONCE during generation
/// - Cross-tenant access blocked by TenantId validation
/// 
/// Phase: 10.3.2.5 - Storefront Email Verification Flow
/// </summary>
public class OnlineCustomerEmailVerification : ITenantOwned
{
    /// <summary>
    /// Unique identifier for this verification record.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The tenant this verification belongs to.
    /// MANDATORY - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The online customer this verification is for.
    /// </summary>
    public Guid OnlineCustomerId { get; set; }

    /// <summary>
    /// SHA256 hash of the verification token.
    /// The raw token is NEVER stored.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// When this verification token expires.
    /// Default: 24 hours from creation.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Whether this token has been used.
    /// Once used, it cannot be reused.
    /// </summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// When this token was used (if used).
    /// </summary>
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>
    /// When this verification record was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation property
    public OnlineCustomer? OnlineCustomer { get; set; }
}
