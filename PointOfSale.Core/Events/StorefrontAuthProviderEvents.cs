namespace PointOfSale.Core.Events;

/// <summary>
/// Event raised when a storefront auth provider is enabled.
/// 
/// SECURITY: This is a security-critical event and should be logged/audited.
/// 
/// Phase: P2 - Storefront Auth Provider Management
/// </summary>
public class StorefrontAuthProviderEnabledEvent : IntegrationEvent
{
    /// <summary>
    /// The authentication provider that was enabled.
    /// </summary>
    public string Provider { get; init; } = null!;

    /// <summary>
    /// When the provider was enabled.
    /// </summary>
    public DateTime EnabledAtUtc { get; init; }

    /// <summary>
    /// User ID who enabled the provider.
    /// </summary>
    public Guid EnabledByUserId { get; init; }
}

/// <summary>
/// Event raised when a storefront auth provider is disabled.
/// 
/// SECURITY: This is a CRITICAL security event.
/// - All active login attempts using this provider should be rejected
/// - Should be logged at WARNING level for audit
/// 
/// Phase: P2 - Storefront Auth Provider Management
/// </summary>
public class StorefrontAuthProviderDisabledEvent : IntegrationEvent
{
    /// <summary>
    /// The authentication provider that was disabled.
    /// </summary>
    public string Provider { get; init; } = null!;

    /// <summary>
    /// When the provider was disabled.
    /// </summary>
    public DateTime DisabledAtUtc { get; init; }

    /// <summary>
    /// User ID who disabled the provider.
    /// </summary>
    public Guid DisabledByUserId { get; init; }

    /// <summary>
    /// Optional reason for disabling.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Event raised when a storefront auth provider's credentials are updated.
/// 
/// SECURITY: This event should NOT contain the actual credentials.
/// Only metadata about the change should be included.
/// 
/// Phase: P2 - Storefront Auth Provider Management
/// </summary>
public class StorefrontAuthProviderCredentialsUpdatedEvent : IntegrationEvent
{
    /// <summary>
    /// The authentication provider whose credentials were updated.
    /// </summary>
    public string Provider { get; init; } = null!;

    /// <summary>
    /// When the credentials were updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; init; }

    /// <summary>
    /// User ID who updated the credentials.
    /// </summary>
    public Guid UpdatedByUserId { get; init; }

    /// <summary>
    /// Whether the client ID was changed.
    /// </summary>
    public bool ClientIdChanged { get; init; }

    /// <summary>
    /// Whether the client secret was rotated.
    /// </summary>
    public bool ClientSecretRotated { get; init; }
}

/// <summary>
/// Event raised when a storefront auth provider's secret is rotated.
/// 
/// SECURITY: This is a security-critical event.
/// - Old secret becomes invalid immediately
/// - Should be logged for audit purposes
/// 
/// Phase: P2 - Storefront Auth Provider Management
/// </summary>
public class StorefrontAuthProviderSecretRotatedEvent : IntegrationEvent
{
    /// <summary>
    /// The authentication provider whose secret was rotated.
    /// </summary>
    public string Provider { get; init; } = null!;

    /// <summary>
    /// When the secret was rotated.
    /// </summary>
    public DateTime RotatedAtUtc { get; init; }

    /// <summary>
    /// User ID who rotated the secret.
    /// </summary>
    public Guid RotatedByUserId { get; init; }
}
