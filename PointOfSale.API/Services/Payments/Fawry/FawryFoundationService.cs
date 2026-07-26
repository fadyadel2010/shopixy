using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.Payments.Fawry;

public interface IFawryFoundationHealthContributor
{
    FawryHealthStatus GetStatus();
}

public sealed class FawryFoundationHealthContributor : IFawryFoundationHealthContributor
{
    private readonly IOptions<FawryOptions> _options;

    public FawryFoundationHealthContributor(IOptions<FawryOptions> options)
    {
        _options = options;
    }

    public FawryHealthStatus GetStatus()
    {
        var options = _options.Value;
        var credentialValidation = FawryCredentialValidator.Validate(options);
        var officialApiAvailability = new FawryOfficialApiAvailability(
            OfficialApiAvailable: true,
            TransportName: "System.Net.Http.HttpClient",
            BaseUri: options.OfficialApiBaseUri,
            UsesOfficialFawryHttpsApi: true,
            UsesUnofficialWrapper: false,
            OfficialDotNetSdkAvailable: false);
        var configuration = new FawryConfigurationStatus(
            FawryOptionsValidator.IsRuntimeEnabled(options),
            options.Environment,
            options.SandboxMode,
            options.FeatureFlags.SandboxEnabled,
            options.FeatureFlags.ProductionEnabled,
            credentialValidation.CredentialsConfigured,
            credentialValidation.CallbackConfigured,
            options.RetryCount,
            options.Timeout,
            credentialValidation,
            officialApiAvailability,
            options.Capabilities);

        var messages = new List<string>();
        messages.AddRange(credentialValidation.Errors);

        if (officialApiAvailability.UsesUnofficialWrapper)
        {
            messages.Add("Fawry foundation must not use unofficial wrappers.");
        }

        var healthy = officialApiAvailability.OfficialApiAvailable
            && officialApiAvailability.UsesOfficialFawryHttpsApi
            && !officialApiAvailability.UsesUnofficialWrapper
            && credentialValidation.IsValid
            && messages.Count == 0;

        return new FawryHealthStatus(healthy, configuration, messages);
    }
}
