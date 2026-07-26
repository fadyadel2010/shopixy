using System.Text.Json.Serialization;
using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.Payments.Cod;

public sealed record CodCapabilityModel
{
    public static CodCapabilityModel FoundationOnly { get; } = new();

    public bool ReferenceWorkflowIdentityDefined { get; init; } = true;
    public bool PaymentMethodSelection { get; init; } = true;
    public bool Collection { get; init; } = true;
    public bool Settlement { get; init; } = true;
    public bool EvidenceAdmission { get; init; } = true;
    public bool Replay { get; init; } = true;
    public bool Recovery { get; init; } = true;
    public bool Retention { get; init; } = true;
    public bool OperationalReview { get; init; } = true;
    public bool Dispute { get; init; } = true;
    public bool Reconciliation { get; init; } = true;
    public bool AuthorityContractsDefined { get; init; } = true;
    public bool RuntimeIntegrationCertified { get; init; }
    public bool EvidenceProcessingCertified { get; init; }
    public bool LifecycleMutationCertified { get; init; }
    public bool SettlementExecutionCertified { get; init; }
    public bool WorkflowApplicationCertified { get; init; }
}

public sealed record CodReferenceWorkflowConfigurationValidationResult(
    bool IsValid,
    bool Enabled,
    CodReferenceWorkflowEnvironment Environment,
    bool CollectionPolicyConfigured,
    bool EligibilityPolicyConfigured,
    bool SettlementPolicyConfigured,
    bool EvidencePolicyConfigured,
    bool ReplayPolicyConfigured,
    bool RecoveryPolicyConfigured,
    bool RetentionPolicyConfigured,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record CodReferenceWorkflowRegistrationStatus(
    string WorkflowCode,
    string WorkflowModel,
    bool ExternalProvider,
    bool RuntimeExecutionEnabled,
    bool EvidenceAdmissionEnabled,
    bool LifecycleMutationEnabled,
    bool SettlementExecutionEnabled);

public sealed record CodReferenceWorkflowConfigurationStatus(
    bool Enabled,
    CodReferenceWorkflowEnvironment Environment,
    CodReferenceWorkflowConfigurationValidationResult Validation,
    CodCapabilityModel Capabilities,
    CodReferenceWorkflowRegistrationStatus Registration,
    IReadOnlyList<string> AuthorityContractNames);

public sealed record CodReferenceWorkflowHealthStatus(
    bool Healthy,
    bool DisabledByDefaultVerified,
    CodReferenceWorkflowConfigurationStatus ConfigurationStatus,
    IReadOnlyList<string> Messages)
{
    [JsonIgnore]
    public bool RequiresOperatorAction => Messages.Count > 0 || !Healthy;
}

public interface ICodAuthorityContractCatalog
{
    IReadOnlyList<Type> AuthorityContracts { get; }
}

public sealed class CodAuthorityContractCatalog : ICodAuthorityContractCatalog
{
    public IReadOnlyList<Type> AuthorityContracts { get; } = new[]
    {
        typeof(ICodEligibilityAuthority),
        typeof(ICodCollectionAuthority),
        typeof(ICodSettlementAuthority),
        typeof(ICodEvidenceAuthority),
        typeof(ICodReplayAuthority),
        typeof(ICodRecoveryAuthority),
        typeof(ICodRetentionAuthority),
        typeof(ICodOperationalAuthority)
    };
}
