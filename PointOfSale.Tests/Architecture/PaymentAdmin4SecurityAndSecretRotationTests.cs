using System.Text.Json;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Payments.Administration;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentAdmin4SecurityAndSecretRotationTests
{
    [Fact]
    public void SecretLifecycle_MigratesExistingSecretsToVersionOneMetadata()
    {
        var administration = new PaymentProviderAdministrationService();
        administration.SaveProviderConfiguration(
            "PAYMOB",
            new PaymentProviderConfigurationRequest
            {
                Enabled = true,
                Environment = "Sandbox",
                Configuration = ValidConfiguration(),
                SecretReplacements = InitialSecrets()
            });
        var secrets = new PaymentProviderSecretService(administration);

        var metadata = secrets.GetSecretMetadata("paymob");

        Assert.Equal("PAYMOB", metadata.ProviderCode);
        Assert.Contains(PaymentProviderPermissions.ViewSecretMetadata, metadata.Permissions);
        Assert.Contains(metadata.Secrets, secret =>
            secret.SecretType == "API Key" &&
            secret.Version == 1 &&
            secret.Status == "Active" &&
            secret.CreatedBy == "migration");
        Assert.Contains(metadata.Secrets, secret =>
            secret.SecretType == "HMAC Secret" &&
            secret.Version == 1 &&
            secret.Status == "Active");
        Assert.All(metadata.Secrets, secret =>
        {
            Assert.Matches("^[A-F0-9]{4}-[A-F0-9]{4}-\\.\\.\\.\\.$", secret.Fingerprint);
        });
    }

    [Fact]
    public void RotateSecret_CreatesNewVersionRetiresPreviousAndNeverReturnsPlaintext()
    {
        var administration = new PaymentProviderAdministrationService();
        administration.SaveProviderConfiguration(
            "PAYMOB",
            new PaymentProviderConfigurationRequest
            {
                Enabled = true,
                Environment = "Production",
                Configuration = ValidConfiguration("Production"),
                SecretReplacements = InitialSecrets("Production")
            });
        var secrets = new PaymentProviderSecretService(administration);

        var rotation = secrets.RotateSecret(
            "PAYMOB",
            new RotatePaymentProviderSecretRequest
            {
                SecretType = "API Key",
                NewSecret = "paymob-next-api-key",
                ConfirmNewSecret = "paymob-next-api-key",
                Reason = "scheduled rotation",
                AdministratorConfirmation = true
            },
            Guid.NewGuid(),
            "security@example.com");

        Assert.Equal("PAYMOB", rotation.ProviderCode);
        Assert.Equal("API Key", rotation.Secret.SecretType);
        Assert.Equal(2, rotation.Secret.Version);
        Assert.Equal("Active", rotation.Secret.Status);
        Assert.Equal("security@example.com", rotation.Secret.CreatedBy);
        Assert.Contains(PaymentProviderPermissions.RotateSecrets, rotation.Permissions);

        var apiKeyVersions = rotation.Versions
            .Where(secret => secret.SecretType == "API Key")
            .OrderBy(secret => secret.Version)
            .ToList();
        Assert.Equal("Retired", apiKeyVersions[0].Status);
        Assert.NotNull(apiKeyVersions[0].RetiredAtUtc);
        Assert.Equal("Active", apiKeyVersions[1].Status);

        var operational = administration.GetProviderOperationalConfiguration("PAYMOB");
        Assert.Equal("paymob-next-api-key", operational.Credentials["apiKey"]);
        Assert.Equal("paymob-initial-hmac-secret", operational.Credentials["hmacSecret"]);

        var serialized = JsonSerializer.Serialize(rotation, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("paymob-next-api-key", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("paymob-initial-hmac-secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("paymob-initial-api-key", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void RotateSecret_ValidatesConfirmationDuplicateTypeProviderAndFormat()
    {
        var administration = new PaymentProviderAdministrationService();
        administration.SaveProviderConfiguration(
            "PAYMOB",
            new PaymentProviderConfigurationRequest
            {
                Enabled = true,
                Environment = "Sandbox",
                Configuration = ValidConfiguration(),
                SecretReplacements = InitialSecrets()
            });
        var secrets = new PaymentProviderSecretService(administration);

        Assert.Equal("SECRET_ROTATION_CONFIRMATION_REQUIRED", Assert.Throws<PaymentProviderAdministrationException>(() =>
            secrets.RotateSecret("PAYMOB", ValidRotation(administratorConfirmation: false), null, null)).Code);

        Assert.Equal("SECRET_CONFIRMATION_MISMATCH", Assert.Throws<PaymentProviderAdministrationException>(() =>
            secrets.RotateSecret("PAYMOB", ValidRotation(confirmNewSecret: "different-secret"), null, null)).Code);

        Assert.Equal("DUPLICATE_REPLACEMENT_SECRET", Assert.Throws<PaymentProviderAdministrationException>(() =>
            secrets.RotateSecret("PAYMOB", ValidRotation(
                newSecret: "paymob-initial-api-key",
                confirmNewSecret: "paymob-initial-api-key"), null, null)).Code);

        Assert.Equal("INVALID_SECRET_TYPE", Assert.Throws<PaymentProviderAdministrationException>(() =>
            secrets.RotateSecret("PAYMOB", ValidRotation(secretType: "Private Key"), null, null)).Code);

        Assert.Equal("UNKNOWN_PROVIDER", Assert.Throws<PaymentProviderAdministrationException>(() =>
            secrets.RotateSecret("STRIPE", ValidRotation(), null, null)).Code);

        Assert.Equal("INVALID_SECRET_FORMAT", Assert.Throws<PaymentProviderAdministrationException>(() =>
            secrets.RotateSecret("PAYMOB", ValidRotation(newSecret: "short", confirmNewSecret: "short"), null, null)).Code);
    }

    [Fact]
    public void BackendArchitecture_ExposesSecretLifecycleWithoutPaymentExecutionOrSecretAudit()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/AdminPaymentProvidersController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/PaymentProviderSecretService.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        var permissions = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Administration/PaymentProviderPermissions.cs");

        Assert.Contains("[HttpGet(\"{providerCode}/secrets\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"{providerCode}/secrets/rotate\")]", controller, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_PROVIDER_SECRET_ROTATED", controller, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_PROVIDER_SECRET_METADATA_VIEWED", controller, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderPermissions.RotateSecrets", controller, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderPermissions.ViewSecretMetadata", controller, StringComparison.Ordinal);
        Assert.Contains("SecretValuePersistedToAudit = false", controller, StringComparison.Ordinal);
        Assert.Contains("EncryptedSecretPersistedToAudit = false", controller, StringComparison.Ordinal);

        Assert.Contains("SHA256.HashData", service, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", service, StringComparison.Ordinal);
        Assert.Contains("PendingActivation", service, StringComparison.Ordinal);
        Assert.Contains("Retired", service, StringComparison.Ordinal);
        Assert.Contains("IPaymentProviderSecretService", program, StringComparison.Ordinal);
        Assert.Contains("PaymentProviders.ViewSecretMetadata", permissions, StringComparison.Ordinal);

        Assert.DoesNotContain("PaymobPaymentService", service, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobPaymentService", service, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatePaymentKeyAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterOrderAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("RefundAsync", service, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendRendersProviderIndependentSecretLifecycleComponents()
    {
        var page = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/PaymentProvidersPage.tsx");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/paymentProvidersService.ts");
        var types = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/paymentProviderTypes.ts");
        var secretCard = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/SecretCard.tsx");
        var versionList = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/SecretVersionList.tsx");
        var dialog = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/RotateSecretDialog.tsx");
        var badge = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/SecretStatusBadge.tsx");
        var metadata = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/SecretMetadataPanel.tsx");
        var frontend = page + service + types + secretCard + versionList + dialog + badge + metadata;

        foreach (var component in new[]
                 {
                     "SecretCard",
                     "SecretVersionList",
                     "RotateSecretDialog",
                     "SecretStatusBadge",
                     "SecretMetadataPanel"
                 })
        {
            Assert.Contains(component, frontend, StringComparison.Ordinal);
        }

        Assert.Contains("/secrets/rotate", service, StringComparison.Ordinal);
        Assert.Contains("getPaymentProviderSecrets", service, StringComparison.Ordinal);
        Assert.Contains("type=\"password\"", dialog, StringComparison.Ordinal);
        Assert.Contains("autoComplete=\"new-password\"", dialog, StringComparison.Ordinal);
        Assert.Contains("administratorConfirmation", dialog, StringComparison.Ordinal);
        Assert.Contains("fingerprint", metadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", frontend, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", frontend, StringComparison.Ordinal);
        Assert.DoesNotContain("zustand", frontend, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DocumentationArtifactExistsWithRequiredSections()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/payment/PAYMENT-ADMIN-4_SECURITY_AND_SECRET_ROTATION.md");

        foreach (var heading in new[]
                 {
                     "Executive Summary",
                     "Secret Lifecycle",
                     "Secret Versioning",
                     "Rotation Workflow",
                     "Metadata",
                     "Fingerprints",
                     "API Design",
                     "Frontend Design",
                     "Backend Design",
                     "Security",
                     "Audit",
                     "Migration",
                     "Testing",
                     "Files Added",
                     "Files Modified",
                     "Compatibility",
                     "Readiness for PAYMENT-ADMIN-5"
                 })
        {
            Assert.Contains(heading, document, StringComparison.Ordinal);
        }
    }

    private static RotatePaymentProviderSecretRequest ValidRotation(
        string secretType = "API Key",
        string newSecret = "paymob-next-api-key",
        string confirmNewSecret = "paymob-next-api-key",
        bool administratorConfirmation = true) => new()
    {
        SecretType = secretType,
        NewSecret = newSecret,
        ConfirmNewSecret = confirmNewSecret,
        Reason = "scheduled rotation",
        AdministratorConfirmation = administratorConfirmation
    };

    private static Dictionary<string, object?> ValidConfiguration(string environment = "Sandbox") =>
        new(StringComparer.Ordinal)
        {
            ["enabled"] = true,
            ["displayName"] = "Paymob",
            ["environment"] = environment,
            ["sandbox"] = environment == "Sandbox",
            ["currency"] = "EGP",
            ["description"] = "Paymob provider account for CommerceHub.",
            ["integrationId"] = "123456",
            ["iframeId"] = "654321",
            ["publicKey"] = environment == "Production" ? "egy_pk_live_test-public" : "egy_pk_test_test-public",
            ["merchantIdentifier"] = "123",
            ["providerAccountName"] = "CommerceHub Paymob",
            ["webhookUrl"] = "https://api.example.test/api/payments/paymob/webhook",
            ["browserCallbackUrl"] = "https://market.example.test/api/commercehub/payments/paymob/callback",
            ["timeoutSeconds"] = "30",
            ["retryCount"] = "2",
            ["requestTimeoutSeconds"] = "30",
            ["maximumRetryAttempts"] = "3"
        };

    private static Dictionary<string, string?> InitialSecrets(string environment = "Sandbox") =>
        new(StringComparer.Ordinal)
        {
            ["apiKey"] = "paymob-initial-api-key",
            ["secretKey"] = environment == "Production"
                ? "egy_sk_live_initial-secret"
                : "egy_sk_test_initial-secret",
            ["hmacSecret"] = "paymob-initial-hmac-secret"
        };
}
