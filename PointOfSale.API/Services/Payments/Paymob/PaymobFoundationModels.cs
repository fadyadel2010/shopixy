using System.Text.Json.Serialization;
using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.Payments.Paymob;

public sealed record PaymobCredentialValidationResult(
    bool IsValid,
    PaymobEnvironment Environment,
    bool SandboxMode,
    bool CredentialsConfigured,
    bool CallbackConfigured,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record PaymobOfficialApiAvailability(
    bool OfficialApiAvailable,
    string TransportName,
    Uri BaseUri,
    bool UsesOfficialPaymobHttpsApi,
    bool UsesUnofficialWrapper);

public sealed record PaymobConfigurationStatus(
    bool Enabled,
    PaymobEnvironment Environment,
    bool SandboxMode,
    bool SandboxEnabled,
    bool ProductionEnabled,
    bool CredentialsConfigured,
    bool CallbackConfigured,
    int RetryCount,
    TimeSpan Timeout,
    PaymobCredentialValidationResult CredentialValidation,
    PaymobOfficialApiAvailability OfficialApiAvailability,
    PaymobCapabilityModel Capabilities);

public sealed record PaymobHealthStatus(
    bool Healthy,
    PaymobConfigurationStatus ConfigurationStatus,
    IReadOnlyList<string> Messages)
{
    [JsonIgnore]
    public bool RequiresOperatorAction => Messages.Count > 0 || !Healthy;
}
