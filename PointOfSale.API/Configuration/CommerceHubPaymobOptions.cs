using Microsoft.Extensions.Options;
using PointOfSale.API.Services.Payments.Paymob;

namespace PointOfSale.API.Configuration;

public record PaymobOptions
{
    public const string SectionName = "Storefront:Payments:Paymob";
    public static readonly Uri OfficialApiBaseUri = new("https://accept.paymob.com/api/");

    public bool Enabled { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string IntegrationId { get; init; } = string.Empty;
    public string IframeId { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
    public string HmacSecret { get; init; } = string.Empty;
    public Uri BaseUrl { get; init; } = OfficialApiBaseUri;
    public PaymobEnvironment Environment { get; init; } = PaymobEnvironment.Sandbox;
    public bool SandboxMode { get; init; } = true;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);
    public int RetryCount { get; init; } = 2;
    public PaymobFeatureFlags FeatureFlags { get; init; } = new();
    public PaymobCallbackConfiguration Callback { get; init; } = new();
    public PaymobCapabilityModel Capabilities { get; init; } = new();
}

public sealed record CommerceHubPaymobOptions : PaymobOptions;

public sealed record PaymobFeatureFlags
{
    public bool Enabled { get; init; }
    public bool SandboxEnabled { get; init; } = true;
    public bool ProductionEnabled { get; init; }
    public bool FoundationHealthEnabled { get; init; } = true;
    public bool CredentialValidationEnabled { get; init; } = true;
    public bool EnvironmentValidationEnabled { get; init; } = true;
    public bool PaymentExecutionEnabled { get; init; }
    public bool CallbackExecutionEnabled { get; init; }
    public bool WebhookExecutionEnabled { get; init; }
    public bool RefundExecutionEnabled { get; init; }
}

public sealed record PaymobCallbackConfiguration
{
    public bool Configured { get; init; }
    public Uri? CallbackUrl { get; init; }
    public Uri? WebhookUrl { get; init; }
}

public sealed record PaymobCapabilityModel
{
    public static PaymobCapabilityModel FoundationOnly { get; } = new();

    public bool SupportsOfficialHttpsApi { get; init; } = true;
    public bool SupportsHmacVerification { get; init; } = true;
    public bool SupportsTenantScopedCredentials { get; init; } = true;
    public bool SupportsSandboxEnvironment { get; init; } = true;
    public bool SupportsProductionEnvironment { get; init; } = true;
    public bool PaymentExecutionCertified { get; init; }
    public bool CallbackCertified { get; init; }
    public bool ProviderAdapterCertified { get; init; }
}

public enum PaymobEnvironment
{
    Sandbox = 0,
    Production = 1
}

public sealed class PaymobOptionsValidator : IValidateOptions<PaymobOptions>
{
    public ValidateOptionsResult Validate(string? name, PaymobOptions options)
    {
        var result = PaymobCredentialValidator.Validate(options);
        return result.IsValid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(result.Errors);
    }

    public static bool IsRuntimeEnabled(PaymobOptions options) =>
        options.Enabled || options.FeatureFlags.Enabled;

    public static bool AreCredentialsConfigured(PaymobOptions options) =>
        !string.IsNullOrWhiteSpace(options.ApiKey)
        && !string.IsNullOrWhiteSpace(options.IntegrationId)
        && !string.IsNullOrWhiteSpace(options.IframeId)
        && !string.IsNullOrWhiteSpace(ResolveWebhookSecret(options));

    public static string ResolveWebhookSecret(PaymobOptions options) =>
        !string.IsNullOrWhiteSpace(options.HmacSecret)
            ? options.HmacSecret
            : options.Secret;
}

public static class PaymobCredentialValidator
{
    private const int MinimumSecretLength = 12;

    public static PaymobCredentialValidationResult Validate(PaymobOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var enabled = PaymobOptionsValidator.IsRuntimeEnabled(options);

        if (!options.Enabled && options.FeatureFlags.Enabled)
        {
            errors.Add("Paymob provider must be enabled when Paymob foundation feature flag is enabled.");
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            errors.Add("Paymob timeout must be positive.");
        }

        if (options.BaseUrl is null || !options.BaseUrl.IsAbsoluteUri)
        {
            errors.Add("Paymob base URL must be an absolute URI.");
        }
        else if (!string.Equals(options.BaseUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(options.BaseUrl.Host, PaymobOptions.OfficialApiBaseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Paymob base URL must use the official https://accept.paymob.com host.");
        }

        if (options.RetryCount is < 0 or > 5)
        {
            errors.Add("Paymob retry count must be between 0 and 5.");
        }

        if (options.Environment == PaymobEnvironment.Sandbox && !options.SandboxMode)
        {
            errors.Add("Paymob sandbox environment must set SandboxMode to true.");
        }

        if (options.Environment == PaymobEnvironment.Production && options.SandboxMode)
        {
            errors.Add("Paymob production environment must set SandboxMode to false.");
        }

        if (enabled && options.Environment == PaymobEnvironment.Sandbox && !options.FeatureFlags.SandboxEnabled)
        {
            errors.Add("Paymob sandbox execution must be explicitly enabled by the sandbox feature flag.");
        }

        if (enabled && options.Environment == PaymobEnvironment.Production && !options.FeatureFlags.ProductionEnabled)
        {
            errors.Add("Paymob production execution must be explicitly enabled by the production feature flag.");
        }

        if (!string.IsNullOrWhiteSpace(options.IntegrationId) && !long.TryParse(options.IntegrationId, out _))
        {
            errors.Add("Paymob integration ID must be numeric.");
        }

        if (!string.IsNullOrWhiteSpace(options.IframeId) && !long.TryParse(options.IframeId, out _))
        {
            errors.Add("Paymob iframe ID must be numeric.");
        }

        if (options.Callback.Configured)
        {
            if (options.Callback.CallbackUrl is null || !options.Callback.CallbackUrl.IsAbsoluteUri)
            {
                errors.Add("Paymob callback URL must be an absolute server-owned URL when callback configuration is enabled.");
            }

            if (options.Callback.WebhookUrl is null || !options.Callback.WebhookUrl.IsAbsoluteUri)
            {
                errors.Add("Paymob webhook URL must be an absolute server-owned URL when callback configuration is enabled.");
            }
        }
        else if (enabled)
        {
            errors.Add("Paymob callback configuration must be explicitly configured before enabling Paymob.");
        }

        if (options.FeatureFlags.PaymentExecutionEnabled &&
            (!options.Capabilities.PaymentExecutionCertified ||
             !options.Capabilities.ProviderAdapterCertified))
        {
            errors.Add("Paymob payment execution requires certified payment execution and provider adapter capabilities.");
        }

        if ((options.FeatureFlags.CallbackExecutionEnabled ||
             options.FeatureFlags.WebhookExecutionEnabled) &&
            (!options.Capabilities.CallbackCertified ||
             !options.Capabilities.SupportsHmacVerification))
        {
            errors.Add("Paymob callback and webhook execution require certified callback and HMAC capabilities.");
        }

        if (options.FeatureFlags.RefundExecutionEnabled &&
            !options.Capabilities.ProviderAdapterCertified)
        {
            errors.Add("Paymob refund execution requires a certified provider adapter capability.");
        }

        return new PaymobCredentialValidationResult(
            errors.Count == 0,
            options.Environment,
            options.SandboxMode,
            PaymobOptionsValidator.AreCredentialsConfigured(options),
            options.Callback.Configured,
            errors,
            warnings);
    }

    private static void ValidateRequiredSecret(string value, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"Paymob {name} is required when Paymob is enabled.");
            return;
        }

        if (value.Trim().Length < MinimumSecretLength)
        {
            errors.Add($"Paymob {name} is too short to be a real credential.");
        }
    }

    private static void ValidateRequiredIdentifier(string value, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"Paymob {name} is required when Paymob is enabled.");
        }
    }
}
