using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.Payments.ManualPayment;

public interface IManualPaymentOperationalReadinessHealthContributor
{
    ManualPaymentOperationalReadinessStatus GetStatus();
}

public sealed record ManualPaymentOperationalReadinessStatus(
    bool Healthy,
    IReadOnlyDictionary<string, bool> Checks,
    ManualPaymentConfigurationStatus ConfigurationStatus,
    IReadOnlyList<string> Messages);

public sealed record ManualPaymentOperationalAlertDefinition(
    string Name,
    string Severity,
    string Signal,
    string Runbook);

public sealed class ManualPaymentOperationalReadinessHealthContributor : IManualPaymentOperationalReadinessHealthContributor
{
    private readonly IManualPaymentFoundationHealthContributor _foundationHealth;

    public ManualPaymentOperationalReadinessHealthContributor(IManualPaymentFoundationHealthContributor foundationHealth)
    {
        _foundationHealth = foundationHealth;
    }

    public ManualPaymentOperationalReadinessStatus GetStatus()
    {
        var foundation = _foundationHealth.GetStatus();
        var configuration = foundation.ConfigurationStatus;

        var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["providerEnabledHealth"] = configuration.Enabled,
            ["configurationValidHealth"] = configuration.Validation.IsValid,
            ["supportedChannelsHealth"] = configuration.EnabledChannelCount > 0,
            ["instructionConfigurationHealth"] = configuration.InstructionsConfigured,
            ["expirationPolicyHealth"] = configuration.ExpirationConfigured,
            ["proofConfigurationHealth"] = configuration.AcceptedProofTypeCount > 0 && configuration.MaximumUploadBytes > 0,
            ["featureFlagHealth"] = FeatureFlagsConsistent(configuration),
            ["capabilityConsistencyHealth"] = CapabilitiesConsistent(configuration.Capabilities),
            ["secretExposureHealth"] = NoSecretsExposed(configuration)
        };

        return new ManualPaymentOperationalReadinessStatus(
            checks.Values.All(value => value),
            checks,
            configuration,
            foundation.Messages);
    }

    private static bool FeatureFlagsConsistent(ManualPaymentConfigurationStatus configuration) =>
        configuration.Validation.IsValid;

    private static bool CapabilitiesConsistent(ManualPaymentCapabilityModel capabilities) =>
        capabilities.SupportsTenantScopedConfiguration
        && capabilities.SupportsInstructionConfiguration
        && capabilities.SupportsReceiptProofUpload
        && capabilities.SupportsAcceptedProofTypes
        && capabilities.SupportsExpirationPolicy
        && capabilities.ProviderAdapterCertified
        && capabilities.RuntimeBridgeCertified
        && capabilities.EvidenceAdapterCertified;

    private static bool NoSecretsExposed(ManualPaymentConfigurationStatus configuration) =>
        !configuration.ProviderRegistration.ExternalNetworkRequired
        && !configuration.ProviderRegistration.UsesOfficialExternalSdk;
}

public static class ManualPaymentOperationalReadinessValidator
{
    public static IReadOnlyList<string> ValidateStartup(ManualPaymentOptions options) =>
        ManualPaymentConfigurationValidator.Validate(options).Errors;
}

public static class ManualPaymentOperationalAlerts
{
    public static readonly IReadOnlyList<ManualPaymentOperationalAlertDefinition> Definitions = new[]
    {
        new ManualPaymentOperationalAlertDefinition(
            "ManualPayment.ConfigurationInvalid",
            "Critical",
            "manual_payment_configuration_failures_total > 0 or manual_payment_operational_readiness unhealthy",
            "Validate Manual Payment options, tenant enablement, instructions, proof policy, channel list, and expiration policy."),
        new ManualPaymentOperationalAlertDefinition(
            "ManualPayment.ProviderDisabled",
            "High",
            "manual_payment_environment_failures_total with provider_disabled reason or providerEnabledHealth=false",
            "Confirm provider and feature flags are intentionally disabled; otherwise restore tenant/provider enablement."),
        new ManualPaymentOperationalAlertDefinition(
            "ManualPayment.RepeatedRuntimeRejection",
            "High",
            "manual_payment_runtime_rejections_total exceeds threshold for the same operation bucket",
            "Inspect Runtime outcome admission, canonical evidence lineage, and prior evidence ordering; fail closed until corrected."),
        new ManualPaymentOperationalAlertDefinition(
            "ManualPayment.EvidenceAdmissionFailures",
            "Medium",
            "manual_payment_evidence_total with rejected outcome exceeds threshold",
            "Review proof metadata, reference-number requirements, supported channels, and customer guidance."),
        new ManualPaymentOperationalAlertDefinition(
            "ManualPayment.HighConfigurationFailures",
            "Medium",
            "manual_payment_configuration_failures_total sustained above threshold",
            "Audit recent configuration changes and roll back invalid Manual Payment settings.")
    };
}
