using System.Text.Json.Serialization;
using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.Payments.ManualPayment;

public sealed record ManualPaymentConfigurationValidationResult(
    bool IsValid,
    bool Enabled,
    ManualPaymentEnvironment Environment,
    bool SandboxMode,
    bool InstructionsConfigured,
    bool ExpirationConfigured,
    int EnabledChannelCount,
    int AcceptedProofTypeCount,
    long MaximumUploadBytes,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record ManualPaymentProviderRegistrationStatus(
    string ProviderCode,
    string ProviderModel,
    bool ExternalNetworkRequired,
    bool UsesOfficialExternalSdk,
    bool RuntimeExecutionEnabled);

public sealed record ManualPaymentConfigurationStatus(
    bool Enabled,
    ManualPaymentEnvironment Environment,
    bool SandboxMode,
    bool SandboxEnabled,
    bool ProductionEnabled,
    bool InstructionsConfigured,
    bool ExpirationConfigured,
    int EnabledChannelCount,
    int AcceptedProofTypeCount,
    long MaximumUploadBytes,
    ManualPaymentConfigurationValidationResult Validation,
    ManualPaymentCapabilityModel Capabilities,
    ManualPaymentProviderRegistrationStatus ProviderRegistration);

public sealed record ManualPaymentHealthStatus(
    bool Healthy,
    ManualPaymentConfigurationStatus ConfigurationStatus,
    IReadOnlyList<string> Messages)
{
    [JsonIgnore]
    public bool RequiresOperatorAction => Messages.Count > 0 || !Healthy;
}
