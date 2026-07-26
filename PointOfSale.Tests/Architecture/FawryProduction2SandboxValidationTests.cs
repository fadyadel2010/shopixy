using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.Fawry;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryProduction2SandboxValidationTests
{
    [Fact]
    public void SandboxConfiguration_MustRequireEnvironmentSeparationTenantCredentialsAndOfficialUrls()
    {
        var options = CertificationSource.ReadRepoFile("PointOfSale.API/Configuration/FawryOptions.cs");
        var resolver = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryTenantConfigurationResolver.cs");

        Assert.Contains("SandboxApiBaseUri", options, StringComparison.Ordinal);
        Assert.Contains("https://atfawry.fawrystaging.com/", options, StringComparison.Ordinal);
        Assert.Contains("ProductionApiBaseUri", options, StringComparison.Ordinal);
        Assert.Contains("https://www.atfawry.com/", options, StringComparison.Ordinal);
        Assert.Contains("Fawry sandbox environment must set SandboxMode to true", options, StringComparison.Ordinal);
        Assert.Contains("Fawry production environment must set SandboxMode to false", options, StringComparison.Ordinal);
        Assert.Contains("Fawry callback URL must be an absolute server-owned URL", options, StringComparison.Ordinal);
        Assert.Contains("Fawry webhook URL must be an absolute server-owned URL", options, StringComparison.Ordinal);

        Assert.Contains("TenantPaymentMethods", resolver, StringComparison.Ordinal);
        Assert.Contains("pm.TenantId == tenantId", resolver, StringComparison.Ordinal);
        Assert.Contains("pm.Code == \"FAWRY\"", resolver, StringComparison.Ordinal);
        Assert.Contains("paymentMethod.IsSandbox != expectedSandbox", resolver, StringComparison.Ordinal);
        Assert.Contains("Global Fawry credentials are never returned or used as a tenant fallback", resolver, StringComparison.Ordinal);

        AssertInvalid(ValidOptions() with { Environment = FawryEnvironment.Sandbox, SandboxMode = false }, "SandboxMode");
        AssertInvalid(ValidOptions() with
        {
            Environment = FawryEnvironment.Production,
            SandboxMode = true,
            FeatureFlags = new FawryFeatureFlags { ProductionEnabled = true }
        }, "SandboxMode");
    }

    [Fact]
    public void CapabilityActivation_MustSeparateCertifiedSourceSupportFromActiveCommandSupport()
    {
        var options = CertificationSource.ReadRepoFile("PointOfSale.API/Configuration/FawryOptions.cs");
        var advancedAdapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryAdvancedOperationsAdapter.cs");
        var document = CertificationSource.ReadRepoFile("docs/architecture/FAWRY_PRODUCTION_2_SANDBOX_VALIDATION.md");

        foreach (var capability in new[]
        {
            "SupportsTransactionInquiry",
            "SupportsExpiration",
            "SupportsRefund",
            "SupportsPartialRefund",
            "SupportsVoidUnpaidOrder",
            "SupportsAuthorizeAndCapture"
        })
        {
            Assert.Contains(capability, options, StringComparison.Ordinal);
        }

        foreach (var operation in new[]
        {
            "FawryAdvancedOperation.Refund",
            "FawryAdvancedOperation.PartialRefund",
            "FawryAdvancedOperation.Void",
            "FawryAdvancedOperation.Capture",
            "FawryAdvancedOperation.TransactionInquiry"
        })
        {
            Assert.Contains(operation, advancedAdapter, StringComparison.Ordinal);
        }

        Assert.Contains("Advanced operations adapter certified translation-only", document, StringComparison.Ordinal);
        Assert.Contains("active command certification remains out of scope", document, StringComparison.Ordinal);
        Assert.Contains("Do NOT fabricate support", document, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayDuplicateRecovery_MustUseDurableReplayPriorEvidenceAndSingleWriterAdmission()
    {
        var integration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryWebhookRuntimeIntegrationService.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/FawryWebhookController.cs");

        Assert.Contains("FawryWebhookReplayRecords", integration, StringComparison.Ordinal);
        Assert.Contains("FawryWebhookPriorEvidenceLoader.LoadAsync", integration, StringComparison.Ordinal);
        Assert.Contains("PaymentWebhookLogs", integration, StringComparison.Ordinal);
        Assert.Contains("ProcessingStatus == WebhookProcessingStatus.Completed", integration, StringComparison.Ordinal);
        Assert.Contains("ProcessingStatus == WebhookProcessingStatus.Skipped", integration, StringComparison.Ordinal);
        Assert.Contains("_orchestrator.Execute", integration, StringComparison.Ordinal);
        Assert.Contains("FawryWebhookLifecycleAdmission.IsLifecyclePersistenceAllowed", controller, StringComparison.Ordinal);
        Assert.Contains("_paymentLifecycleService.HandlePaymentResultAsync", controller, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", controller, StringComparison.Ordinal);
        Assert.Contains("PersistReplayRecordAsync", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityCertification_MustRetainHistoricalExceptionAndRecordItsSeparateClosure()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/FawryWebhookController.cs");
        var runtimeDirectory = ReadRepoFiles("PointOfSale.Core/Payments/Runtime");
        var document = CertificationSource.ReadRepoFile("docs/architecture/FAWRY_PRODUCTION_2_SANDBOX_VALIDATION.md");
        var closure = CertificationSource.ReadRepoFile("docs/architecture/FAWRY_CERTIFICATION_CLOSURE_1.md");

        Assert.Contains("ProtectWebhookPayload(rawPayload)", controller, StringComparison.Ordinal);
        Assert.Contains("DigestMessageSignature(signature)", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("RawPayload = rawPayload", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("Signature = signature,", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("Fawry", runtimeDirectory, StringComparison.Ordinal);

        Assert.Contains("Architecture Exception", document, StringComparison.Ordinal);
        Assert.Contains("raw webhook payload", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("raw message signature", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No Runtime modification is required or authorized", document, StringComparison.Ordinal);
        Assert.Contains("HIGH FINDING CLOSED", closure, StringComparison.Ordinal);
        Assert.Contains("Runtime preserved", closure, StringComparison.Ordinal);
    }

    [Fact]
    public void TelemetryAndPerformance_MustBeSourceDocumentedWithoutLiveBenchmarkClaims()
    {
        var telemetry = CertificationSource.ReadRepoFile("PointOfSale.API/Instrumentation/FawryOperationalTelemetry.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        var executingPaths = string.Join(
            Environment.NewLine,
            CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/FawryWebhookController.cs"),
            CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs"),
            ReadRepoFiles("PointOfSale.API/Services/Payments/Fawry"));
        var document = CertificationSource.ReadRepoFile("docs/architecture/FAWRY_PRODUCTION_2_SANDBOX_VALIDATION.md");

        foreach (var metric in PointOfSale.API.Instrumentation.FawryOperationalTelemetry.RequiredMetricNames)
        {
            Assert.Contains(metric, telemetry, StringComparison.Ordinal);
        }

        Assert.Contains("FawryOperationalTelemetry.MeterName", program, StringComparison.Ordinal);
        Assert.Contains("tenant_bucket", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("FawryOperationalTelemetry", executingPaths, StringComparison.Ordinal);
        Assert.Contains("No benchmarks were executed", document, StringComparison.Ordinal);
        Assert.Contains("performance expectations are documented", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("executing-path counter calls are not yet wired", document, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CertificationArtifact_MustSeparateSourceDeploymentAndLiveSandboxDecisions()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/FAWRY_PRODUCTION_2_SANDBOX_VALIDATION.md");

        foreach (var section in new[]
        {
            "Validation Scope",
            "Source Certification",
            "Deployment Prerequisites",
            "Sandbox Prerequisites",
            "Security Review",
            "Operational Review",
            "Remaining External Evidence",
            "Certification Decision",
            "Success Answers"
        })
        {
            Assert.Contains(section, document, StringComparison.Ordinal);
        }

        Assert.Contains("Source Certified With Exceptions", document, StringComparison.Ordinal);
        Assert.Contains("Deployment Pending", document, StringComparison.Ordinal);
        Assert.Contains("Live Sandbox Not Certified", document, StringComparison.Ordinal);
        Assert.Contains("This is not a software failure for live-provider execution", document, StringComparison.Ordinal);
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
        MerchantCode = "fawry_merchant_sandbox",
        ApiKey = "fawry_api_key_sandbox",
        Secret = "fawry_secret_sandbox",
        Environment = FawryEnvironment.Sandbox,
        SandboxMode = true,
        FeatureFlags = new FawryFeatureFlags
        {
            Enabled = true,
            SandboxEnabled = true,
            PaymentExecutionEnabled = true
        },
        Capabilities = new FawryCapabilityModel
        {
            PaymentExecutionCertified = true,
            ProviderAdapterCertified = true
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
