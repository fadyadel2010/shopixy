using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobProductionSandboxValidationTests
{
    [Fact]
    public void SandboxValidationDocument_MustRecordEnvironmentMatrixEvidenceAndCertificationLimits()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/PAYMOB_PRODUCTION_2_SANDBOX_VALIDATION.md");

        foreach (var section in new[]
        {
            "Executive Summary",
            "Environment Validation",
            "Certification Separation",
            "Validation Matrix",
            "Observed Results",
            "Operational Findings",
            "Security Validation",
            "Performance Notes",
            "Outstanding Deployment Dependencies",
            "Stripe Delta",
            "Certification Decision",
            "Success Answers"
        })
        {
            Assert.Contains(section, document, StringComparison.Ordinal);
        }

        Assert.Contains("SANDBOX EXECUTION NOT CERTIFIED", document, StringComparison.Ordinal);
        Assert.Contains("Live Paymob Sandbox execution is **not certified**", document, StringComparison.Ordinal);
        Assert.Contains("Production Sandbox certified? | **No.", document, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationMatrix_MustCoverRequiredPaymobSandboxDrillsAndUnsupportedLiveEvidence()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/PAYMOB_PRODUCTION_2_SANDBOX_VALIDATION.md");

        foreach (var scenario in new[]
        {
            "Environment configuration",
            "Tenant configuration",
            "Credential validation",
            "Create payment",
            "Redirect / Iframe generation",
            "Successful payment",
            "Declined payment",
            "Cancelled payment",
            "Webhook delivery",
            "Callback delivery",
            "HMAC verification",
            "Replay rejection",
            "Duplicate rejection",
            "Background recovery",
            "Retry",
            "Refund",
            "Partial refund",
            "Void",
            "Capture",
            "Transaction inquiry",
            "Recovery",
            "Tenant isolation"
        })
        {
            Assert.Contains(scenario, document, StringComparison.Ordinal);
        }

        Assert.Contains("live Sandbox", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not executed", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Source Certification", document, StringComparison.Ordinal);
        Assert.Contains("Operational Readiness", document, StringComparison.Ordinal);
        Assert.Contains("Deployment Readiness", document, StringComparison.Ordinal);
        Assert.Contains("Live Sandbox Evidence", document, StringComparison.Ordinal);
        Assert.Contains("Live success must not be inferred from source code", document, StringComparison.Ordinal);
    }

    [Fact]
    public void SourcePipeline_MustKeepPaymobHmacAdmissionReplayAndLifecycleOrderingForWebhookValidation()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var integration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobWebhookRuntimeIntegrationService.cs");
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobWebhookEvidenceAdapter.cs");

        var hmacIndex = service.IndexOf("VerifyHmacSignature(canonicalHmacPayload", StringComparison.Ordinal);
        var admissionIndex = service.IndexOf("_paymobWebhookRuntimeIntegrationService.EvaluateAndExecuteAsync", StringComparison.Ordinal);
        var lifecycleIndex = service.IndexOf("_paymentLifecycleService.HandlePaymentResultAsync", admissionIndex, StringComparison.Ordinal);
        var refundIndex = service.IndexOf("_paymentLifecycleService.RecordRefundAsync", admissionIndex, StringComparison.Ordinal);
        var replayPersistIndex = service.IndexOf("_paymobWebhookRuntimeIntegrationService.PersistReplayRecordAsync", admissionIndex, StringComparison.Ordinal);

        Assert.True(hmacIndex >= 0);
        Assert.True(admissionIndex > hmacIndex);
        Assert.True(lifecycleIndex > admissionIndex);
        Assert.True(refundIndex > admissionIndex);
        Assert.True(replayPersistIndex > admissionIndex);

        Assert.Contains("PaymobWebhookHmacVerifier.Verify", integration, StringComparison.Ordinal);
        Assert.Contains("PaymobWebhookPriorEvidenceLoader.LoadAsync", integration, StringComparison.Ordinal);
        Assert.Contains("PaymobWebhookOutcomeAdmissionService.Evaluate", integration, StringComparison.Ordinal);
        Assert.Contains("replayContext.IsReplay || duplicate", integration, StringComparison.Ordinal);
        Assert.Contains("PAYMOB_WEBHOOK_REPLAY_REJECTED", integration, StringComparison.Ordinal);
        Assert.Contains("PAYMOB_WEBHOOK_DUPLICATE_REJECTED", integration, StringComparison.Ordinal);
        Assert.Contains("HMACSHA512", adapter, StringComparison.Ordinal);
        Assert.Contains("FixedTimeHexEquals", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentDefaults_MustRemainSandboxDisabledAndFailClosedUntilPaymobSecretsAreProvisioned()
    {
        var appsettings = CertificationSource.ReadRepoFile("PointOfSale.API/appsettings.json");
        var optionsSource = CertificationSource.ReadRepoFile("PointOfSale.API/Configuration/CommerceHubPaymobOptions.cs");

        Assert.Contains("\"Paymob\":", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"Enabled\": false", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"ApiKey\": \"\"", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"IntegrationId\": \"\"", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"IframeId\": \"\"", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"HmacSecret\": \"\"", appsettings, StringComparison.Ordinal);

        Assert.Contains("Environment { get; init; } = PaymobEnvironment.Sandbox", optionsSource, StringComparison.Ordinal);
        Assert.Contains("SandboxMode { get; init; } = true", optionsSource, StringComparison.Ordinal);
        Assert.Contains("SandboxEnabled { get; init; } = true", optionsSource, StringComparison.Ordinal);
        Assert.Contains("OfficialApiBaseUri", optionsSource, StringComparison.Ordinal);
        Assert.Contains("https://accept.paymob.com/api/", optionsSource, StringComparison.Ordinal);

        var validation = PaymobCredentialValidator.Validate(new PaymobOptions
        {
            Enabled = true,
            Environment = PaymobEnvironment.Sandbox,
            SandboxMode = true
        });

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("callback configuration", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SecurityAndObservability_MustExcludeSensitiveValuesAndExposeRequiredPaymobMetrics()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/PAYMOB_PRODUCTION_2_SANDBOX_VALIDATION.md");
        var telemetry = CertificationSource.ReadRepoFile("PointOfSale.API/Instrumentation/PaymobOperationalTelemetry.cs");
        var operationalReadiness = CertificationSource.ReadRepoFile("PointOfSale.API/HealthChecks/PaymobOperationalReadinessHealthCheck.cs");
        var paymobSources = string.Join(
            Environment.NewLine,
            new[]
            {
                "PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs",
                "PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs",
                "PointOfSale.API/Services/Payments/Paymob/PaymobWebhookRuntimeIntegrationService.cs",
                "PointOfSale.API/Services/Payments/Paymob/PaymobWebhookEvidenceAdapter.cs",
                "PointOfSale.API/Controllers/PaymobWebhooksController.cs"
            }.Select(CertificationSource.ReadRepoFile));

        foreach (var metric in PaymobOperationalTelemetry.RequiredMetricNames)
        {
            Assert.Contains(metric, telemetry, StringComparison.Ordinal);
        }

        Assert.Contains("Validated Paymob metric names", document, StringComparison.Ordinal);

        Assert.Contains("tenant_bucket", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant_id", telemetry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{ApiKey}", paymobSources, StringComparison.Ordinal);
        Assert.DoesNotContain("{HmacSecret}", paymobSources, StringComparison.Ordinal);
        Assert.DoesNotContain("{PaymentKey}", paymobSources, StringComparison.Ordinal);
        Assert.DoesNotContain("{IframeUrl}", paymobSources, StringComparison.Ordinal);
        Assert.Contains("PAYMOB-SECURITY-HOTFIX-1", document, StringComparison.Ordinal);
        Assert.Contains("All `.ai-context` files were reviewed", document, StringComparison.Ordinal);
        Assert.Contains("Create Payment certified?", document, StringComparison.Ordinal);
        Assert.Contains("Payment Key certified?", document, StringComparison.Ordinal);
        Assert.Contains("Iframe generation certified?", document, StringComparison.Ordinal);
        Assert.Contains("HMAC certified?", document, StringComparison.Ordinal);
        Assert.Contains("tenantConfigurationHealth", operationalReadiness, StringComparison.Ordinal);
        Assert.Contains("replayStoreHealth", operationalReadiness, StringComparison.Ordinal);
        Assert.Contains("webhookAuditStoreHealth", operationalReadiness, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveFrozenRuntimeAndRecordOnlyPaymobProtocolDelta()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/PAYMOB_PRODUCTION_2_SANDBOX_VALIDATION.md");
        var runtimeDirectory = ReadRepoFiles("PointOfSale.Core/Payments/Runtime");

        Assert.DoesNotContain("Paymob", runtimeDirectory, StringComparison.Ordinal);
        Assert.Contains("The architecture remains identical", document, StringComparison.Ordinal);
        Assert.Contains("API key authentication, Paymob order/payment key, redirect/iframe flow, callback, and webhook flow", document, StringComparison.Ordinal);
        Assert.Contains("Paymob HMAC-SHA512 verification", document, StringComparison.Ordinal);
    }

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
