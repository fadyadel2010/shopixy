namespace PointOfSale.Core.Payments;

/// <summary>
/// Shared validation rules for tenant payment-method configuration.
/// The write model remains encrypted, but every caller should agree on the
/// same provider-specific definition of "valid configuration".
/// </summary>
public static class PaymentMethodConfigurationValidator
{
    public static PaymentMethodConfigurationValidationResult Validate(
        string code,
        IReadOnlyDictionary<string, string>? configuration)
    {
        var normalizedCode = NormalizeCode(code);
        var normalizedConfiguration = NormalizeConfiguration(normalizedCode, configuration);
        var validationErrors = new List<string>();

        switch (normalizedCode)
        {
            case "C" + "OD":
                break;

            case "STRIPE":
                Require(normalizedConfiguration, "publishableKey", "Gateway publishable key is required.", validationErrors);
                Require(normalizedConfiguration, "secretKey", "Gateway API secret key is required.", validationErrors);
                Require(normalizedConfiguration, "webhookSecret", "Gateway webhook secret is required.", validationErrors);
                ValidateStripeConfiguration(normalizedConfiguration, validationErrors);
                break;

            case "PAYMOB":
                if ((normalizedConfiguration.ContainsKey("apiKey") || normalizedConfiguration.ContainsKey("iframeId")) &&
                    (!normalizedConfiguration.ContainsKey("publicKey") || !normalizedConfiguration.ContainsKey("secretKey")))
                {
                    validationErrors.Add("Legacy Paymob credentials require replacement with modern public and secret keys before enabling checkout.");
                }

                Require(normalizedConfiguration, "publicKey", "Gateway public key is required.", validationErrors);
                Require(normalizedConfiguration, "secretKey", "Gateway secret key is required.", validationErrors);
                Require(normalizedConfiguration, "integrationId", "Gateway integration ID is required.", validationErrors);
                Require(normalizedConfiguration, "hmacSecret", "Gateway HMAC secret is required.", validationErrors);
                ValidatePaymobConfiguration(normalizedConfiguration, validationErrors);
                break;

            case "FAWRY":
                Require(normalizedConfiguration, "merchantCode", "Gateway merchant code is required.", validationErrors);
                Require(normalizedConfiguration, "apiKey", "Gateway API key is required.", validationErrors);
                Require(normalizedConfiguration, "webhookSecret", "Gateway webhook secret is required.", validationErrors);
                break;

            case "MANUALWALLET":
                Require(normalizedConfiguration, "walletNumber", "Manual wallet number is required.", validationErrors);
                if (normalizedConfiguration.TryGetValue("walletNumber", out var walletNumber) &&
                    !IsValidManualWalletNumber(walletNumber))
                {
                    validationErrors.Add("Manual wallet number must contain 8 to 20 digits and only phone-number characters (+, spaces, hyphens or parentheses).");
                }
                break;

            default:
                validationErrors.Add($"Unsupported payment method: {normalizedCode}");
                break;
        }

        return new PaymentMethodConfigurationValidationResult(
            normalizedCode,
            RequiresConfiguration(normalizedCode),
            validationErrors.Count == 0,
            normalizedConfiguration,
            validationErrors,
            BuildPublicConfiguration(normalizedCode, normalizedConfiguration));
    }

    public static Dictionary<string, string> NormalizeConfiguration(
        string code,
        IReadOnlyDictionary<string, string>? configuration)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (configuration != null)
        {
            foreach (var pair in configuration)
            {
                var key = pair.Key?.Trim();
                var value = pair.Value?.Trim();

                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                normalized[key] = value;
            }
        }

        var normalizedCode = NormalizeCode(code);

        if (normalizedCode == "STRIPE" &&
            !normalized.ContainsKey("secretKey") &&
            normalized.TryGetValue("apiKey", out var stripeApiKey))
        {
            normalized["secretKey"] = stripeApiKey;
        }

        if (normalizedCode == "FAWRY" &&
            !normalized.ContainsKey("apiKey") &&
            normalized.TryGetValue("securityKey", out var fawrySecurityKey))
        {
            normalized["apiKey"] = fawrySecurityKey;
        }

        if (normalizedCode == "PAYMOB" &&
            normalized.TryGetValue("integrationId", out var paymobIntegrationIds))
        {
            normalized["integrationId"] = TryParsePaymobIntegrationIds(paymobIntegrationIds, out var parsedIds)
                ? string.Join(",", parsedIds)
                : string.Join(",", SplitPaymobIntegrationIds(paymobIntegrationIds));
        }

        return normalized;
    }

    public static Dictionary<string, string> MergeConfiguration(
        string code,
        IReadOnlyDictionary<string, string>? existingConfiguration,
        IReadOnlyDictionary<string, string>? incomingConfiguration)
    {
        var merged = NormalizeConfiguration(code, existingConfiguration);
        var normalizedIncoming = NormalizeConfiguration(code, incomingConfiguration);

        foreach (var pair in normalizedIncoming)
        {
            merged[pair.Key] = pair.Value;
        }

        return NormalizeConfiguration(code, merged);
    }

    public static bool RequiresConfiguration(string code)
    {
        var normalizedCode = NormalizeCode(code);
        return normalizedCode is "STRIPE" or "PAYMOB" or "FAWRY" or "MANUALWALLET";
    }

    // Same provider-neutral phone contract already used by Manual Wallet checkout.
    // Do not impose a country prefix or a specific mobile operator.
    public static bool IsValidManualWalletNumber(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), @"^(?=(?:\D*\d){8,20}\D*$)[\d\s\-+()]+$");

    private static string NormalizeCode(string? code) =>
        code?.Trim().ToUpperInvariant() ?? string.Empty;

    private static void Require(
        IReadOnlyDictionary<string, string> configuration,
        string key,
        string message,
        ICollection<string> validationErrors)
    {
        if (!configuration.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            validationErrors.Add(message);
        }
    }

    private static void ValidatePaymobConfiguration(
        IReadOnlyDictionary<string, string> configuration,
        ICollection<string> validationErrors)
    {
        if (configuration.TryGetValue("integrationId", out var integrationIds) &&
            !TryParsePaymobIntegrationIds(integrationIds, out _))
        {
            validationErrors.Add("Gateway integration IDs must be one or more positive integers separated by commas.");
        }

        ValidateOptionalPositiveInteger(configuration, "merchantId", "Gateway merchant ID must be a positive integer when provided.", validationErrors);
        ValidateOptionalPositiveInteger(configuration, "iframeId", "Gateway legacy iframe ID must be a positive integer when provided.", validationErrors);

        if (!configuration.TryGetValue("publicKey", out var publicKey) ||
            !configuration.TryGetValue("secretKey", out var secretKey))
        {
            return;
        }

        var publicEnvironment = ResolvePaymobKeyEnvironment(publicKey, "egy_pk_");
        var secretEnvironment = ResolvePaymobKeyEnvironment(secretKey, "egy_sk_");
        if (publicEnvironment is null)
        {
            validationErrors.Add("Gateway public key must be a Paymob Egypt test or live public key.");
        }

        if (secretEnvironment is null)
        {
            validationErrors.Add("Gateway secret key must be a Paymob Egypt test or live secret key.");
        }

        if (publicEnvironment is not null &&
            secretEnvironment is not null &&
            !string.Equals(publicEnvironment, secretEnvironment, StringComparison.Ordinal))
        {
            validationErrors.Add("Gateway public and secret keys must belong to the same Paymob environment.");
        }
    }

    private static void ValidateStripeConfiguration(
        IReadOnlyDictionary<string, string> configuration,
        ICollection<string> validationErrors)
    {
        if (!configuration.TryGetValue("publishableKey", out var publishableKey) ||
            !configuration.TryGetValue("secretKey", out var secretKey))
        {
            return;
        }

        var publishableEnvironment = ResolveStripeKeyEnvironment(publishableKey, "pk_");
        var secretEnvironment = ResolveStripeSecretEnvironment(secretKey);
        if (publishableEnvironment is null)
        {
            validationErrors.Add("Gateway publishable key must be a Stripe test or live publishable key.");
        }

        if (secretEnvironment is null)
        {
            validationErrors.Add("Gateway secret key must be a Stripe test or live secret or restricted key.");
        }

        if (publishableEnvironment is not null &&
            secretEnvironment is not null &&
            !string.Equals(publishableEnvironment, secretEnvironment, StringComparison.Ordinal))
        {
            validationErrors.Add("Gateway publishable and secret keys must belong to the same Stripe environment.");
        }

        if (configuration.TryGetValue("webhookSecret", out var webhookSecret) &&
            !webhookSecret.StartsWith("whsec_", StringComparison.Ordinal))
        {
            validationErrors.Add("Gateway webhook secret must use the Stripe whsec_ prefix.");
        }
    }

    public static bool TryParsePaymobIntegrationIds(
        string? value,
        out IReadOnlyList<int> integrationIds)
    {
        var parsed = new List<int>();
        var seen = new HashSet<int>();

        foreach (var token in SplitPaymobIntegrationIds(value))
        {
            if (!int.TryParse(token, out var integrationId) || integrationId <= 0)
            {
                integrationIds = Array.Empty<int>();
                return false;
            }

            if (seen.Add(integrationId))
            {
                parsed.Add(integrationId);
            }
        }

        integrationIds = parsed;
        return parsed.Count > 0;
    }

    private static IEnumerable<string> SplitPaymobIntegrationIds(string? value) =>
        (value ?? string.Empty).Split(
            [',', ';', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void ValidatePositiveInteger(
        IReadOnlyDictionary<string, string> configuration,
        string key,
        string message,
        ICollection<string> validationErrors)
    {
        if (configuration.TryGetValue(key, out var value) &&
            (!long.TryParse(value, out var parsed) || parsed <= 0))
        {
            validationErrors.Add(message);
        }
    }

    private static void ValidateOptionalPositiveInteger(
        IReadOnlyDictionary<string, string> configuration,
        string key,
        string message,
        ICollection<string> validationErrors)
    {
        if (!configuration.ContainsKey(key))
        {
            return;
        }

        ValidatePositiveInteger(configuration, key, message, validationErrors);
    }

    private static string? ResolvePaymobKeyEnvironment(string value, string prefix)
    {
        if (value.StartsWith($"{prefix}test_", StringComparison.Ordinal))
        {
            return "test";
        }

        return value.StartsWith($"{prefix}live_", StringComparison.Ordinal)
            ? "live"
            : null;
    }

    private static string? ResolveStripeKeyEnvironment(string value, string prefix)
    {
        if (value.StartsWith($"{prefix}test_", StringComparison.Ordinal))
        {
            return "test";
        }

        return value.StartsWith($"{prefix}live_", StringComparison.Ordinal)
            ? "live"
            : null;
    }

    private static string? ResolveStripeSecretEnvironment(string value)
    {
        return ResolveStripeKeyEnvironment(value, "sk_") ??
               ResolveStripeKeyEnvironment(value, "rk_");
    }

    private static IReadOnlyDictionary<string, string> BuildPublicConfiguration(
        string code,
        IReadOnlyDictionary<string, string> normalizedConfiguration)
    {
        if (!string.Equals(code, "MANUALWALLET", StringComparison.OrdinalIgnoreCase) ||
            !normalizedConfiguration.TryGetValue("walletNumber", out var walletNumber) ||
            string.IsNullOrWhiteSpace(walletNumber))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["walletNumber"] = walletNumber
        };
    }
}

public sealed record PaymentMethodConfigurationValidationResult(
    string Code,
    bool RequiresConfiguration,
    bool IsConfigurationValid,
    IReadOnlyDictionary<string, string> NormalizedConfiguration,
    IReadOnlyList<string> ValidationErrors,
    IReadOnlyDictionary<string, string> PublicConfiguration);
