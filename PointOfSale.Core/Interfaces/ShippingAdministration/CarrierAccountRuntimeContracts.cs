using PointOfSale.Core.Entities.ShippingAdministration;

namespace PointOfSale.Core.Interfaces.ShippingAdministration;

public sealed record CarrierAdministrationActor(string ActorId);

public sealed record CarrierAccountRuntimeResult(
    bool Succeeded,
    Guid? CarrierAccountId,
    CarrierAccountState? State,
    CarrierAccountHealthState? HealthState,
    string? FailureCode,
    IReadOnlyList<string> Blockers)
{
    public static CarrierAccountRuntimeResult Success(
        Guid carrierAccountId,
        CarrierAccountState state,
        CarrierAccountHealthState healthState,
        IReadOnlyList<string>? blockers = null) =>
        new(true, carrierAccountId, state, healthState, null, blockers ?? Array.Empty<string>());

    public static CarrierAccountRuntimeResult Failure(
        string failureCode,
        IReadOnlyList<string>? blockers = null,
        Guid? carrierAccountId = null,
        CarrierAccountState? state = null,
        CarrierAccountHealthState? healthState = null) =>
        new(false, carrierAccountId, state, healthState, failureCode, blockers ?? new[] { failureCode });
}

public sealed record CarrierCredentialReferenceCommand(
    Guid CarrierAccountId,
    string SecretReference,
    string? MaskedFingerprint,
    DateTime? ExpiresAtUtc);

public sealed record CarrierWarehouseAssignmentCommand(
    Guid CarrierAccountId,
    Guid WarehouseId,
    bool OriginReadinessPublished,
    string? ReadinessEvidenceReference);

public sealed record CarrierPolicyCommand(
    Guid CarrierAccountId,
    CarrierPolicyScope Scope,
    CarrierPolicyMode Mode,
    bool CustomsReadinessPublished,
    string? ReadinessEvidenceReference);

public sealed record CarrierReadinessEvidence(
    bool? CarrierMappingPublished,
    bool? ProviderCapabilitySupported);

public sealed record CarrierConnectionTestCredentials(
    string ApiKey,
    string? ApiSecret,
    string? AccountId,
    string? ApiUrl,
    bool UseSandbox,
    IReadOnlyDictionary<string, string>? ExtraConfig = null);

public interface ICarrierAccountAdministrationRuntime
{
    Task<CarrierAccountRuntimeResult> RegisterCarrierAccountAsync(
        Guid tenantId,
        string carrierCode,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CarrierAccountRuntimeResult> UpdateCredentialReferenceAsync(
        Guid tenantId,
        CarrierCredentialReferenceCommand command,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CarrierAccountRuntimeResult> SelectEnvironmentAsync(
        Guid tenantId,
        Guid carrierAccountId,
        CarrierAccountEnvironment environment,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CarrierAccountRuntimeResult> AssignWarehouseAsync(
        Guid tenantId,
        CarrierWarehouseAssignmentCommand command,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CarrierAccountRuntimeResult> SetPolicyAsync(
        Guid tenantId,
        CarrierPolicyCommand command,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CarrierAccountRuntimeResult> SetServiceLevelAsync(
        Guid tenantId,
        Guid carrierAccountId,
        string serviceLevelCode,
        bool enabled,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CarrierAccountRuntimeResult> RunConnectionTestAsync(
        Guid tenantId,
        Guid carrierAccountId,
        CarrierConnectionTestCredentials credentials,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CarrierAccountRuntimeResult> TransitionStateAsync(
        Guid tenantId,
        Guid carrierAccountId,
        CarrierAccountState nextState,
        CarrierReadinessEvidence readiness,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CarrierAccountRuntimeResult> EvaluateHealthAsync(
        Guid tenantId,
        Guid carrierAccountId,
        CarrierReadinessEvidence readiness,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default);
}
