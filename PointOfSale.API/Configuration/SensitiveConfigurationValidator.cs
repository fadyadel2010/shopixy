using Microsoft.Extensions.Configuration;

namespace PointOfSale.API.Configuration;

public static class SensitiveConfigurationValidator
{
    private static readonly string[] PlaceholderMarkers =
    {
        "REPLACE_WITH",
        "CHANGE_THIS",
        "DEV-ONLY",
        "YOUR_",
        "YOUR-",
        "YOURPASSWORD",
        "YOUR_PASSWORD",
        "DEFAULT-SECRET",
        "PLACEHOLDER",
        "CHANGEME"
    };

    public static void ValidateOrThrow(IConfiguration configuration)
    {
        var failures = new List<string>();

        RequireConnectionString(configuration, "ConnectionStrings:DefaultConnection", "primary PostgreSQL connection string", failures);
        RequireNonEmpty(configuration, "BaileysService:BaseUrl", "Baileys service base URL", failures);
        RequireSecret(configuration, "BaileysService:ApiKey", "Baileys service API key", failures, minimumLength: 32);

        RequireSecret(configuration, "JwtSettings:Secret", "POS JWT signing key", failures, minimumLength: 32);
        RequireSecret(configuration, "SuperAdminJwt:Secret", "Super Admin JWT signing key", failures, minimumLength: 32);
        RequireSecret(configuration, "StorefrontJwt:Secret", "Storefront JWT signing key", failures, minimumLength: 32);
        RequireSecret(configuration, "CustomerJwt:Secret", "CommerceHub customer JWT signing key", failures, minimumLength: 32);
        RequireSecret(configuration, "AffiliateJwt:Secret", "Affiliate JWT signing key", failures, minimumLength: 32);
        RequireSecret(configuration, "CommerceHub:OrderAccessSigningKey", "CommerceHub order access signing key", failures, minimumLength: 32);
        RequireSecret(configuration, "AbandonedCart:TokenSecret", "abandoned cart token secret", failures, minimumLength: 32);

        RequireDistinctSecrets(configuration, "JwtSettings:Secret", "SuperAdminJwt:Secret", "POS and Super Admin JWT secrets", failures);
        RequireDistinctSecrets(configuration, "JwtSettings:Secret", "StorefrontJwt:Secret", "POS and Storefront JWT secrets", failures);
        RequireDistinctSecrets(configuration, "JwtSettings:Secret", "CustomerJwt:Secret", "POS and CommerceHub customer JWT secrets", failures);
        RequireDistinctSecrets(configuration, "JwtSettings:Secret", "AffiliateJwt:Secret", "POS and Affiliate JWT secrets", failures);
        RequireDistinctSecrets(configuration, "SuperAdminJwt:Secret", "StorefrontJwt:Secret", "Super Admin and Storefront JWT secrets", failures);
        RequireDistinctSecrets(configuration, "SuperAdminJwt:Secret", "CustomerJwt:Secret", "Super Admin and CommerceHub customer JWT secrets", failures);
        RequireDistinctSecrets(configuration, "SuperAdminJwt:Secret", "AffiliateJwt:Secret", "Super Admin and Affiliate JWT secrets", failures);
        RequireDistinctSecrets(configuration, "StorefrontJwt:Secret", "CustomerJwt:Secret", "Storefront and CommerceHub customer JWT secrets", failures);
        RequireDistinctSecrets(configuration, "StorefrontJwt:Secret", "AffiliateJwt:Secret", "Storefront and Affiliate JWT secrets", failures);
        RequireDistinctSecrets(configuration, "CustomerJwt:Secret", "AffiliateJwt:Secret", "CommerceHub customer and Affiliate JWT secrets", failures);

        RequireNonEmpty(configuration, "RabbitMQ:Host", "RabbitMQ host", failures);
        RequireNonEmpty(configuration, "RabbitMQ:VirtualHost", "RabbitMQ virtual host", failures);
        RequireNonEmpty(configuration, "RabbitMQ:Username", "RabbitMQ username", failures);
        RequireSecret(configuration, "RabbitMQ:Password", "RabbitMQ password", failures);

        RequireConnectionString(configuration, "MongoDB:ConnectionString", "MongoDB connection string", failures);
        RequireNonEmpty(configuration, "MongoDB:DatabaseName", "MongoDB database name", failures);
        RequireNonEmpty(configuration, "MongoDB:ProductsCollectionName", "MongoDB products collection name", failures);

        if (configuration.GetValue("Redis:Enabled", true))
        {
            RequireConnectionString(configuration, "Redis:ConnectionString", "Redis connection string", failures);
        }

        if (configuration.GetValue("Elasticsearch:Enabled", true))
        {
            RequireNonEmpty(configuration, "Elasticsearch:Url", "Elasticsearch URL", failures, rejectPlaceholders: true);
        }

        if (configuration.GetValue("Smtp:Enabled", false))
        {
            RequireNonEmpty(configuration, "Smtp:Host", "SMTP host", failures);
            RequireNonEmpty(configuration, "Smtp:Username", "SMTP username", failures);
            RequireSecret(configuration, "Smtp:Password", "SMTP password", failures);
            RequireNonEmpty(configuration, "Smtp:FromEmail", "SMTP from email", failures);
        }

        if (configuration.GetValue("CommerceAI:Enabled", false))
        {
            RequireNonEmpty(configuration, "CommerceAI:Provider", "Commerce AI provider", failures);
            RequireNonEmpty(configuration, "CommerceAI:Model", "Commerce AI model", failures);
            RequireNonEmpty(configuration, "CommerceAI:Endpoint", "Commerce AI endpoint", failures, rejectPlaceholders: true);
            RequireSecret(configuration, "CommerceAI:ApiKey", "Commerce AI API key", failures);
        }

        if (configuration.GetValue("CommerceHub:Payments:Paymob:Enabled", false))
        {
            RequireSecret(configuration, "CommerceHub:Payments:Paymob:ApiKey", "Paymob API key", failures);
            RequireNonEmpty(configuration, "CommerceHub:Payments:Paymob:IntegrationId", "Paymob integration ID", failures);
            RequireNonEmpty(configuration, "CommerceHub:Payments:Paymob:IframeId", "Paymob iframe ID", failures);
            RequireSecret(configuration, "CommerceHub:Payments:Paymob:HmacSecret", "Paymob HMAC secret", failures);
        }

        if (configuration.GetValue("CommerceHub:Payments:Stripe:Enabled", false))
        {
            RequireSecret(configuration, "CommerceHub:Payments:Stripe:SecretKey", "Stripe secret key", failures);
            RequireNonEmpty(configuration, "CommerceHub:Payments:Stripe:PublishableKey", "Stripe publishable key", failures);
            RequireSecret(configuration, "CommerceHub:Payments:Stripe:WebhookSecret", "Stripe webhook secret", failures);
        }

        ValidateOptionalR2Configuration(configuration, failures);

        if (failures.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "SECURITY CRITICAL: Missing or unsafe configuration detected. " +
            "Provide real secrets via environment variables, dotnet user-secrets, or gitignored local configuration files before startup." +
            Environment.NewLine +
            string.Join(Environment.NewLine, failures.Select(static failure => $"- {failure}")));
    }

    private static void RequireConnectionString(
        IConfiguration configuration,
        string key,
        string description,
        ICollection<string> failures)
    {
        RequireNonEmpty(configuration, key, description, failures, rejectPlaceholders: true);
    }

    private static void RequireSecret(
        IConfiguration configuration,
        string key,
        string description,
        ICollection<string> failures,
        int minimumLength = 1)
    {
        var value = configuration[key];
        if (IsUnsafeSecretValue(value))
        {
            failures.Add($"{description} is missing or still uses a committed placeholder. Configure '{key}' securely.");
            return;
        }

        if (value!.Trim().Length < minimumLength)
        {
            failures.Add($"{description} must be at least {minimumLength} characters. Configure '{key}' securely.");
        }
    }

    private static void RequireNonEmpty(
        IConfiguration configuration,
        string key,
        string description,
        ICollection<string> failures,
        bool rejectPlaceholders = false)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{description} is missing. Configure '{key}'.");
            return;
        }

        if (rejectPlaceholders && LooksLikePlaceholder(value))
        {
            failures.Add($"{description} still contains a placeholder value. Configure '{key}' with a real deployment value.");
        }
    }

    private static void RequireDistinctSecrets(
        IConfiguration configuration,
        string firstKey,
        string secondKey,
        string description,
        ICollection<string> failures)
    {
        var first = configuration[firstKey];
        var second = configuration[secondKey];

        if (IsUnsafeSecretValue(first) || IsUnsafeSecretValue(second))
        {
            return;
        }

        if (string.Equals(first!.Trim(), second!.Trim(), StringComparison.Ordinal))
        {
            failures.Add($"{description} must not reuse the same signing key.");
        }
    }

    private static void ValidateOptionalR2Configuration(IConfiguration configuration, ICollection<string> failures)
    {
        var accountId = GetTrimmedValue(configuration, "R2_ACCOUNT_ID", "R2:AccountId");
        var accessKeyId = GetTrimmedValue(configuration, "R2_ACCESS_KEY_ID", "R2:AccessKeyId");
        var secretAccessKey = GetTrimmedValue(configuration, "R2_SECRET_ACCESS_KEY", "R2:SecretAccessKey");
        var bucket = GetTrimmedValue(configuration, "R2_BUCKET", "R2:Bucket");
        var serviceUrl = GetTrimmedValue(configuration, "R2_SERVICE_URL", "R2:ServiceUrl");
        var publicUrl = GetTrimmedValue(configuration, "R2_PUBLIC_URL", "R2:PublicUrl");

        var anyConfigured = new[] { accountId, accessKeyId, secretAccessKey, bucket, serviceUrl, publicUrl }
            .Any(static value => !string.IsNullOrWhiteSpace(value));

        if (!anyConfigured)
        {
            return;
        }

        if (new[] { accountId, accessKeyId, secretAccessKey, bucket, serviceUrl, publicUrl }
            .Any(static value => !string.IsNullOrWhiteSpace(value) && LooksLikePlaceholder(value)))
        {
            failures.Add("R2 configuration contains placeholder values. Provide real values or clear the partial configuration.");
            return;
        }

        if (string.IsNullOrWhiteSpace(accessKeyId) ||
            string.IsNullOrWhiteSpace(secretAccessKey) ||
            string.IsNullOrWhiteSpace(bucket) ||
            (string.IsNullOrWhiteSpace(accountId) && string.IsNullOrWhiteSpace(serviceUrl)))
        {
            failures.Add("R2 configuration is partial. Set R2 account ID/service URL, access key ID, secret access key, and bucket together.");
        }
    }

    private static string? GetTrimmedValue(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool IsUnsafeSecretValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || LooksLikePlaceholder(value);
    }

    private static bool LooksLikePlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return PlaceholderMarkers.Any(normalized.Contains);
    }
}
