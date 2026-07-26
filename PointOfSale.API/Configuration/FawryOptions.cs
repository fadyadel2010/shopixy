using Microsoft.Extensions.Options;
using PointOfSale.API.Services.Payments.Fawry;

namespace PointOfSale.API.Configuration;

public sealed record FawryOptions
{
    public const string SectionName = "CommerceHub:Payments:Fawry";
    public static readonly Uri SandboxApiBaseUri = new("https://atfawry.fawrystaging.com/");
    public static readonly Uri ProductionApiBaseUri = new("https://www.atfawry.com/");

    public bool Enabled { get; init; }
    public string MerchantCode { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
    public FawryEnvironment Environment { get; init; } = FawryEnvironment.Sandbox;
    public bool SandboxMode { get; init; } = true;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);
    public int RetryCount { get; init; } = 2;
    public FawryFeatureFlags FeatureFlags { get; init; } = new();
    public FawryCallbackConfiguration Callback { get; init; } = new();
    public FawryCapabilityModel Capabilities { get; init; } = FawryCapabilityModel.FoundationOnly;

    public Uri OfficialApiBaseUri => Environment == FawryEnvironment.Production
        ? ProductionApiBaseUri
        : SandboxApiBaseUri;
}

public sealed record FawryFeatureFlags
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
    public bool PartialRefundExecutionEnabled { get; init; }
    public bool VoidExecutionEnabled { get; init; }
    public bool CaptureExecutionEnabled { get; init; }
    public bool InquiryExecutionEnabled { get; init; }
}

public sealed record FawryCallbackConfiguration
{
    public bool Configured { get; init; }
    public Uri? CallbackUrl { get; init; }
    public Uri? WebhookUrl { get; init; }
}

public sealed record FawryCapabilityModel
{
    public static FawryCapabilityModel FoundationOnly { get; } = new();

    public bool SupportsOfficialHttpsApi { get; init; } = true;
    public bool SupportsOfficialDotNetSdk { get; init; }
    public bool SupportsSha256Signature { get; init; } = true;
    public bool SupportsTenantScopedCredentials { get; init; } = true;
    public bool SupportsSandboxEnvironment { get; init; } = true;
    public bool SupportsProductionEnvironment { get; init; } = true;
    public bool SupportsConfiguredServerNotification { get; init; } = true;
    public bool SupportsPerOrderWebhookUrl { get; init; } = true;
    public bool SupportsTransactionInquiry { get; init; } = true;
    public bool SupportsExpiration { get; init; } = true;
    public bool SupportsRefund { get; init; } = true;
    public bool SupportsPartialRefund { get; init; } = true;
    public bool SupportsVoidUnpaidOrder { get; init; } = true;
    public bool SupportsAuthorizeAndCapture { get; init; } = true;
    public bool PaymentExecutionCertified { get; init; }
    public bool CallbackCertified { get; init; }
    public bool WebhookCertified { get; init; }
    public bool ProviderAdapterCertified { get; init; }
    public bool AdvancedOperationsCertified { get; init; }
}

public enum FawryEnvironment
{
    Sandbox = 0,
    Production = 1
}

public sealed class FawryOptionsValidator : IValidateOptions<FawryOptions>
{
    public ValidateOptionsResult Validate(string? name, FawryOptions options)
    {
        var result = FawryCredentialValidator.Validate(options);
        return result.IsValid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(result.Errors);
    }

    public static bool IsRuntimeEnabled(FawryOptions options) =>
        options.Enabled || options.FeatureFlags.Enabled;

    public static bool AreCredentialsConfigured(FawryOptions options) =>
        !string.IsNullOrWhiteSpace(options.MerchantCode)
        && !string.IsNullOrWhiteSpace(options.ApiKey)
        && !string.IsNullOrWhiteSpace(options.Secret);
}

public static class FawryCredentialValidator
{
    private const int MinimumCredentialLength = 8;
    private const int MinimumSecretLength = 12;

    public static FawryCredentialValidationResult Validate(FawryOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var enabled = FawryOptionsValidator.IsRuntimeEnabled(options);

        if (!options.Enabled && options.FeatureFlags.Enabled)
        {
            errors.Add("Fawry provider must be enabled when Fawry foundation feature flag is enabled.");
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            errors.Add("Fawry timeout must be positive.");
        }

        if (options.RetryCount is < 0 or > 5)
        {
            errors.Add("Fawry retry count must be between 0 and 5.");
        }

        if (options.Environment == FawryEnvironment.Sandbox && !options.SandboxMode)
        {
            errors.Add("Fawry sandbox environment must set SandboxMode to true.");
        }

        if (options.Environment == FawryEnvironment.Production && options.SandboxMode)
        {
            errors.Add("Fawry production environment must set SandboxMode to false.");
        }

        if (enabled && options.Environment == FawryEnvironment.Sandbox && !options.FeatureFlags.SandboxEnabled)
        {
            errors.Add("Fawry sandbox execution must be explicitly enabled by the sandbox feature flag.");
        }

        if (enabled && options.Environment == FawryEnvironment.Production && !options.FeatureFlags.ProductionEnabled)
        {
            errors.Add("Fawry production execution must be explicitly enabled by the production feature flag.");
        }

        if (enabled)
        {
            ValidateRequiredCredential(options.MerchantCode, "merchant code", MinimumCredentialLength, errors);
            ValidateRequiredCredential(options.ApiKey, "API key", MinimumCredentialLength, errors);
            ValidateRequiredCredential(options.Secret, "secret", MinimumSecretLength, errors);
        }

        if (options.Callback.Configured)
        {
            if (options.Callback.CallbackUrl is null || !options.Callback.CallbackUrl.IsAbsoluteUri)
            {
                errors.Add("Fawry callback URL must be an absolute server-owned URL when callback configuration is enabled.");
            }

            if (options.Callback.WebhookUrl is null || !options.Callback.WebhookUrl.IsAbsoluteUri)
            {
                errors.Add("Fawry webhook URL must be an absolute server-owned URL when callback configuration is enabled.");
            }
        }
        else if (enabled)
        {
            errors.Add("Fawry callback and webhook configuration must be explicitly configured before enabling Fawry.");
        }

        if (!options.Capabilities.SupportsOfficialHttpsApi)
        {
            errors.Add("Fawry foundation requires official HTTPS API support.");
        }

        if (!options.Capabilities.SupportsSha256Signature)
        {
            errors.Add("Fawry foundation requires official SHA-256 signature support.");
        }

        if (options.FeatureFlags.PaymentExecutionEnabled &&
            (!options.Capabilities.PaymentExecutionCertified || !options.Capabilities.ProviderAdapterCertified))
        {
            errors.Add("Fawry payment execution requires certified payment execution and provider adapter capabilities.");
        }

        if ((options.FeatureFlags.CallbackExecutionEnabled || options.FeatureFlags.WebhookExecutionEnabled) &&
            (!options.Capabilities.CallbackCertified || !options.Capabilities.WebhookCertified))
        {
            errors.Add("Fawry callback and webhook execution require certified callback and webhook capabilities.");
        }

        if ((options.FeatureFlags.RefundExecutionEnabled ||
             options.FeatureFlags.PartialRefundExecutionEnabled ||
             options.FeatureFlags.VoidExecutionEnabled ||
             options.FeatureFlags.CaptureExecutionEnabled ||
             options.FeatureFlags.InquiryExecutionEnabled) &&
            !options.Capabilities.AdvancedOperationsCertified)
        {
            errors.Add("Fawry advanced operation execution requires certified advanced operation capabilities.");
        }

        return new FawryCredentialValidationResult(
            errors.Count == 0,
            options.Environment,
            options.SandboxMode,
            FawryOptionsValidator.AreCredentialsConfigured(options),
            options.Callback.Configured,
            errors,
            warnings);
    }

    private static void ValidateRequiredCredential(
        string value,
        string name,
        int minimumLength,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"Fawry {name} is required when Fawry is enabled.");
            return;
        }

        if (value.Trim().Length < minimumLength)
        {
            errors.Add($"Fawry {name} is too short to be a real credential.");
        }
    }
}
