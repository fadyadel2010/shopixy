using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// User entity with role-based access.
/// Phase 8.0: Extended with TenantId for multi-tenant support.
/// </summary>
public class User : BaseEntity, ITenantOwned
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}";
    
    public string? Phone { get; set; }
    
    public UserRole Role { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Last login timestamp
    /// </summary>
    public DateTime? LastLoginAt { get; set; }
    
    /// <summary>
    /// Hash-only binding for the current POS refresh-backed session.
    /// Raw refresh credential material must not be persisted here.
    /// </summary>
    public string? RefreshToken { get; set; }
    
    /// <summary>
    /// Refresh token expiry
    /// </summary>
    public DateTime? RefreshTokenExpiryTime { get; set; }
    
    // =========================================================================
    // Phase 8.0: Multi-tenant support
    // =========================================================================
    
    /// <summary>
    /// Tenant that owns this user.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    // =========================================================================
    // Phase 8.4: Email Verification & Account Trust
    // =========================================================================
    
    /// <summary>
    /// Email verification status. Determines user trust level.
    /// Unverified users cannot publish storefront or invite users.
    /// Default: Verified (for existing users migration compatibility)
    /// </summary>
    public EmailVerificationStatus EmailVerificationStatus { get; set; } = EmailVerificationStatus.Verified;
    
    /// <summary>
    /// Timestamp when email was verified.
    /// Null if never verified.
    /// </summary>
    public DateTime? EmailVerifiedAtUtc { get; set; }
}

