using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Events.Domain;

/// <summary>
/// Internal domain event published when a domain is registered (created).
/// Published via IEventPublisher — the only supported event bus.
///
/// Architecture Law #19: Events via IEventPublisher Only.
///
/// TenantId and CorrelationId are inherited from IntegrationEvent base class.
/// ActorType and ActorUserId identify who performed the action.
/// </summary>
public sealed class DomainRegisteredEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public string HostName { get; init; } = string.Empty;
    public string NormalizedHostName { get; init; } = string.Empty;
    public DomainType DomainType { get; init; }
    public DomainLifecycleState LifecycleState { get; init; }
    public ActorType ActorType { get; init; }
    public Guid? ActorUserId { get; init; }
}

/// <summary>
/// Internal domain event published when a domain is updated.
/// </summary>
public sealed class DomainUpdatedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public string? PreviousHostName { get; init; }
    public string HostName { get; init; } = string.Empty;
    public string NormalizedHostName { get; init; } = string.Empty;
    public DomainType DomainType { get; init; }
    public DomainLifecycleState PreviousState { get; init; }
    public DomainLifecycleState LifecycleState { get; init; }
    public ActorType ActorType { get; init; }
    public Guid? ActorUserId { get; init; }
}

/// <summary>
/// Internal domain event published when a domain is archived.
/// </summary>
public sealed class DomainArchivedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public string HostName { get; init; } = string.Empty;
    public string NormalizedHostName { get; init; } = string.Empty;
    public DomainLifecycleState PreviousState { get; init; }
    public ActorType ActorType { get; init; }
    public Guid? ActorUserId { get; init; }
}

/// <summary>
/// Internal domain event published when a domain is restored from
/// Deleted, Archived, or Disabled state.
/// </summary>
public sealed class DomainRestoredEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public string HostName { get; init; } = string.Empty;
    public string NormalizedHostName { get; init; } = string.Empty;
    public DomainLifecycleState PreviousState { get; init; }
    public DomainLifecycleState LifecycleState { get; init; }
    public ActorType ActorType { get; init; }
    public Guid? ActorUserId { get; init; }
}

/// <summary>
/// Internal domain event published when a domain is soft-deleted.
/// </summary>
public sealed class DomainDeletedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public string HostName { get; init; } = string.Empty;
    public string NormalizedHostName { get; init; } = string.Empty;
    public DomainLifecycleState PreviousState { get; init; }
    public ActorType ActorType { get; init; }
    public Guid? ActorUserId { get; init; }
}

/// <summary>
/// Internal domain event published when a domain is promoted to Primary.
/// The previous primary (if any) is demoted simultaneously within the
/// same transaction boundary.
/// </summary>
public sealed class DomainPromotedToPrimaryEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public string HostName { get; init; } = string.Empty;
    public string NormalizedHostName { get; init; } = string.Empty;
    public Guid? PreviousPrimaryDomainId { get; init; }
    public string? PreviousPrimaryHostName { get; init; }
    public ActorType ActorType { get; init; }
    public Guid? ActorUserId { get; init; }
}

/// <summary>
/// Internal domain event published when a domain ownership verification succeeds.
/// </summary>
public sealed class DomainVerifiedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public string HostName { get; init; } = string.Empty;
    public string NormalizedHostName { get; init; } = string.Empty;
    public DateTime VerifiedAtUtc { get; init; }
    public VerificationMethod VerificationMethod { get; init; }
}

/// <summary>
/// Internal domain event published when a domain ownership verification attempt fails.
/// </summary>
public sealed class DomainVerificationFailedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public string HostName { get; init; } = string.Empty;
    public string NormalizedHostName { get; init; } = string.Empty;
    public VerificationMethod VerificationMethod { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public int Attempt { get; init; }
    public bool RetryEligible { get; init; }
}
