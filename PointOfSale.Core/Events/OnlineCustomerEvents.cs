namespace PointOfSale.Core.Events;

#region Online Customer Events

/// <summary>
/// Event raised when a new online customer is registered.
/// 
/// ARCHITECTURAL RULES:
/// - Minimal payload - only essential data
/// - NO sensitive data (NO password hash)
/// - Idempotent-safe - consumers can safely replay
/// - TenantId is MANDATORY
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// </summary>
public class OnlineCustomerRegisteredEvent : IntegrationEvent
{
    /// <summary>
    /// Unique identifier of the registered customer.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Customer email address (normalized to lowercase).
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Authentication provider: local, google, facebook
    /// </summary>
    public string Provider { get; set; } = "local";

    /// <summary>
    /// Customer's display name (optional).
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Whether the account is active upon registration.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether the email is verified upon registration.
    /// Social logins typically have verified emails.
    /// </summary>
    public bool EmailVerified { get; set; } = false;

    /// <summary>
    /// UTC timestamp when the account was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Event raised when an online customer account is activated.
/// Enables login for Storefront access.
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// </summary>
public class OnlineCustomerActivatedEvent : IntegrationEvent
{
    /// <summary>
    /// Unique identifier of the activated customer.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Customer email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the activation occurred.
    /// </summary>
    public DateTime ActivatedAtUtc { get; set; }
}

/// <summary>
/// Event raised when an online customer account is deactivated.
/// Disabled accounts cannot log in and their tokens are invalidated.
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// </summary>
public class OnlineCustomerDeactivatedEvent : IntegrationEvent
{
    /// <summary>
    /// Unique identifier of the deactivated customer.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Customer email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the deactivation occurred.
    /// </summary>
    public DateTime DeactivatedAtUtc { get; set; }

    /// <summary>
    /// Optional reason for deactivation.
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Event raised when an online customer's email is verified.
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// </summary>
public class OnlineCustomerEmailVerifiedEvent : IntegrationEvent
{
    /// <summary>
    /// Unique identifier of the customer.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Customer email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the email was verified.
    /// </summary>
    public DateTime VerifiedAtUtc { get; set; }
}

/// <summary>
/// Event raised when an online customer logs in.
/// Used for updating LastLoginAtUtc and cache invalidation.
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// </summary>
public class OnlineCustomerLoggedInEvent : IntegrationEvent
{
    /// <summary>
    /// Unique identifier of the customer.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Customer email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of the login.
    /// </summary>
    public DateTime LoginAtUtc { get; set; }

    /// <summary>
    /// Authentication provider used for this login.
    /// </summary>
    public string Provider { get; set; } = "local";
}

/// <summary>
/// Event raised when an online customer's profile is updated.
/// Triggers MongoDB projection update and Redis cache invalidation.
/// 
/// Phase F2: Account Area
/// </summary>
public class OnlineCustomerUpdatedEvent : IntegrationEvent
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string Provider { get; set; } = "local";
    public bool EmailVerified { get; set; }
    public bool IsActive { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Event raised when an online customer changes their password.
/// All sessions are revoked for security.
/// 
/// Phase F2: Account Area
/// </summary>
public class OnlineCustomerPasswordChangedEvent : IntegrationEvent
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public DateTime ChangedAt { get; set; }
}

#endregion

