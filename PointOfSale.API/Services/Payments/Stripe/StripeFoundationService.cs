using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using Stripe;

namespace PointOfSale.API.Services.Payments.Stripe;

public interface IStripeFoundationHealthContributor
{
    StripeHealthStatus GetStatus();
}

public sealed class StripeFoundationHealthContributor : IStripeFoundationHealthContributor
{
    private const string CertifiedStripeNetVersion = "52.1.0";
    private readonly IOptions<StripeOptions> _options;

    public StripeFoundationHealthContributor(IOptions<StripeOptions> options)
    {
        _options = options;
    }

    public StripeHealthStatus GetStatus()
    {
        var options = _options.Value;
        var credentialValidation = StripeCredentialValidator.Validate(options);
        var sdkAvailability = GetSdkAvailability();
        var configuration = new StripeConfigurationStatus(
            StripeOptionsValidator.IsRuntimeEnabled(options),
            options.Environment,
            options.SandboxMode,
            options.FeatureFlags.SandboxEnabled,
            options.FeatureFlags.ProductionEnabled,
            credentialValidation.CredentialsConfigured,
            !string.IsNullOrWhiteSpace(options.ApiVersion),
            options.TelemetryEnabled,
            options.RetryCount,
            options.Timeout,
            credentialValidation,
            sdkAvailability);

        var messages = new List<string>();
        messages.AddRange(credentialValidation.Errors);

        if (!sdkAvailability.OfficialSdkAvailable)
        {
            messages.Add("Official Stripe .NET SDK is not available.");
        }

        if (!string.Equals(options.ApiVersion, StripeOptions.CertifiedApiVersion, StringComparison.Ordinal))
        {
            messages.Add($"Stripe API version should be pinned to {StripeOptions.CertifiedApiVersion} for this foundation certification.");
        }

        var healthy = sdkAvailability.OfficialSdkAvailable && credentialValidation.IsValid && messages.Count == 0;
        return new StripeHealthStatus(healthy, configuration, messages);
    }

    private static SDKAvailability GetSdkAvailability()
    {
        var assembly = typeof(StripeClient).Assembly.GetName();
        return new SDKAvailability(
            OfficialSdkAvailable: string.Equals(assembly.Name, "Stripe.net", StringComparison.Ordinal),
            PackageName: "Stripe.net",
            InstalledVersion: assembly.Version?.ToString(),
            CertifiedVersion: CertifiedStripeNetVersion,
            CurrentStripeApiVersion: StripeOptions.CertifiedApiVersion,
            UsesOfficialStripeClient: typeof(StripeClient).FullName == "Stripe.StripeClient");
    }
}
