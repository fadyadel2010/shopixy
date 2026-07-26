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
        Assert.Equal("/api/commercehub/payments/paymob/webhook", provider.Configuration["webhookUrl"]);
        Assert.Equal("GET", provider.Configuration["browserCallbackMethod"]);
        Assert.Equal("/api/commercehub/payments/paymob/callback", provider.Configuration["browserCallbackUrl"]);
        Assert.Equal("**************", provider.Configuration["apiKeyMask"]);
        Assert.Equal("**************", provider.Configuration["hmacSecretMask"]);
        Assert.False((bool)provider.Configuration["apiKeyConfigured"]!);
        Assert.False((bool)provider.Configuration["hmacSecretConfigured"]!);
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
                    ["apiKey"] = "paymob-live-api-key",
                    ["hmacSecret"] = "paymob-live-hmac-secret"
                }
            });

        Assert.Equal("Configured", result.Provider.Status);
        Assert.True(result.Provider.Enabled);
        Assert.Equal("Production", result.Provider.Environment);
        Assert.True((bool)result.Provider.Configuration["apiKeyConfigured"]!);
        Assert.True((bool)result.Provider.Configuration["hmacSecretConfigured"]!);
        Assert.Contains("credentials.apiCredential:redacted", result.RedactedChangedFields);
        Assert.Contains("credentials.signatureCredential:redacted", result.RedactedChangedFields);

        var serialized = JsonSerializer.Serialize(result.Provider, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("paymob-live-api-key", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("paymob-live-hmac-secret", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymobConfiguration_ValidatesRequiredSecretsIdsCurrencyEnvironmentAndRuntimeNumbers()
    {
        var service = new PaymentProviderAdministrationService();
        var configuration = ValidConfiguration("Mars");
        configuration["currency"] = "EG";
        configuration["integrationId"] = "0";
        configuration["iframeId"] = "abc";
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
        Assert.Contains("Missing API Key", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Missing HMAC Secret", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Invalid Integration ID", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Invalid iFrame ID", exception.Message, StringComparison.Ordinal);
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
            ["iframeId"] = "654321",
            ["merchantIdentifier"] = "merchant-1",
            ["providerAccountName"] = "CommerceHub Paymob",
            ["timeoutSeconds"] = "30",
            ["retryCount"] = "2",
            ["requestTimeoutSeconds"] = "30",
            ["maximumRetryAttempts"] = "3"
        };
}
