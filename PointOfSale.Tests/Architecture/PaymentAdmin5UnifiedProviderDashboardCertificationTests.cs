using System.Text.Json;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Payments.Administration;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentAdmin5UnifiedProviderDashboardCertificationTests
{
    [Fact]
    public void DashboardAggregation_ProducesProviderSummaryHealthCapabilitiesSecretsPermissionsAndActivity()
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
        secrets.RotateSecret(
            "PAYMOB",
            new RotatePaymentProviderSecretRequest
            {
                SecretType = "API Key",
                NewSecret = "paymob-next-api-key",
                ConfirmNewSecret = "paymob-next-api-key",
                AdministratorConfirmation = true
            },
            Guid.NewGuid(),
            "security@example.com");
        administration.RecordProviderActionResult(
            "PAYMOB",
            new PaymentProviderActionResultDto
            {
                Success = true,
                Status = "Healthy",
                Provider = "PAYMOB",
                Environment = "Production",
                Message = "Provider authentication and profile verification succeeded.",
                CheckedAtUtc = DateTime.UtcNow,
                LatencyMs = 40,
                ProviderVersion = "Paymob Accept API"
            });

        var dashboard = administration.GetDashboard(
        [
            new PaymentProviderAdministrationActivityDto
            {
                ActivityId = "1",
                ProviderCode = "PAYMOB",
                Action = "PAYMENT_PROVIDER_CONFIGURATION_SAVED",
                DisplayName = "Configuration Updated",
                Description = "Configuration Updated",
                OccurredAtUtc = DateTime.UtcNow,
                Success = true
            }
        ]);

        var provider = Assert.Single(dashboard.Providers);
        Assert.Equal("PAYMOB", provider.ProviderCode);
        Assert.Equal("Production", provider.Environment);
        Assert.True(provider.Enabled);
        Assert.Equal("Healthy", provider.Health.Status);
        Assert.NotNull(provider.LastCheckUtc);
        Assert.Contains(provider.Secrets, secret => secret.SecretType == "API Key" && secret.Version == 2 && secret.Status == "Active");
        Assert.Contains(provider.Capabilities, capability => capability.Code == "payments");
        Assert.Contains(provider.Capabilities, capability => capability.Code == "webhooks");
        Assert.Contains(PaymentProviderPermissions.ViewSecretMetadata, provider.Permissions);
        Assert.Equal(1, dashboard.Summary.ProviderCount);
        Assert.True(dashboard.Summary.CapabilityCount >= 10);
        Assert.NotEmpty(dashboard.Health);
        Assert.NotEmpty(dashboard.Secrets);
        Assert.Single(dashboard.RecentActivity);
    }

    [Fact]
    public void DashboardDtoSerialization_RemainsGenericAndDoesNotExposeSecretsOrProviderPayloads()
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

        var json = JsonSerializer.Serialize(
            administration.GetDashboard(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"providers\":[", json, StringComparison.Ordinal);
        Assert.Contains("\"summary\":", json, StringComparison.Ordinal);
        Assert.Contains("\"capabilities\":[", json, StringComparison.Ordinal);
        Assert.Contains("\"recentActivity\":[", json, StringComparison.Ordinal);
        Assert.DoesNotContain("paymob-initial-api-key", json, StringComparison.Ordinal);
        Assert.DoesNotContain("paymob-initial-hmac-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("encryptedSecret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerPayload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paymobProvider", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendArchitecture_UsesReadOnlyDashboardRouteAuditTimelineAndBatchedProviderReads()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/AdminPaymentProvidersController.cs");
        var dashboardService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/PaymentProviderDashboardService.cs");
        var administrationService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/PaymentProviderAdministrationService.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("[HttpGet(\"dashboard\")]", controller, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_PROVIDER_DASHBOARD_VIEWED", controller, StringComparison.Ordinal);
        Assert.Contains("RawAuditPayloadsReturned = false", controller, StringComparison.Ordinal);
        Assert.Contains("ProviderPayloadsReturned = false", controller, StringComparison.Ordinal);
        Assert.Contains("IPaymentProviderDashboardService", program, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderDashboardService", program, StringComparison.Ordinal);
        Assert.Contains("AuditLogs", dashboardService, StringComparison.Ordinal);
        Assert.Contains("DashboardAuditActions.Contains(audit.ActionType)", dashboardService, StringComparison.Ordinal);
        Assert.Contains("Take(25)", dashboardService, StringComparison.Ordinal);
        Assert.Contains("LoadProviderStates()", administrationService, StringComparison.Ordinal);
        Assert.Contains("settingKeys.Contains(item.Key)", administrationService, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking()", administrationService, StringComparison.Ordinal);

        foreach (var forbidden in new[]
                 {
                     "TestConnectionAsync(",
                     "RegisterOrderAsync",
                     "CreatePaymentKeyAsync",
                     "RefundAsync",
                     "CheckoutSession",
                     "Publish"
                 })
        {
            Assert.DoesNotContain(forbidden, dashboardService, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FrontendDashboard_RendersReusableSectionsFilteringSearchingRefreshSelectionAndPermissions()
    {
        var page = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/PaymentProvidersPage.tsx");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/paymentProvidersService.ts");
        var types = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/paymentProviderTypes.ts");
        var dashboard = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/ProviderDashboard.tsx");
        var filters = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/DashboardFilters.tsx");
        var filterAuthority = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/providerDashboardFilters.ts");
        var card = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/ProviderSummaryCard.tsx");
        var capabilityGrid = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/CapabilityGrid.tsx");
        var timeline = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/AdministrationTimeline.tsx");
        var overview = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/ProviderStatusOverview.tsx");
        var secretCard = CertificationSource.ReadRepoFile("PointOfSale.Web/src/super-admin/payment-providers/components/SecretCard.tsx");
        var frontend = page + service + types + dashboard + filters + filterAuthority + card + capabilityGrid + timeline + overview + secretCard;

        foreach (var component in new[]
                 {
                     "ProviderDashboard",
                     "ProviderSummaryCard",
                     "CapabilityGrid",
                     "AdministrationTimeline",
                     "ProviderStatusOverview",
                     "DashboardFilters",
                     "ProviderHealthCard",
                     "SecretCard",
                     "ConfigurationSection",
                     "ProviderBadge"
                 })
        {
            Assert.Contains(component, frontend, StringComparison.Ordinal);
        }

        Assert.Contains("/dashboard", service, StringComparison.Ordinal);
        Assert.Contains("getPaymentProviderDashboard", page, StringComparison.Ordinal);
        Assert.Contains("dashboardQuery.refetch()", page, StringComparison.Ordinal);
        Assert.DoesNotContain("testConnectionMutation.mutate()", page.Substring(page.IndexOf("onRefresh={()", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("filterDashboardProviders", filterAuthority, StringComparison.Ordinal);
        Assert.Contains("filters.search", filterAuthority, StringComparison.Ordinal);
        Assert.Contains("filters.capability", filterAuthority, StringComparison.Ordinal);
        Assert.Contains("selectedProviderCode", dashboard, StringComparison.Ordinal);
        Assert.Contains("expandedProviderCodes", dashboard, StringComparison.Ordinal);
        Assert.Contains("canRotate={false}", dashboard, StringComparison.Ordinal);
        Assert.Contains("canRotate && onRotate", secretCard, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentationArtifactExistsWithRequiredSections()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/payment/PAYMENT-ADMIN-5_UNIFIED_PROVIDER_DASHBOARD_AND_FINAL_CERTIFICATION.md");

        foreach (var heading in new[]
                 {
                     "Executive Summary",
                     "Administration Architecture",
                     "Dashboard Architecture",
                     "Capability Model",
                     "Dashboard API",
                     "DTO Design",
                     "Frontend Design",
                     "Backend Design",
                     "Security",
                     "Performance",
                     "Testing",
                     "Certification",
                     "Files Added",
                     "Files Modified",
                     "Final Architecture Decision"
                 })
        {
            Assert.Contains(heading, document, StringComparison.Ordinal);
        }
    }

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
