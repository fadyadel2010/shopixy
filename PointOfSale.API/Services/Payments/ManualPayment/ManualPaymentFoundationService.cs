using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.Payments.ManualPayment;

public interface IManualPaymentFoundationHealthContributor
{
    ManualPaymentHealthStatus GetStatus();
}

public sealed class ManualPaymentFoundationHealthContributor : IManualPaymentFoundationHealthContributor
{
    public const string ProviderCode = "manual_payment";

    private readonly IOptions<ManualPaymentOptions> _options;

    public ManualPaymentFoundationHealthContributor(IOptions<ManualPaymentOptions> options)
    {
        _options = options;
    }

    public ManualPaymentHealthStatus GetStatus()
    {
        var options = _options.Value;
        var validation = ManualPaymentConfigurationValidator.Validate(options);
        var providerRegistration = new ManualPaymentProviderRegistrationStatus(
            ProviderCode,
            "Backend-owned workflow provider",
            ExternalNetworkRequired: false,
            UsesOfficialExternalSdk: false,
            RuntimeExecutionEnabled: false);
        var configuration = new ManualPaymentConfigurationStatus(
            validation.Enabled,
            options.Environment,
            options.SandboxMode,
            options.FeatureFlags.SandboxEnabled,
            options.FeatureFlags.ProductionEnabled,
            validation.InstructionsConfigured,
            validation.ExpirationConfigured,
            validation.EnabledChannelCount,
            validation.AcceptedProofTypeCount,
            validation.MaximumUploadBytes,
            validation,
            options.Capabilities,
            providerRegistration);

        return new ManualPaymentHealthStatus(validation.IsValid, configuration, validation.Errors);
    }
}
