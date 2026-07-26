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
                break;

            case "PAYMOB":
                Require(normalizedConfiguration, "apiKey", "Gateway API key is required.", validationErrors);
                Require(normalizedConfiguration, "integrationId", "Gateway integration ID is required.", validationErrors);
                Require(normalizedConfiguration, "iframeId", "Gateway iframe ID is required.", validationErrors);
                Require(normalizedConfiguration, "hmacSecret", "Gateway HMAC secret is required.", validationErrors);
                break;

            case "FAWRY":
                Require(normalizedConfiguration, "merchantCode", "Gateway merchant code is required.", validationErrors);
                Require(normalizedConfiguration, "apiKey", "Gateway API key is required.", validationErrors);
                Require(normalizedConfiguration, "webhookSecret", "Gateway webhook secret is required.", validationErrors);
                break;

            case "MANUALWALLET":
                Require(normalizedConfiguration, "walletNumber", "Manual wallet number is required.", validationErrors);
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
