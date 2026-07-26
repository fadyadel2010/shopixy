using System.Text.Json.Serialization;
using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.Payments.Fawry;

public sealed record FawryCredentialValidationResult(
    bool IsValid,
    FawryEnvironment Environment,
    bool SandboxMode,
    bool CredentialsConfigured,
    bool CallbackConfigured,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record FawryOfficialApiAvailability(
    bool OfficialApiAvailable,
    string TransportName,
    Uri BaseUri,
    bool UsesOfficialFawryHttpsApi,
    bool UsesUnofficialWrapper,
    bool OfficialDotNetSdkAvailable);

public sealed record FawryConfigurationStatus(
    bool Enabled,
    FawryEnvironment Environment,
    bool SandboxMode,
    bool SandboxEnabled,
    bool ProductionEnabled,
    bool CredentialsConfigured,
    bool CallbackConfigured,
    int RetryCount,
    TimeSpan Timeout,
    FawryCredentialValidationResult CredentialValidation,
    FawryOfficialApiAvailability OfficialApiAvailability,
    FawryCapabilityModel Capabilities);

public sealed record FawryHealthStatus(
    bool Healthy,
    FawryConfigurationStatus ConfigurationStatus,
    IReadOnlyList<string> Messages)
{
    [JsonIgnore]
    public bool RequiresOperatorAction => Messages.Count > 0 || !Healthy;
}
