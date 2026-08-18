using System.Text.Json;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Payments.Administration;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentProviderAdministrationFoundationTests
{
    [Fact]
    public void BackendAdministrationSurface_MustUseCanonicalProviderIndependentRoutesAndSuperAdminAuthority()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/AdminPaymentProvidersController.cs");
        var authController = CertificationSource.ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminAuthController.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("[Route(\"api/admin/payment-providers\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"{providerCode}\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPut(\"{providerCode}\")]", controller, StringComparison.Ordinal);
        Assert.Contains("public sealed class AdminPaymentProvidersController : SuperAdminBaseController", controller, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_PROVIDERS_VIEWED", controller, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_PROVIDER_CONFIGURATION_SAVED", controller, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderPermissions.All", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("/paymob/settings", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/stripe/settings", controller, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("\"super-admin.payment-providers.view\"", authController, StringComparison.Ordinal);
        Assert.Contains("IPaymentProviderAdministrationService", program, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderAdministrationService", program, StringComparison.Ordinal);
    }

    [Fact]
    public void AdministrationContracts_MustRemainProviderIndependentAndSerializeCanonicalDtos()
    {
        var dtos = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/Admin/PaymentProviderAdministrationDtos.cs");
        var domain = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Administration/PaymentProviderAdministrationModels.cs");
        var permissions = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Administration/PaymentProviderPermissions.cs");

        Assert.Contains("PaymentProviderConfigurationDto", dtos, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderSummaryDto", dtos, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderCapabilityDto", dtos, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderConfigurationRequest", dtos, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderConfigurationResponse", dtos, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyDictionary<string, object?> Configuration", dtos, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiKey", dtos, StringComparison.Ordinal);
        Assert.DoesNotContain("IntegrationId", dtos, StringComparison.Ordinal);
        Assert.DoesNotContain("Hmac", dtos, StringComparison.Ordinal);

        Assert.Contains("PaymentProviderConfiguration", domain, StringComparison.Ordinal);
        Assert.Contains("ProviderCode", domain, StringComparison.Ordinal);
        Assert.Contains("DisplayName", domain, StringComparison.Ordinal);
        Assert.Contains("Capabilities", domain, StringComparison.Ordinal);
        Assert.Contains("Metadata", domain, StringComparison.Ordinal);

        Assert.Contains("PaymentProviders.View", permissions, StringComparison.Ordinal);
        Assert.Contains("PaymentProviders.Edit", permissions, StringComparison.Ordinal);
        Assert.Contains("PaymentProviders.Configure", permissions, StringComparison.Ordinal);
        Assert.Contains("PaymentProviders.TestConnection", permissions, StringComparison.Ordinal);
        Assert.Contains("PaymentProviders.RotateSecrets", permissions, StringComparison.Ordinal);

        var payload = new PaymentProviderConfigurationResponse
        {
            Provider = new PaymentProviderConfigurationDto
            {
                ProviderCode = "PAYMOB",
                DisplayName = "Paymob",
                Description = "Provider registration",
                Enabled = false,
                Environment = "Unconfigured",
                Status = "NotConfigured",
                Capabilities =
                [
                    new PaymentProviderCapabilityDto
                    {
                        Code = "checkout_sessions",
                        DisplayName = "Checkout Sessions",
                        Description = "Create sessions.",
                        Supported = true,
                        RequiresConfiguration = true
                    }
                ],
                Configuration = new Dictionary<string, object?>(StringComparer.Ordinal),
                Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
            },
            Permissions = PaymentProviderPermissions.All
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"providerCode\":\"PAYMOB\"", json, StringComparison.Ordinal);
        Assert.Contains("\"configuration\":{}", json, StringComparison.Ordinal);
        Assert.Contains("\"permissions\":[", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AdministrationService_MustValidateUnknownMissingDuplicateAndSecretInputsWithoutProviderCommunication()
    {
        var serviceSource = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/PaymentProviderAdministrationService.cs");
        var service = new PaymentProviderAdministrationService();

        Assert.Contains("DUPLICATE_PROVIDER", serviceSource, StringComparison.Ordinal);
        Assert.Contains("UNKNOWN_PROVIDER", serviceSource, StringComparison.Ordinal);
        Assert.Contains("MISSING_PROVIDER", serviceSource, StringComparison.Ordinal);
        Assert.Contains("PROVIDER_SECRET_NOT_ACCEPTED", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RotateSecretsAsync(", serviceSource, StringComparison.Ordinal);

        var providers = service.GetProviders();
        Assert.Single(providers);
        Assert.Equal("PAYMOB", providers[0].ProviderCode);

        var provider = service.GetProviderConfiguration("paymob");
        Assert.Equal("PAYMOB", provider.ProviderCode);
        Assert.Equal("Sandbox", provider.Environment);
        Assert.Contains("webhookUrl", provider.Configuration.Keys);

        var unknown = Assert.Throws<PaymentProviderAdministrationException>(() => service.GetProviderConfiguration("stripe"));
        Assert.Equal("UNKNOWN_PROVIDER", unknown.Code);

        var missing = Assert.Throws<PaymentProviderAdministrationException>(() => service.GetProviderConfiguration(" "));
        Assert.Equal("MISSING_PROVIDER", missing.Code);

        var secret = Assert.Throws<PaymentProviderAdministrationException>(() =>
            service.SaveProviderConfiguration(
                "PAYMOB",
                new PaymentProviderConfigurationRequest
                {
                    Configuration = new Dictionary<string, object?> { ["apiSecret"] = "never" }
                }));
        Assert.Equal("PROVIDER_SECRET_NOT_ACCEPTED", secret.Code);
    }

    [Fact]
    public void FrontendFoundation_MustRenderReusableDynamicSuperAdminProviderShell()
    {
        var app = CertificationSource.ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var layout = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/layout/SuperAdminLayout.tsx");
        var authTypes = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/auth/superAdminAuth.types.ts");
        var apiClient = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/core/superAdminApiClient.ts");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/paymentProvidersService.ts");
        var page = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/PaymentProvidersPage.tsx");
        var list = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/PaymentProviderList.tsx");
        var card = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/PaymentProviderCard.tsx");
        var details = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/PaymentProviderDetails.tsx");
        var configurationSection = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/ConfigurationSection.tsx");
        var state = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/usePaymentProviderAdministrationState.ts");

        Assert.Contains("PaymentProvidersPage", app, StringComparison.Ordinal);
        Assert.Contains("commercehub/payment-providers", app, StringComparison.Ordinal);
        Assert.Contains("super-admin.payment-providers.view", app, StringComparison.Ordinal);
        Assert.Contains("Payment Providers", layout, StringComparison.Ordinal);
        Assert.Contains("CommerceHub provider configuration", layout, StringComparison.Ordinal);
        Assert.Contains("'super-admin.payment-providers.view'", authTypes, StringComparison.Ordinal);

        Assert.Contains("SUPER_ADMIN_PAYMENT_PROVIDERS_ENDPOINT = '/payment-providers'", service, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/admin/payment-providers", service, StringComparison.Ordinal);
        Assert.Contains("canonicalPrefix: '/payment-providers'", apiClient, StringComparison.Ordinal);
        Assert.Contains("backendPrefix: '/api/admin/payment-providers'", apiClient, StringComparison.Ordinal);
        Assert.Contains("providers.forEach", service, StringComparison.Ordinal);
        Assert.Contains("duplicate providers", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Paymob", list, StringComparison.Ordinal);
        Assert.DoesNotContain("Paymob", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Paymob", details, StringComparison.Ordinal);

        Assert.Contains("PaymentProviderList", list, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderCard", card, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderDetails", details, StringComparison.Ordinal);
        Assert.Contains("PaymobConfigurationForm", configurationSection, StringComparison.Ordinal);
        Assert.Contains("secretReplacementDirty", configurationSection, StringComparison.Ordinal);
        Assert.Contains("This provider has not published a configuration form yet.", configurationSection, StringComparison.Ordinal);
        Assert.Contains("loading", state, StringComparison.Ordinal);
        Assert.Contains("saving", state, StringComparison.Ordinal);
        Assert.Contains("selectedProviderCode", state, StringComparison.Ordinal);
        Assert.Contains("providerList", state, StringComparison.Ordinal);
        Assert.Contains("configuration", state, StringComparison.Ordinal);
        Assert.Contains("validationErrors", state, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentationArtifact_MustRecordPaymentAdmin1Foundation()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/payment/PAYMENT-ADMIN-1_PAYMENT_PROVIDER_MANAGEMENT_FOUNDATION.md");

        foreach (var heading in new[]
                 {
                     "Executive Summary",
                     "Administrative Architecture",
                     "Navigation",
                     "Routing",
                     "DTO Design",
                     "Domain Model",
                     "API Design",
                     "Permission Model",
                     "Frontend Architecture",
                     "UI Components",
                     "Backend Validation",
                     "Testing",
                     "Files Added",
                     "Files Modified",
                     "Compatibility",
                     "Readiness for PAYMENT-ADMIN-2"
                 })
        {
            Assert.Contains(heading, document, StringComparison.Ordinal);
        }
    }
}
