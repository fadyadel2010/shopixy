using System.Text.Json.Serialization;
using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.Payments.Stripe;

public sealed record CredentialValidationResult(
    bool IsValid,
    StripeEnvironment Environment,
    bool SandboxMode,
    bool CredentialsConfigured,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record SDKAvailability(
    bool OfficialSdkAvailable,
    string PackageName,
    string? InstalledVersion,
    string CertifiedVersion,
    string CurrentStripeApiVersion,
    bool UsesOfficialStripeClient);

public sealed record StripeConfigurationStatus(
    bool Enabled,
    StripeEnvironment Environment,
    bool SandboxMode,
    bool SandboxEnabled,
    bool ProductionEnabled,
    bool CredentialsConfigured,
    bool ApiVersionConfigured,
    bool TelemetryEnabled,
    int RetryCount,
    TimeSpan Timeout,
    CredentialValidationResult CredentialValidation,
    SDKAvailability SDKAvailability);

public sealed record StripeHealthStatus(
    bool Healthy,
    StripeConfigurationStatus ConfigurationStatus,
    IReadOnlyList<string> Messages)
{
    [JsonIgnore]
    public bool RequiresOperatorAction => Messages.Count > 0 || !Healthy;
}
