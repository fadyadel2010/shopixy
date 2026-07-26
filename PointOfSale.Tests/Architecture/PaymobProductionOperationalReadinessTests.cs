using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.Payments.Paymob;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobProductionOperationalReadinessTests
{
    [Fact]
    public void OperationalHealth_MustCoverRequiredPaymobReadinessComponents()
    {
        var health = CertificationSource.ReadRepoFile("PointOfSale.API/HealthChecks/PaymobOperationalReadinessHealthCheck.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        foreach (var component in new[]
        {
            "providerEnabledHealth",
            "environmentHealth",
            "credentialHealth",
            "merchantIdentifierHealth",
            "hmacSecretHealth",
            "callbackUrlHealth",
            "featureFlagHealth",
            "capabilityConsistencyHealth",
            "runtimeBridgeHealth",
            "applicationServiceHealth",
            "webhookEndpointHealth",
            "replayStoreHealth",
            "dependencyHealth"
        })
        {
            Assert.Contains(component, health, StringComparison.Ordinal);
        }

        Assert.Contains("paymob_operational_readiness", program, StringComparison.Ordinal);
        Assert.Contains("IsRegisteredOnce<IPaymobRuntimeBridge>", health, StringComparison.Ordinal);
        Assert.Contains("IsRegisteredOnce<IPaymobWebhookRuntimeIntegrationService>", health, StringComparison.Ordinal);
        Assert.Contains("PaymobWebhookReplayRecords", health, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupValidation_MustFailClosedForRequiredPaymobConfigurationModes()
    {
        AssertInvalid(new PaymobOptions
        {
            Enabled = true,
            Environment = PaymobEnvironment.Sandbox,
            SandboxMode = true
        }, "API key");

        AssertInvalid(ValidOptions() with
        {
            Environment = PaymobEnvironment.Sandbox,
            SandboxMode = false
        }, "SandboxMode");

        AssertInvalid(ValidOptions() with
        {
            Environment = PaymobEnvironment.Production,
            SandboxMode = true
        }, "SandboxMode");

        AssertInvalid(ValidOptions() with
        {
            Environment = PaymobEnvironment.Production,
            SandboxMode = false,
            FeatureFlags = new PaymobFeatureFlags { ProductionEnabled = false }
        }, "production execution");

        AssertInvalid(ValidOptions() with
        {
            HmacSecret = string.Empty
        }, "HMAC secret");

        AssertInvalid(ValidOptions() with
        {
            Callback = new PaymobCallbackConfiguration { Configured = true }
        }, "callback URL");
    }

    [Fact]
    public void Metrics_MustBeDefinedAndRegisteredWithoutRawTenantIdentifiers()
    {
        var telemetry = CertificationSource.ReadRepoFile("PointOfSale.API/Instrumentation/PaymobOperationalTelemetry.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        foreach (var metric in PaymobOperationalTelemetry.RequiredMetricNames)
        {
            Assert.Contains(metric, telemetry, StringComparison.Ordinal);
        }

        Assert.Contains("PointOfSale.Payments.Paymob", telemetry, StringComparison.Ordinal);
        Assert.Contains("PaymobOperationalTelemetry.MeterName", program, StringComparison.Ordinal);
        Assert.Contains("AddMeter(PointOfSale.API.Instrumentation.PaymobOperationalTelemetry.MeterName)", program, StringComparison.Ordinal);
        Assert.Contains("tenant_bucket", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant_id", telemetry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveFrozenRuntimeAndProviderExecutionBoundaries()
    {
        var runtimeDirectory = ReadRepoFiles("PointOfSale.Core/Payments/Runtime");
        var health = CertificationSource.ReadRepoFile("PointOfSale.API/HealthChecks/PaymobOperationalReadinessHealthCheck.cs");
        var telemetry = CertificationSource.ReadRepoFile("PointOfSale.API/Instrumentation/PaymobOperationalTelemetry.cs");

        Assert.DoesNotContain("Paymob", runtimeDirectory, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", health, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobOfficialApiClient", health, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobPaymentAdapter", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", telemetry, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionReadinessDocument_MustIncludeOperationalCertificationSections()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/PAYMOB_PRODUCTION_1_OPERATIONAL_READINESS.md");

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
            "Stripe Delta",
            "Certification Decision",
            "Success Answers"
        })
        {
            Assert.Contains(section, document, StringComparison.Ordinal);
        }

        Assert.Contains("PointOfSale.Payments.Paymob", document, StringComparison.Ordinal);
        Assert.Contains("Operational Readiness certified", document, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertInvalid(PaymobOptions options, string expectedError)
    {
        var result = PaymobCredentialValidator.Validate(options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains(expectedError, StringComparison.OrdinalIgnoreCase));
    }

    private static PaymobOptions ValidOptions() => new()
    {
        Enabled = true,
        ApiKey = "paymob_api_key_readiness",
        IntegrationId = "123456",
        IframeId = "654321",
        HmacSecret = "paymob_hmac_secret_readiness",
        Environment = PaymobEnvironment.Sandbox,
        SandboxMode = true,
        FeatureFlags = new PaymobFeatureFlags { SandboxEnabled = true },
        Callback = new PaymobCallbackConfiguration
        {
            Configured = true,
            CallbackUrl = new Uri("https://pos.example.com/api/paymob/callback"),
            WebhookUrl = new Uri("https://pos.example.com/api/paymob/webhook")
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
