namespace PointOfSale.Core.Entities;

/// <summary>
/// Email verification token for secure email confirmation.
/// Phase 8.4: Email Verification & Account Trust
/// 
/// Security features:
/// - Token is stored as SHA256 hash (never plain text)
/// - Single-use (UsedAtUtc marks usage)
/// - Time-limited (ExpiresAtUtc)
/// - Multiple tokens per user allowed (for resends)
/// </summary>
public class EmailVerificationToken : BaseEntity
{
    /// <summary>
    /// User this token belongs to.
    /// </summary>
    public Guid UserId { get; set; }
    
    /// <summary>
    /// SHA256 hash of the verification token.
    /// Never store plain token in database.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;
    
    /// <summary>
    /// When this token expires.
    /// Default: 24 hours from creation.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }
    
    /// <summary>
    /// When this token was used for verification.
    /// Null if not yet used.
    /// </summary>
    public DateTime? UsedAtUtc { get; set; }
    
    /// <summary>
    /// When this token was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public User? User { get; set; }
    
    // =========================================================================
    // Helper methods
    // =========================================================================
    
    /// <summary>
    /// Check if token is expired.
    /// </summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAtUtc;
    
    /// <summary>
    /// Check if token has been used.
    /// </summary>
    public bool IsUsed => UsedAtUtc.HasValue;
    
    /// <summary>
    /// Check if token is valid (not expired and not used).
    /// </summary>
    public bool IsValid => !IsExpired && !IsUsed;
}
