namespace PointOfSale.Core.Events;

/// <summary>
/// Event raised when a social login provider is attached to an existing customer.
/// 
/// Phase: 10.3.3.6 - Social Login Events
/// </summary>
public class OnlineCustomerSocialLoginAttachedEvent : IntegrationEvent
{
    public Guid CustomerId { get; init; }
    public string Provider { get; init; } = null!;
    public string ProviderUserId { get; init; } = null!;
    public string Email { get; init; } = null!;
    public bool EmailVerifiedByProvider { get; init; }
    public DateTime AttachedAtUtc { get; init; }
}

/// <summary>
/// Event raised when a customer authenticates via social login.
/// 
/// Phase: 10.3.3.6 - Social Login Events
/// </summary>
public class OnlineCustomerSocialLoginEvent : IntegrationEvent
{
    public Guid CustomerId { get; init; }
    public string Provider { get; init; } = null!;
    public string Email { get; init; } = null!;
    public DateTime LoginAtUtc { get; init; }
    public bool IsFirstLogin { get; init; }
}

/// <summary>
/// Event raised when social login provider settings are updated by POS admin.
/// 
/// Phase: 10.3.3.6 - Social Login Events
/// </summary>
public class StorefrontAuthProviderSettingsUpdatedEvent : IntegrationEvent
{
    public string Provider { get; init; } = null!;
    public bool IsEnabled { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public Guid UpdatedByUserId { get; init; }
}
