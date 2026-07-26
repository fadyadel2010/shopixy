using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryProductionOperationalReadinessTests
{
    [Fact]
    public void OperationalHealth_MustCoverRequiredFawryReadinessComponents()
    {
        var health = CertificationSource.ReadRepoFile("PointOfSale.API/HealthChecks/FawryOperationalReadinessHealthCheck.cs");
        var contributor = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryOperationalReadinessHealthContributor.cs");
        var foundation = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryFoundationServiceCollectionExtensions.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        foreach (var component in new[]
        {
            "providerEnabledHealth",
            "environmentHealth",
            "credentialHealth",
            "merchantCodeHealth",
            "apiKeyHealth",
            "secretHealth",
            "callbackUrlHealth",
            "featureFlagHealth",
            "capabilityConsistencyHealth",
            "runtimeBridgeHealth",
            "applicationServiceHealth",
            "webhookEndpointHealth",
            "replayStoreHealth",
            "webhookAuditStoreHealth",
            "tenantConfigurationHealth",
            "dependencyHealth"
        })
        {
            Assert.Contains(component, health + contributor, StringComparison.Ordinal);
        }

        Assert.Contains("IFawryOperationalReadinessHealthContributor", contributor, StringComparison.Ordinal);
        Assert.Contains("FawryOperationalReadinessHealthCheck", health, StringComparison.Ordinal);
        Assert.Contains("fawry_operational_readiness", program, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IFawryOperationalReadinessHealthContributor", foundation, StringComparison.Ordinal);
        Assert.Contains("IsRegisteredOnce<IFawryRuntimeBridge>", health, StringComparison.Ordinal);
        Assert.Contains("IsRegisteredOnce<IFawryWebhookRuntimeIntegrationService>", health, StringComparison.Ordinal);
        Assert.Contains("FawryWebhookReplayRecords", health, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupValidation_MustFailClosedForRequiredFawryConfigurationModes()
    {
        AssertInvalid(new FawryOptions
        {
            Enabled = true,
            Environment = FawryEnvironment.Sandbox,
            SandboxMode = true
        }, "merchant code");

        AssertInvalid(ValidOptions() with
        {
            Environment = FawryEnvironment.Sandbox,
            SandboxMode = false
        }, "SandboxMode");

        AssertInvalid(ValidOptions() with
        {
            Environment = FawryEnvironment.Production,
            SandboxMode = true,
            FeatureFlags = new FawryFeatureFlags { ProductionEnabled = true }
        }, "SandboxMode");

        AssertInvalid(ValidOptions() with
        {
            Environment = FawryEnvironment.Production,
            SandboxMode = false,
            FeatureFlags = new FawryFeatureFlags { ProductionEnabled = false }
        }, "production execution");

        AssertInvalid(ValidOptions() with
        {
            Secret = string.Empty
        }, "secret");

        AssertInvalid(ValidOptions() with
        {
            Callback = new FawryCallbackConfiguration { Configured = true }
        }, "callback URL");
    }

    [Fact]
    public void Metrics_MustBeDefinedAndRegisteredWithoutSecretsOrRawTenantIdentifiers()
    {
        var telemetry = CertificationSource.ReadRepoFile("PointOfSale.API/Instrumentation/FawryOperationalTelemetry.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        foreach (var metric in FawryOperationalTelemetry.RequiredMetricNames)
        {
            Assert.Contains(metric, telemetry, StringComparison.Ordinal);
        }

        Assert.Contains("PointOfSale.Payments.Fawry", telemetry, StringComparison.Ordinal);
        Assert.Contains("FawryOperationalTelemetry.MeterName", program, StringComparison.Ordinal);
        Assert.Contains("AddMeter(PointOfSale.API.Instrumentation.FawryOperationalTelemetry.MeterName)", program, StringComparison.Ordinal);
        Assert.Contains("tenant_bucket", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant_id", telemetry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", telemetry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", telemetry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", telemetry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveFrozenRuntimeAndProviderExecutionBoundaries()
    {
        var runtimeDirectory = ReadRepoFiles("PointOfSale.Core/Payments/Runtime");
        var health = CertificationSource.ReadRepoFile("PointOfSale.API/HealthChecks/FawryOperationalReadinessHealthCheck.cs");
        var contributor = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryOperationalReadinessHealthContributor.cs");
        var telemetry = CertificationSource.ReadRepoFile("PointOfSale.API/Instrumentation/FawryOperationalTelemetry.cs");

        Assert.DoesNotContain("Fawry", runtimeDirectory, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", health, StringComparison.Ordinal);
        Assert.DoesNotContain("FawryPaymentAdapter(", health, StringComparison.Ordinal);
        Assert.DoesNotContain("Initiate", health + contributor, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleWebhook", health + contributor, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", telemetry, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionReadinessDocument_MustIncludeOperationalCertificationSections()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/FAWRY_PRODUCTION_1_OPERATIONAL_READINESS.md");

        foreach (var section in new[]
        {
            "Executive Summary",
            "Operational Architecture",
            "Health",
            "Telemetry",
            "Metrics",
            "Alerts",
            "Runbooks",
            "Failure Drills",
            "Security Review",
            "Production Checklist",
            "Certification Decision",
            "Success Answers"
        })
        {
            Assert.Contains(section, document, StringComparison.Ordinal);
        }

        Assert.Contains("PointOfSale.Payments.Fawry", document, StringComparison.Ordinal);
        Assert.Contains("fawry_operational_readiness", document, StringComparison.Ordinal);
        Assert.Contains("Operational readiness certified", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No Runtime", document, StringComparison.Ordinal);
        Assert.Contains("No live drills were executed", document, StringComparison.Ordinal);
    }

    private static void AssertInvalid(FawryOptions options, string expectedError)
    {
        var result = FawryCredentialValidator.Validate(options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains(expectedError, StringComparison.OrdinalIgnoreCase));
    }

    private static FawryOptions ValidOptions() => new()
    {
        Enabled = true,
        MerchantCode = "fawry_merchant_readiness",
        ApiKey = "fawry_api_key_readiness",
        Secret = "fawry_secret_readiness",
        Environment = FawryEnvironment.Sandbox,
        SandboxMode = true,
        FeatureFlags = new FawryFeatureFlags
        {
            Enabled = true,
            SandboxEnabled = true
        },
        Callback = new FawryCallbackConfiguration
        {
            Configured = true,
            CallbackUrl = new Uri("https://pos.example.com/api/webhooks/fawry/callback"),
            WebhookUrl = new Uri("https://pos.example.com/api/webhooks/fawry")
        }
    };

    private static string ReadRepoFiles(string relativeDirectory)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var directory = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
