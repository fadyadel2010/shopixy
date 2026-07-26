namespace PointOfSale.Core.Events;

/// <summary>
/// Event emitted when a customer requests email verification.
/// Used to trigger email sending and audit logging.
/// 
/// SECURITY:
/// - Does NOT contain the raw token
/// - Only metadata for processing
/// 
/// Phase: 10.3.2.5 - Storefront Email Verification Flow
/// </summary>
public class OnlineCustomerEmailVerificationRequestedEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent
{
    /// <summary>
    /// The customer requesting verification.
    /// </summary>
    public Guid CustomerId { get; init; }

    /// <summary>
    /// Customer's email address (for email sending).
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Customer's display name (for email personalization).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// When the verification token expires.
    /// </summary>
    public DateTime ExpiresAtUtc { get; init; }

    /// <summary>
    /// When this request was made.
    /// </summary>
    public DateTime RequestedAtUtc { get; init; }

    /// <summary>
    /// Whether this is a resend request.
    /// </summary>
    public bool IsResend { get; init; }
}

/// <summary>
/// Event emitted when a customer's email is successfully verified.
/// Used to update MongoDB projection and invalidate cache.
/// 
/// IMPORTANT:
/// - This event is emitted AFTER PostgreSQL is updated
/// - Consumer updates MongoDB and invalidates Redis cache
/// 
/// Phase: 10.3.2.5 - Storefront Email Verification Flow
/// </summary>
public class StorefrontCustomerEmailVerifiedEvent : IntegrationEvent
{
    /// <summary>
    /// The customer whose email was verified.
    /// </summary>
    public Guid CustomerId { get; init; }

    /// <summary>
    /// The verified email address.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// When the verification occurred.
    /// </summary>
    public DateTime VerifiedAtUtc { get; init; }
}
