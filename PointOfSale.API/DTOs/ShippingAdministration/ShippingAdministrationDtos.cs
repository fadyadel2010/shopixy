using PointOfSale.Core.Entities.ShippingAdministration;

namespace PointOfSale.API.DTOs.ShippingAdministration;

public sealed record ShippingAdministrationResultDto(
    bool Succeeded,
    Guid? CarrierAccountId,
    string? State,
    string? HealthState,
    string? FailureCode,
    IReadOnlyList<string> Blockers);

public sealed record SupportedShippingCarrierDto(
    string CarrierCode,
    string DisplayName,
    IReadOnlyList<ShippingAdministrationServiceLevelDto> ServiceLevels);

public sealed record ShippingAdministrationServiceLevelDto(
    string Code,
    string Name,
    string? Description,
    int TypicalDaysMin,
    int TypicalDaysMax,
    bool? IsEnabled = null);

public sealed record CarrierAccountAdministrationDto(
    Guid Id,
    string CarrierCode,
    string? Environment,
    Guid? ActiveCredentialReferenceId,
    string State,
    string OperationalState,
    string HealthState,
    string? HealthReason,
    DateTime? LastHealthEvaluatedAtUtc,
    DateTime? ActivatedAtUtc,
    DateTime? SuspendedAtUtc,
    DateTime? DisabledAtUtc,
    DateTime? RetiredAtUtc,
    IReadOnlyList<CarrierCredentialReferenceDto> CredentialReferences,
    IReadOnlyList<CarrierWarehouseAssignmentDto> WarehouseAssignments,
    IReadOnlyList<CarrierPolicyDto> Policies,
    IReadOnlyList<ShippingAdministrationServiceLevelDto> ServiceLevels,
    CarrierConnectionTestDto? LastConnectionTest);

public sealed record CarrierCredentialReferenceDto(
    Guid Id,
    string? MaskedFingerprint,
    string State,
    DateTime? ExpiresAtUtc);

public sealed record CarrierWarehouseAssignmentDto(
    Guid WarehouseId,
    bool IsEnabled,
    bool OriginReadinessPublished,
    string? ReadinessEvidenceReference);

public sealed record CarrierPolicyDto(
    string Scope,
    string Mode,
    bool CustomsReadinessPublished,
    string? ReadinessEvidenceReference);

public sealed record CarrierConnectionTestDto(
    Guid Id,
    string Environment,
    string Status,
    string? FailureCode,
    string? FailureReason,
    DateTime TestedAtUtc);

public sealed record CarrierAdministrationAuditDto(
    Guid Id,
    Guid CarrierAccountId,
    string Action,
    string ActorId,
    string? CorrelationId,
    string? PreviousState,
    string? NextState,
    string? Reason,
    DateTime OccurredAtUtc);

public sealed record CreateCarrierAccountCommand(string CarrierCode);

public sealed record UpdateCarrierAccountCommand(string? Environment);

public sealed record CredentialReferenceCommand(
    string SecretReference,
    string? MaskedFingerprint,
    DateTime? ExpiresAtUtc);

public sealed record EnvironmentCommand(string Environment);

public sealed record WarehouseAssignmentCommand(
    Guid WarehouseId,
    bool OriginReadinessPublished,
    string? ReadinessEvidenceReference);

public sealed record PolicyCommand(
    string Mode,
    bool CustomsReadinessPublished,
    string? ReadinessEvidenceReference);

public sealed record ServiceLevelCommand(bool Enabled);

public sealed record ConnectionTestCommand(
    string ApiKey,
    string? ApiSecret,
    string? AccountId,
    string? ApiUrl,
    bool UseSandbox,
    IReadOnlyDictionary<string, string>? ExtraConfig);

public sealed record ReadinessEvidenceCommand(
    bool? CarrierMappingPublished,
    bool? ProviderCapabilitySupported);

public static class ShippingAdministrationDtoMapper
{
    public static ShippingAdministrationResultDto ToDto(this PointOfSale.Core.Interfaces.ShippingAdministration.CarrierAccountRuntimeResult result) =>
        new(
            result.Succeeded,
            result.CarrierAccountId,
            result.State?.ToString(),
            result.HealthState?.ToString(),
            result.FailureCode,
            result.Blockers);
}
