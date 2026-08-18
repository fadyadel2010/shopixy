using Microsoft.Extensions.Options;
using PointOfSale.API.Services.Payments.Stripe;

namespace PointOfSale.API.Configuration;

public sealed record StripeOptions
{
    public const string SectionName = "CommerceHub:Payments:Stripe";
    public const string CertifiedApiVersion = "2026-06-24.dahlia";

    public bool Enabled { get; init; }
    public string PublishableKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string WebhookSecret { get; init; } = string.Empty;
    public string ApiVersion { get; init; } = CertifiedApiVersion;
    public StripeEnvironment Environment { get; init; } = StripeEnvironment.Sandbox;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);
    public int RetryCount { get; init; } = 2;
    public bool TelemetryEnabled { get; init; } = true;
    public bool SandboxMode { get; init; } = true;
    public StripeFeatureFlags FeatureFlags { get; init; } = new();
}

public sealed record StripeFeatureFlags
{
    public bool Enabled { get; init; }
    public bool SandboxEnabled { get; init; } = true;
    public bool ProductionEnabled { get; init; }
    public bool PaymentIntentsEnabled { get; init; }
    public bool CheckoutSessionsEnabled { get; init; }
    public bool WebhooksEnabled { get; init; }
    public bool CaptureEnabled { get; init; }
    public bool RefundsEnabled { get; init; }
    public bool CustomerPortalEnabled { get; init; }
}

public enum StripeEnvironment
{
    Sandbox = 0,
    Production = 1
}

public sealed class StripeOptionsValidator : IValidateOptions<StripeOptions>
{
    private const int MinimumSecretLength = 16;

    public ValidateOptionsResult Validate(string? name, StripeOptions options)
    {
        var result = StripeCredentialValidator.Validate(options);
        return result.IsValid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(result.Errors);
    }

    public static bool IsRuntimeEnabled(StripeOptions options)
    {
        return options.Enabled || options.FeatureFlags.Enabled;
    }

    public static bool RequiresGlobalCredentials(StripeOptions options)
    {
        return options.Enabled;
    }

    public static bool IsSecretConfigured(StripeOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.PublishableKey)
            && !string.IsNullOrWhiteSpace(options.SecretKey)
            && !string.IsNullOrWhiteSpace(options.WebhookSecret);
    }

    internal static bool HasMinimumSecretLength(string value)
    {
        return value.Trim().Length >= MinimumSecretLength;
    }
}

public static class StripeCredentialValidator
{
    public static CredentialValidationResult Validate(StripeOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var enabled = StripeOptionsValidator.IsRuntimeEnabled(options);

        if (options.Timeout <= TimeSpan.Zero)
        {
            errors.Add("Stripe timeout must be positive.");
        }

        if (options.RetryCount is < 0 or > 5)
        {
            errors.Add("Stripe retry count must be between 0 and 5.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiVersion))
        {
            errors.Add("Stripe API version must be configured.");
        }
        else if (enabled && !string.Equals(options.ApiVersion, StripeOptions.CertifiedApiVersion, StringComparison.Ordinal))
        {
            errors.Add($"Stripe API version must match the certified Stripe.net version ({StripeOptions.CertifiedApiVersion}).");
        }

        if (options.Environment == StripeEnvironment.Sandbox && !options.SandboxMode)
        {
            errors.Add("Stripe sandbox environment must set SandboxMode to true.");
        }

        if (options.Environment == StripeEnvironment.Production && options.SandboxMode)
        {
            errors.Add("Stripe production environment must set SandboxMode to false.");
        }

        if (enabled && options.Environment == StripeEnvironment.Sandbox && !options.FeatureFlags.SandboxEnabled)
        {
            errors.Add("Stripe sandbox execution must be explicitly enabled by the sandbox feature flag.");
        }

        if (enabled && options.Environment == StripeEnvironment.Production && !options.FeatureFlags.ProductionEnabled)
        {
            errors.Add("Stripe production execution must be explicitly enabled by the production feature flag.");
        }

        // Global CommerceHub execution uses these options directly. Tenant storefront
        // execution is independently feature-gated and resolves encrypted tenant keys.
        if (StripeOptionsValidator.RequiresGlobalCredentials(options))
        {
            ValidateRequiredCredential(options.PublishableKey, "publishable key", errors);
            ValidateRequiredCredential(options.SecretKey, "secret key", errors);
            ValidateRequiredCredential(options.WebhookSecret, "webhook secret", errors);
        }

        if (!string.IsNullOrWhiteSpace(options.PublishableKey))
        {
            ValidatePublishableKeyMode(options, errors);
        }

        if (!string.IsNullOrWhiteSpace(options.SecretKey))
        {
            ValidateSecretKeyMode(options, errors, warnings);
        }

        if (!string.IsNullOrWhiteSpace(options.WebhookSecret) &&
            !options.WebhookSecret.StartsWith("whsec_", StringComparison.Ordinal))
        {
            errors.Add("Stripe webhook secret must use the whsec_ prefix.");
        }

        return new CredentialValidationResult(
            errors.Count == 0,
            options.Environment,
            options.SandboxMode,
            StripeOptionsValidator.IsSecretConfigured(options),
            errors,
            warnings);
    }

    private static void ValidateRequiredCredential(string value, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"Stripe {name} is required when Stripe is enabled.");
            return;
        }

        if (!StripeOptionsValidator.HasMinimumSecretLength(value))
        {
            errors.Add($"Stripe {name} is too short to be a real credential.");
        }
    }

    private static void ValidatePublishableKeyMode(StripeOptions options, ICollection<string> errors)
    {
        var expectedPrefix = options.Environment == StripeEnvironment.Production ? "pk_live_" : "pk_test_";
        if (!options.PublishableKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            errors.Add($"Stripe publishable key must use the {expectedPrefix} prefix for {options.Environment}.");
        }
    }

    private static void ValidateSecretKeyMode(
        StripeOptions options,
        ICollection<string> errors,
        ICollection<string> warnings)
    {
        var expectedSecretPrefix = options.Environment == StripeEnvironment.Production ? "sk_live_" : "sk_test_";
        var expectedRestrictedPrefix = options.Environment == StripeEnvironment.Production ? "rk_live_" : "rk_test_";

        if (options.SecretKey.StartsWith(expectedSecretPrefix, StringComparison.Ordinal))
        {
            warnings.Add("Stripe unrestricted secret key is configured; restricted keys are preferred for runtime operations.");
            return;
        }

        if (!options.SecretKey.StartsWith(expectedRestrictedPrefix, StringComparison.Ordinal))
        {
            errors.Add(
                $"Stripe secret key must use {expectedSecretPrefix} or {expectedRestrictedPrefix} for {options.Environment}.");
        }
    }
}
