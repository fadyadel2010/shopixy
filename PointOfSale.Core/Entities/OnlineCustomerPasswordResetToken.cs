using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Password reset token for Online Customer accounts.
/// 
/// SECURITY CRITICAL:
/// - Raw token is NEVER stored - only SHA256 hash
/// - Tokens are one-time use (IsUsed flag)
/// - Tokens expire after 30 minutes
/// - Previous unused tokens are invalidated on new request
/// - Token validation ALWAYS includes TenantId
/// 
/// PRIVACY RULES:
/// - Same response returned whether email exists or not
/// - No user enumeration allowed
/// - Logs may differ but responses must not
/// 
/// Phase: 10.3.5 - Password Reset
/// </summary>
public class OnlineCustomerPasswordResetToken : ITenantOwned
{
    /// <summary>
    /// Unique identifier for this reset token.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Tenant this token belongs to.
    /// CRITICAL: Token validation MUST include TenantId.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The customer this token was issued to.
    /// </summary>
    public Guid OnlineCustomerId { get; set; }

    /// <summary>
    /// SHA256 hash of the reset token.
    /// RAW TOKEN IS NEVER STORED.
    /// </summary>
    public string TokenHash { get; set; } = null!;

    /// <summary>
    /// When this token expires.
    /// Default: 30 minutes from creation.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Whether this token has been used.
    /// Once used, token cannot be reused.
    /// </summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// When the token was used.
    /// NULL if not yet used.
    /// </summary>
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>
    /// When this token was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// IP address that requested the reset.
    /// For security logging only.
    /// </summary>
    public string CreatedByIp { get; set; } = null!;

    // Navigation property
    public OnlineCustomer? OnlineCustomer { get; set; }
}
