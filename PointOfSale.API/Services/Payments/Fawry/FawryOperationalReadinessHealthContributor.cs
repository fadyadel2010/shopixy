using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.Payments.Fawry;

public interface IFawryOperationalReadinessHealthContributor
{
    FawryOperationalReadinessStatus GetStatus();
}

public sealed record FawryOperationalReadinessStatus(
    bool Healthy,
    IReadOnlyDictionary<string, bool> Checks,
    FawryConfigurationStatus ConfigurationStatus,
    IReadOnlyList<string> Messages);

public sealed class FawryOperationalReadinessHealthContributor : IFawryOperationalReadinessHealthContributor
{
    private readonly IFawryFoundationHealthContributor _foundationHealth;

    public FawryOperationalReadinessHealthContributor(IFawryFoundationHealthContributor foundationHealth)
    {
        _foundationHealth = foundationHealth;
    }

    public FawryOperationalReadinessStatus GetStatus()
    {
        var foundation = _foundationHealth.GetStatus();
        var configuration = foundation.ConfigurationStatus;
        var credentials = configuration.CredentialValidation;

        var checks = new Dictionary<string, bool>
        {
            ["providerEnabledHealth"] = configuration.Enabled,
            ["foundationHealth"] = foundation.Healthy,
            ["environmentHealth"] = EnvironmentConfigured(configuration),
            ["credentialHealth"] = credentials.IsValid && credentials.CredentialsConfigured,
            ["merchantCodeHealth"] = credentials.CredentialsConfigured,
            ["apiKeyHealth"] = credentials.CredentialsConfigured,
            ["secretHealth"] = credentials.CredentialsConfigured,
            ["callbackUrlHealth"] = credentials.CallbackConfigured,
            ["featureFlagHealth"] = FeatureFlagsConsistent(configuration),
            ["capabilityConsistencyHealth"] = CapabilitiesConsistent(configuration.Capabilities)
        };

        var healthy = checks.Values.All(value => value);
        return new FawryOperationalReadinessStatus(
            healthy,
            checks,
            configuration,
            foundation.Messages);
    }

    private static bool EnvironmentConfigured(FawryConfigurationStatus configuration) =>
        configuration.Environment switch
        {
            FawryEnvironment.Sandbox => configuration.SandboxMode && configuration.SandboxEnabled,
            FawryEnvironment.Production => !configuration.SandboxMode && configuration.ProductionEnabled,
            _ => false
        };

    private static bool FeatureFlagsConsistent(FawryConfigurationStatus configuration) =>
        EnvironmentConfigured(configuration)
        && configuration.CredentialValidation.IsValid;

    private static bool CapabilitiesConsistent(FawryCapabilityModel capabilities) =>
        capabilities.SupportsOfficialHttpsApi
        && capabilities.SupportsSha256Signature
        && capabilities.SupportsTenantScopedCredentials
        && capabilities.SupportsSandboxEnvironment
        && capabilities.SupportsProductionEnvironment
        && capabilities.SupportsConfiguredServerNotification
        && capabilities.SupportsTransactionInquiry;
}
