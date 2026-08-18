using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.Payments.Paymob;

public interface IPaymobFoundationHealthContributor
{
    PaymobHealthStatus GetStatus();
}

public sealed class PaymobFoundationHealthContributor : IPaymobFoundationHealthContributor
{
    private readonly IOptions<PaymobOptions> _options;

    public PaymobFoundationHealthContributor(IOptions<PaymobOptions> options)
    {
        _options = options;
    }

    public PaymobHealthStatus GetStatus()
    {
        var options = _options.Value;
        var credentialValidation = PaymobCredentialValidator.Validate(options);
        var usesOfficialApi = options.BaseUrl is { IsAbsoluteUri: true } &&
            string.Equals(options.BaseUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(options.BaseUrl.Host, PaymobOptions.OfficialApiBaseUri.Host, StringComparison.OrdinalIgnoreCase);
        var officialApiAvailability = new PaymobOfficialApiAvailability(
            OfficialApiAvailable: usesOfficialApi,
            TransportName: "System.Net.Http.HttpClient",
            BaseUri: options.BaseUrl,
            UsesOfficialPaymobHttpsApi: usesOfficialApi,
            UsesUnofficialWrapper: false);
        var configuration = new PaymobConfigurationStatus(
            PaymobOptionsValidator.IsRuntimeEnabled(options),
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

        var healthy = officialApiAvailability.OfficialApiAvailable
            && !officialApiAvailability.UsesUnofficialWrapper
            && credentialValidation.IsValid
            && messages.Count == 0;

        return new PaymobHealthStatus(healthy, configuration, messages);
    }
}
