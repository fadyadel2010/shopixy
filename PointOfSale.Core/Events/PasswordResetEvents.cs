namespace PointOfSale.Core.Events;

/// <summary>
/// Event raised when a password reset is requested.
/// Used to trigger email sending.
/// 
/// SECURITY: Contains sensitive ResetToken - must use PublishSensitiveAsync to bypass persistence.
/// Phase: 10.3.5 - Password Reset
/// </summary>
public class OnlineCustomerPasswordResetRequestedEvent : IntegrationEvent, ISensitiveIntegrationEvent
{
    public Guid CustomerId { get; init; }
    public string Email { get; init; } = null!;
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime RequestedAtUtc { get; init; }
    
    /// <summary>
    /// The raw reset token for email generation.
    /// SECURITY: Sensitive data - event bypasses outbox persistence via ISensitiveIntegrationEvent.
    /// </summary>
    public string ResetToken { get; init; } = null!;
}

/// <summary>
/// Event raised when password reset is completed successfully.
/// 
/// Phase: 10.3.5 - Password Reset
/// </summary>
public class OnlineCustomerPasswordResetCompletedEvent : IntegrationEvent
{
    public Guid CustomerId { get; init; }
    public DateTime CompletedAtUtc { get; init; }
}
