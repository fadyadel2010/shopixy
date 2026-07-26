namespace PointOfSale.Core.Entities;

/// <summary>
/// Tracks revoked CommerceHub customer access tokens to prevent replay attacks.
/// Tokens are blacklisted on logout to invalidate them server-side.
/// </summary>
public class RevokedCustomerAccessToken
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// The JWT ID (jti claim) from the revoked token.
    /// </summary>
    public string Jti { get; set; } = null!;
    
    /// <summary>
    /// The customer ID associated with the revoked token.
    /// </summary>
    public Guid CustomerId { get; set; }
    
    /// <summary>
    /// When the token was revoked (logout time).
    /// </summary>
    public DateTime RevokedAt { get; set; }
    
    /// <summary>
    /// When the token expires naturally. Used for cleanup - revoked tokens
    /// can be deleted after this time since they would be invalid anyway.
    /// </summary>
    public DateTime ExpiresAt { get; set; }
    
    /// <summary>
    /// Optional reason for revocation (e.g., "logout", "security_incident").
    /// </summary>
    public string? Reason { get; set; }
}
