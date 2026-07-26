using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.ShippingAdministration;

public enum CarrierAccountEnvironment
{
    Sandbox = 1,
    Production = 2
}

public enum CarrierAccountState
{
    Draft = 1,
    CredentialsPending = 2,
    ConnectionTestPending = 3,
    ReadyForActivation = 4,
    Active = 5,
    Suspended = 6,
    Disabled = 7,
    Retired = 8
}

public enum CarrierOperationalState
{
    NotReady = 1,
    Ready = 2,
    Suspended = 3,
    Disabled = 4,
    Retired = 5
}

public enum CarrierAccountHealthState
{
    Healthy = 1,
    Warning = 2,
    Blocked = 3,
    Disabled = 4,
    Retired = 5
}

public enum CarrierCredentialReferenceState
{
    Staged = 1,
    Active = 2,
    Rotating = 3,
    Disabled = 4,
    Retired = 5
}

public enum CarrierPolicyScope
{
    Domestic = 1,
    International = 2
}

public enum CarrierPolicyMode
{
    Disabled = 1,
    Enabled = 2
}

public enum CarrierConnectionTestStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3
}

public enum CarrierAdministrationAuditAction
{
    Registration = 1,
    CredentialReferenceUpdated = 2,
    CredentialRotation = 3,
    EnvironmentChanged = 4,
    Activation = 5,
    Suspension = 6,
    Disable = 7,
    Retirement = 8,
    WarehouseAssignment = 9,
    PolicyChanged = 10,
    ConnectionTest = 11,
    ServiceLevelChanged = 12,
    HealthChanged = 13,
    StateTransitionRejected = 14,
    ActivationRejected = 15
}

public sealed class CarrierAccount : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierAccountEnvironment? Environment { get; set; }
    public Guid? ActiveCredentialReferenceId { get; set; }
    public CarrierAccountState State { get; set; } = CarrierAccountState.Draft;
    public CarrierOperationalState OperationalState { get; set; } = CarrierOperationalState.NotReady;
    public CarrierAccountHealthState HealthState { get; set; } = CarrierAccountHealthState.Blocked;
    public string? HealthReason { get; set; }
    public DateTime? LastHealthEvaluatedAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? SuspendedAtUtc { get; set; }
    public DateTime? DisabledAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
}

public sealed class CarrierCredentialReference : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CarrierAccountId { get; set; }
    public string SecretReference { get; set; } = string.Empty;
    public string? MaskedFingerprint { get; set; }
    public CarrierCredentialReferenceState State { get; set; } = CarrierCredentialReferenceState.Staged;
    public DateTime? ExpiresAtUtc { get; set; }
}

public sealed class CarrierWarehouseAssignment : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CarrierAccountId { get; set; }
    public Guid WarehouseId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool OriginReadinessPublished { get; set; }
    public string? ReadinessEvidenceReference { get; set; }
}

public sealed class CarrierAccountPolicy : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CarrierAccountId { get; set; }
    public CarrierPolicyScope Scope { get; set; }
    public CarrierPolicyMode Mode { get; set; }
    public bool CustomsReadinessPublished { get; set; }
    public string? ReadinessEvidenceReference { get; set; }
}

public sealed class CarrierServiceLevelEnablement : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CarrierAccountId { get; set; }
    public string ServiceLevelCode { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

public sealed class CarrierConnectionTestRecord : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CarrierAccountId { get; set; }
    public Guid? CredentialReferenceId { get; set; }
    public CarrierAccountEnvironment Environment { get; set; }
    public CarrierConnectionTestStatus Status { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public DateTime TestedAtUtc { get; set; }
}

public sealed class CarrierAdministrationAudit : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CarrierAccountId { get; set; }
    public CarrierAdministrationAuditAction Action { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public string? PreviousState { get; set; }
    public string? NextState { get; set; }
    public string? Reason { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
