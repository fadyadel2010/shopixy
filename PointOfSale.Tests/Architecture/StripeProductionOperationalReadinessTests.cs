using PointOfSale.API.Instrumentation;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.Stripe;

namespace PointOfSale.Tests.Architecture;

public sealed class StripeProductionOperationalReadinessTests
{
    [Fact]
    public void OperationalHealth_MustCoverRequiredStripeReadinessComponents()
    {
        var health = CertificationSource.ReadRepoFile("PointOfSale.API/HealthChecks/StripeOperationalReadinessHealthCheck.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        foreach (var component in new[]
        {
            "foundationHealth",
            "credentialHealth",
            "environmentHealth",
            "sdkHealth",
            "webhookEndpointHealth",
            "runtimeBridgeHealth",
            "applicationServiceHealth",
            "replayStoreHealth",
            "outcomeAdmissionHealth",
            "dependencyHealth"
        })
        {
            Assert.Contains(component, health, StringComparison.Ordinal);
        }

        Assert.Contains("stripe_operational_readiness", program, StringComparison.Ordinal);
        Assert.Contains("IsRegisteredOnce<IStripeRuntimeBridge>", health, StringComparison.Ordinal);
        Assert.Contains("IsRegisteredOnce<IStripeWebhookRuntimeIntegrationService>", health, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupValidation_MustFailClosedForRequiredStripeConfigurationModes()
    {
        AssertInvalid(new StripeOptions
        {
            Enabled = true,
            Environment = StripeEnvironment.Sandbox,
            SandboxMode = true
        }, "publishable key");

        AssertInvalid(new StripeOptions
        {
            Enabled = true,
            PublishableKey = "pk_live_readiness123456",
            SecretKey = "rk_live_readiness123456",
            WebhookSecret = "whsec_readiness123456",
            Environment = StripeEnvironment.Sandbox,
            SandboxMode = true
        }, "pk_test_");

        AssertInvalid(new StripeOptions
        {
            Enabled = true,
            PublishableKey = "pk_test_readiness123456",
            SecretKey = "rk_test_readiness123456",
            WebhookSecret = "not-a-webhook-secret",
            Environment = StripeEnvironment.Sandbox,
            SandboxMode = true
        }, "whsec_");

        AssertInvalid(new StripeOptions
        {
            Enabled = true,
            PublishableKey = "pk_test_readiness123456",
            SecretKey = "rk_test_readiness123456",
            WebhookSecret = "whsec_readiness123456",
            ApiVersion = "2026-05-27.dahlia",
            Environment = StripeEnvironment.Sandbox,
            SandboxMode = true
        }, StripeOptions.CertifiedApiVersion);

        AssertInvalid(new StripeOptions
        {
            Enabled = true,
            PublishableKey = "pk_live_readiness123456",
            SecretKey = "rk_live_readiness123456",
            WebhookSecret = "whsec_readiness123456",
            Environment = StripeEnvironment.Production,
            SandboxMode = false,
            FeatureFlags = new StripeFeatureFlags { ProductionEnabled = false }
        }, "production feature flag");
    }

    [Fact]
    public void StripeCreateIntentRegistrations_MustNotBeDuplicatedAcrossFoundationAndProgram()
    {
        var foundation = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeFoundationServiceCollectionExtensions.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.DoesNotContain("AddSingleton<IStripePaymentIntentClient", foundation, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSingleton<IStripePaymentIntentAdapter", foundation, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSingleton<IStripeRuntimeBridge", foundation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IStripePaymentIntentClient, TenantScopedStripePaymentIntentClient>", program, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IStripePaymentIntentAdapter, StripePaymentIntentAdapter>", program, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IStripeRuntimeBridge, StripeRuntimeBridge>", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Metrics_MustBeDefinedWiredAndEmittedWithoutRawTenantIdentifiers()
    {
        var telemetry = CertificationSource.ReadRepoFile("PointOfSale.API/Instrumentation/StripeOperationalTelemetry.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        foreach (var metric in StripeOperationalTelemetry.RequiredMetricNames)
        {
            Assert.Contains(metric, telemetry, StringComparison.Ordinal);
        }

        Assert.Contains("StripeOperationalTelemetry.MeterName", program, StringComparison.Ordinal);
        Assert.Contains("RecordPaymentCreated", service, StringComparison.Ordinal);
        Assert.Contains("RecordWebhookReceived", service, StringComparison.Ordinal);
        Assert.Contains("RecordWebhookDuplicate", service, StringComparison.Ordinal);
        Assert.Contains("RecordReplayPrevented", service, StringComparison.Ordinal);
        Assert.Contains("RecordPaymentSucceeded", service, StringComparison.Ordinal);
        Assert.Contains("RecordPaymentFailed", service, StringComparison.Ordinal);
        Assert.Contains("RecordRefundSuccess", service, StringComparison.Ordinal);
        Assert.Contains("RecordPaymentDuration", service, StringComparison.Ordinal);
        Assert.Contains("RecordProviderLatency", service, StringComparison.Ordinal);
        Assert.Contains("RecordWebhookProcessing", service, StringComparison.Ordinal);
        Assert.Contains("RecordWebhookReplay", service, StringComparison.Ordinal);
        Assert.Contains("RecordWebhookFailure", service, StringComparison.Ordinal);
        Assert.Contains("RecordProviderAvailability", service, StringComparison.Ordinal);
        Assert.Contains("RecordProviderRetry", service, StringComparison.Ordinal);
        Assert.Contains("RecordRefundLatency", service, StringComparison.Ordinal);
        Assert.Contains("tenant_bucket", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant_id", telemetry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SandboxObservabilityArtifacts_MustDefineDashboardsAndAlerts()
    {
        var dashboard = CertificationSource.ReadRepoFile("docs/observability/stripe/grafana-dashboard.json");
        var alerts = CertificationSource.ReadRepoFile("docs/observability/stripe/prometheus-alerts.yml");
        var certification = CertificationSource.ReadRepoFile("docs/architecture/STRIPE_SANDBOX_1_CERTIFICATION.md");

        foreach (var metric in StripeOperationalTelemetry.RequiredMetricNames)
        {
            Assert.Contains(metric, certification, StringComparison.Ordinal);
        }

        foreach (var token in new[]
        {
            "stripe_payment_duration_ms_bucket",
            "stripe_provider_latency_ms_bucket",
            "stripe_webhook_processing_ms_bucket",
            "stripe_webhook_replay_total",
            "stripe_webhook_failures_total",
            "stripe_provider_availability_total",
            "stripe_provider_retries_total",
            "stripe_refund_latency_ms_bucket"
        })
        {
            Assert.Contains(token, dashboard + alerts, StringComparison.Ordinal);
        }

        Assert.Contains("StripeProviderUnavailable", alerts, StringComparison.Ordinal);
        Assert.Contains("StripeWebhookFailuresHigh", alerts, StringComparison.Ordinal);
        Assert.Contains("StripeWebhookReplaySpike", alerts, StringComparison.Ordinal);
        Assert.Contains("Stripe Sandbox Operational Readiness", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public void LoggingGuards_MustNotLogStripeSecretsOrClientSecretValues()
    {
        var stripeSources = ReadStripeSources();
        var forbiddenStructuredTokens = new[]
        {
            "{ClientSecret}",
            "{SecretKey}",
            "{WebhookSecret}",
            "{ApiKey}",
            "{PublishableKey}"
        };

        foreach (var token in forbiddenStructuredTokens)
        {
            Assert.DoesNotContain(token, stripeSources, StringComparison.Ordinal);
        }

        Assert.Contains("ClientSecretEncrypted", stripeSources, StringComparison.Ordinal);
        Assert.Contains("Stripe webhook verified | EventId: {EventId}", stripeSources, StringComparison.Ordinal);
        Assert.Contains("EvidenceId = evidence.EvidenceId.Value", stripeSources, StringComparison.Ordinal);
        Assert.Contains("ProviderReference = evidence.PayloadReference.Value", stripeSources, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionReadinessDocument_MustIncludeMatricesRunbooksDrillsChecklistAndDecision()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/STRIPE_PRODUCTION_1_OPERATIONAL_READINESS.md");

        foreach (var section in new[]
        {
            "Operational Architecture",
            "Health Matrix",
            "Metrics Matrix",
            "Alert Matrix",
            "Runbooks",
            "Failure Drills",
            "Production Checklist",
            "Certification Decision"
        })
        {
            Assert.Contains(section, document, StringComparison.Ordinal);
        }

        Assert.Contains("No secrets", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No ClientSecret", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sandbox verified", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Production operational readiness certified", document, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertInvalid(StripeOptions options, string expectedError)
    {
        var result = StripeCredentialValidator.Validate(options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains(expectedError, StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadStripeSources()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var directory = Path.Combine(root, "PointOfSale.API", "Services", "Payments", "Stripe");
        var files = Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(new[]
            {
                Path.Combine(root, "PointOfSale.API", "Controllers", "StripeWebhooksController.cs"),
                Path.Combine(root, "PointOfSale.API", "Controllers", "StorefrontStripePaymentsController.cs")
            });

        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
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
