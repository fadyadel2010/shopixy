namespace PointOfSale.Core.Events;

/// <summary>
/// Event raised when a refresh token is issued.
/// 
/// Phase: 10.3.4 - Storefront Refresh Tokens & Token Revocation
/// </summary>
public class OnlineCustomerRefreshTokenIssuedEvent : IntegrationEvent
{
    public Guid CustomerId { get; init; }
    public Guid TokenId { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime IssuedAtUtc { get; init; }
    public string CreatedByIp { get; init; } = null!;
    
    /// <summary>
    /// User agent that created this session.
    /// For admin session management and security monitoring.
    /// Phase: Admin Online Session Management
    /// </summary>
    public string? CreatedByUserAgent { get; init; }
}

/// <summary>
/// Event raised when a refresh token is revoked.
/// 
/// Phase: 10.3.4 - Storefront Refresh Tokens & Token Revocation
/// </summary>
public class OnlineCustomerRefreshTokenRevokedEvent : IntegrationEvent
{
    public Guid CustomerId { get; init; }
    public Guid TokenId { get; init; }
    public string Reason { get; init; } = null!;
    public DateTime RevokedAtUtc { get; init; }
}

/// <summary>
/// Event raised when ALL refresh tokens for a customer are revoked.
/// Triggered by: password change, email change, account deactivation, token reuse detection.
/// 
/// Phase: 10.3.4 - Storefront Refresh Tokens & Token Revocation
/// </summary>
public class OnlineCustomerAllTokensRevokedEvent : IntegrationEvent
{
    public Guid CustomerId { get; init; }
    public string Reason { get; init; } = null!;
    public int TokensRevoked { get; init; }
    public DateTime RevokedAtUtc { get; init; }
}

/// <summary>
/// SECURITY EVENT: Raised when a revoked refresh token is reused.
/// This indicates a potential token theft attack.
/// 
/// RESPONSE:
/// - All tokens for the customer are revoked
/// - Customer is forced to re-authenticate
/// - Security team should be alerted
/// 
/// Phase: 10.3.4 - Storefront Refresh Tokens & Token Revocation
/// </summary>
public class OnlineCustomerTokenReuseDetectedEvent : IntegrationEvent
{
    public Guid CustomerId { get; init; }
    public Guid RevokedTokenId { get; init; }
    public string AttemptedFromIp { get; init; } = null!;
    public DateTime DetectedAtUtc { get; init; }
    public int TokensRevoked { get; init; }
}
