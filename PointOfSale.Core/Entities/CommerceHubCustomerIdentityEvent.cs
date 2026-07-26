namespace PointOfSale.Core.Entities;

/// <summary>
/// Append-only SQL identity lifecycle evidence for CommerceHub customers.
/// Every security-critical identity mutation is recorded here for incident reconstruction.
///
/// SECURITY CRITICAL:
/// - This table is INSERT-ONLY. No updates or deletes.
/// - Every identity-sensitive action must produce an entry.
/// </summary>
public class CommerceHubCustomerIdentityEvent
{
    public Guid Id { get; set; }

    /// <summary>
    /// The customer this event relates to.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// The type of identity event that occurred.
    /// </summary>
    public string EventType { get; set; } = null!;

    /// <summary>
    /// Human-readable description for forensic review.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional metadata (e.g. old email, governance code, token jti).
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// IP address of the originating request, if available.
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// When this event was recorded.
    /// </summary>
    public DateTime OccurredAtUtc { get; set; }

    // Well-known event types
    public static class EventTypes
    {
        public const string Registered = "customer_registered";
        public const string LoggedIn = "customer_logged_in";
        public const string LoggedOut = "customer_logged_out";
        public const string PasswordResetRequested = "password_reset_requested";
        public const string PasswordResetCompleted = "password_reset_completed";
        public const string PasswordChanged = "password_changed";
        public const string EmailVerificationRequested = "email_verification_requested";
        public const string EmailVerified = "email_verified";
        public const string EmailChanged = "email_changed";
        public const string ProfileUpdated = "profile_updated";
        public const string AllSessionsRevoked = "all_sessions_revoked";
        public const string LoginBlocked = "login_blocked";
        public const string RegistrationBlocked = "registration_blocked";
    }
}
