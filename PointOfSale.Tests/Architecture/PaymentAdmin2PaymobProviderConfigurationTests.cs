using System.Text.Json;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentAdmin2PaymobProviderConfigurationTests
{
    [Fact]
    public void PaymobConfiguration_LoadsDefaultConfigurationWithMaskedSecretsAndCallbacks()
    {
        var service = new PaymentProviderAdministrationService();

        var provider = service.GetProviderConfiguration("paymob");

        Assert.Equal("PAYMOB", provider.ProviderCode);
        Assert.Equal("Sandbox", provider.Environment);
        Assert.False(provider.Enabled);
        Assert.Equal("POST", provider.Configuration["webhookMethod"]);
        Assert.Equal("/api/payments/paymob/webhook", provider.Configuration["webhookPath"]);
        Assert.Equal("GET", provider.Configuration["browserCallbackMethod"]);
        Assert.Equal("/api/commercehub/payments/paymob/callback", provider.Configuration["browserCallbackPath"]);
        Assert.Equal("**************", provider.Configuration["secretKeyMask"]);
        Assert.Equal("**************", provider.Configuration["hmacSecretMask"]);
        Assert.Equal("**************", provider.Configuration["apiKeyMask"]);
        Assert.False((bool)provider.Configuration["secretKeyConfigured"]!);
        Assert.False((bool)provider.Configuration["hmacSecretConfigured"]!);
        Assert.False((bool)provider.Configuration["apiKeyConfigured"]!);
    }

    [Fact]
    public void PaymobConfiguration_SavePersistsValidConfigurationAndNeverSerializesSecretValues()
    {
        var service = new PaymentProviderAdministrationService();

        var result = service.SaveProviderConfiguration(
            "PAYMOB",
            new PaymentProviderConfigurationRequest
            {
                Enabled = true,
                Environment = "Production",
                Configuration = ValidConfiguration("Production"),
                SecretReplacements = new Dictionary<string, string?>
                {
                    ["apiKey"] = "paymob-dashboard-api-key",
                    ["secretKey"] = "egy_sk_live_test-credential",
                    ["hmacSecret"] = "paymob-live-hmac-secret"
                }
            });

        Assert.Equal("Configured", result.Provider.Status);
        Assert.True(result.Provider.Enabled);
        Assert.Equal("Production", result.Provider.Environment);
        Assert.True((bool)result.Provider.Configuration["secretKeyConfigured"]!);
        Assert.True((bool)result.Provider.Configuration["hmacSecretConfigured"]!);
        Assert.True((bool)result.Provider.Configuration["apiKeyConfigured"]!);
        Assert.Contains("credentials.accountApiKey:redacted", result.RedactedChangedFields);
        Assert.Contains("credentials.apiCredential:redacted", result.RedactedChangedFields);
        Assert.Contains("credentials.signatureCredential:redacted", result.RedactedChangedFields);

        var serialized = JsonSerializer.Serialize(result.Provider, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("paymob-dashboard-api-key", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("egy_sk_live_test-credential", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("paymob-live-hmac-secret", serialized, StringComparison.Ordinal);

        var operational = service.GetProviderOperationalConfiguration("PAYMOB");
        Assert.Equal("paymob-dashboard-api-key", operational.Credentials["apiKey"]);
    }

    [Fact]
    public void PaymobConfiguration_ValidatesRequiredSecretsIdsCurrencyEnvironmentAndRuntimeNumbers()
    {
        var service = new PaymentProviderAdministrationService();
        var configuration = ValidConfiguration("Mars");
        configuration["currency"] = "EG";
        configuration["integrationId"] = "0";
        configuration["merchantIdentifier"] = "0";
        configuration["timeoutSeconds"] = "-1";
        configuration["retryCount"] = "-2";

        var exception = Assert.Throws<PaymentProviderAdministrationException>(() =>
            service.SaveProviderConfiguration(
                "PAYMOB",
                new PaymentProviderConfigurationRequest
                {
                    Enabled = true,
                    Environment = "Mars",
                    Configuration = configuration
                }));

        Assert.Equal("PAYMOB_CONFIGURATION_VALIDATION_FAILED", exception.Code);
        Assert.Contains("Missing Secret Key", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Missing API Key", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Missing HMAC Secret", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Invalid Integration ID", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Invalid Merchant Identifier", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Negative Timeout", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Negative Retry Count", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Invalid Currency", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Unknown Environment", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymobConfiguration_RejectsSecretsInsideConfigurationEnvelope()
    {
        var service = new PaymentProviderAdministrationService();

        var exception = Assert.Throws<PaymentProviderAdministrationException>(() =>
            service.SaveProviderConfiguration(
                "PAYMOB",
                new PaymentProviderConfigurationRequest
                {
                    Configuration = new Dictionary<string, object?> { ["hmacSecret"] = "raw-secret" }
                }));

        Assert.Equal("PROVIDER_SECRET_NOT_ACCEPTED", exception.Code);
    }

    [Fact]
    public void PaymobConfiguration_BlankSecretReplacementPreservesExistingApiKey()
    {
        var service = new PaymentProviderAdministrationService();
        var initial = service.SaveProviderConfiguration(
            "PAYMOB",
            new PaymentProviderConfigurationRequest
            {
                Enabled = false,
                Environment = "Sandbox",
                Configuration = ValidConfiguration("Sandbox"),
                SecretReplacements = new Dictionary<string, string?>
                {
                    ["apiKey"] = "paymob-dashboard-api-key",
                    ["secretKey"] = "egy_sk_test_checkout-secret",
                    ["hmacSecret"] = "paymob-test-hmac-secret"
                }
            });

        var second = service.SaveProviderConfiguration(
            "PAYMOB",
            new PaymentProviderConfigurationRequest
            {
                Enabled = false,
                Environment = "Sandbox",
                Configuration = ValidConfiguration("Sandbox"),
                SecretReplacements = new Dictionary<string, string?>
                {
                    ["apiKey"] = "   "
                }
            });

        Assert.True((bool)initial.Provider.Configuration["apiKeyConfigured"]!);
        Assert.True((bool)second.Provider.Configuration["apiKeyConfigured"]!);
        Assert.Equal("paymob-dashboard-api-key", service.GetProviderOperationalConfiguration("PAYMOB").Credentials["apiKey"]);
    }

    [Fact]
    public void PaymobConfiguration_SaveCanonicalizesCallbackUrlWhitespaceBeforeProviderExecution()
    {
        var service = new PaymentProviderAdministrationService();
        var configuration = ValidConfiguration("Sandbox");
        configuration["webhookUrl"] = "  https://api.example.test/api/payments/paymob/webhook  ";
        configuration["browserCallbackUrl"] = "  https://market.example.test/payments/paymob/callback  ";

        service.SaveProviderConfiguration(
            "PAYMOB",
            new PaymentProviderConfigurationRequest
            {
                Enabled = true,
                Environment = "Sandbox",
                Configuration = configuration,
                SecretReplacements = new Dictionary<string, string?>
                {
                    ["apiKey"] = "paymob-dashboard-api-key",
                    ["secretKey"] = "egy_sk_test_checkout-secret",
                    ["hmacSecret"] = "paymob-test-hmac-secret"
                }
            });

        var operational = service.GetProviderOperationalConfiguration("PAYMOB");
        Assert.Equal("https://api.example.test/api/payments/paymob/webhook", operational.Configuration["webhookUrl"]);
        Assert.Equal("https://market.example.test/payments/paymob/callback", operational.Configuration["browserCallbackUrl"]);
    }

    [Fact]
    public void PaymentAdmin2_BackendAuditAndRuntimeIsolationRemainAdministrativeOnly()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/AdminPaymentProvidersController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/PaymentProviderAdministrationService.cs");
        var runtime = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");

        Assert.Contains("[HttpPut(\"{providerCode}\")]", controller, StringComparison.Ordinal);
        Assert.Contains("ChangedFields = saveResult.RedactedChangedFields", controller, StringComparison.Ordinal);
        Assert.Contains("SecretsRedacted = true", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretReplacements", controller, StringComparison.Ordinal);

        Assert.DoesNotContain("PaymobPaymentService", service, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobPaymentService", service, StringComparison.Ordinal);
        Assert.Contains("ProcessWebhookAsync", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentAdmin2_FrontendRendersPaymobFormAndKeepsCallbacksReadOnly()
    {
        var page = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/PaymentProvidersPage.tsx");
        var form = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/PaymobConfigurationForm.tsx");
        var saveBar = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/SaveBar.tsx");

        Assert.Contains("PaymentProviderLayout", page, StringComparison.Ordinal);
        Assert.Contains("secretReplacementsRef", page, StringComparison.Ordinal);
        Assert.Contains("secretReplacements: secretReplacementsRef.current", page, StringComparison.Ordinal);
        Assert.Contains("PaymobConfigurationForm", form, StringComparison.Ordinal);
        Assert.Contains("apiKeyConfigured", form, StringComparison.Ordinal);
        Assert.Contains("onSecretReplacementChange('apiKey'", form, StringComparison.Ordinal);
        Assert.Contains("API Key", form, StringComparison.Ordinal);
        Assert.Contains("type=\"password\"", form, StringComparison.Ordinal);
        Assert.Contains("autoComplete=\"new-password\"", form, StringComparison.Ordinal);
        Assert.Contains("readOnly", form, StringComparison.Ordinal);
        Assert.Contains("Callback Information", form, StringComparison.Ordinal);
        Assert.Contains("Validation Summary", page, StringComparison.Ordinal);
        Assert.Contains("Cancel Changes", saveBar, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentAdmin2_DocumentationArtifactExistsWithRequiredSections()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/payment/PAYMENT-ADMIN-2_PAYMOB_PROVIDER_CONFIGURATION.md");

        foreach (var heading in new[]
                 {
                     "Executive Summary",
                     "Configuration Architecture",
                     "General Section",
                     "Credentials",
                     "Integration",
                     "Runtime Settings",
                     "Callback Information",
                     "Validation",
                     "Save Workflow",
                     "Audit Model",
                     "API Design",
                     "Frontend Design",
                     "Security",
                     "Testing",
                     "Files Added",
                     "Files Modified",
                     "Compatibility",
                     "Readiness for PAYMENT-ADMIN-3"
                 })
        {
            Assert.Contains(heading, document, StringComparison.Ordinal);
        }
    }

    private static Dictionary<string, object?> ValidConfiguration(string environment) =>
        new(StringComparer.Ordinal)
        {
            ["enabled"] = true,
            ["displayName"] = "Paymob",
            ["environment"] = environment,
            ["sandbox"] = environment == "Sandbox",
            ["currency"] = "EGP",
            ["description"] = "Paymob provider account for CommerceHub.",
            ["integrationId"] = "123456",
            ["publicKey"] = environment == "Production" ? "egy_pk_live_test-public" : "egy_pk_test_test-public",
            ["merchantIdentifier"] = "123",
            ["providerAccountName"] = "CommerceHub Paymob",
            ["timeoutSeconds"] = "30",
            ["retryCount"] = "2",
            ["requestTimeoutSeconds"] = "30",
            ["maximumRetryAttempts"] = "3",
            ["webhookUrl"] = "https://api.example.test/api/payments/paymob/webhook",
            ["browserCallbackUrl"] = "https://market.example.test/payments/paymob/callback"
        };
}
