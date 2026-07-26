using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.Payments.ManualPayment;

namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentProduction2SandboxValidationTests
{
    [Fact]
    public void SandboxConfiguration_MustValidateSourceEnvironmentIsolationAndFailClosedDefaults()
    {
        var appsettings = CertificationSource.ReadRepoFile("PointOfSale.API/appsettings.json");
        var options = CertificationSource.ReadRepoFile("PointOfSale.API/Configuration/ManualPaymentOptions.cs");
        var application = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");

        Assert.Contains("\"ManualPayment\":", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"Enabled\": false", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"Environment\": \"Sandbox\"", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"SandboxMode\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"SandboxEnabled\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("\"ProductionEnabled\": false", appsettings, StringComparison.Ordinal);

        Assert.Contains("Manual Payment sandbox environment must set SandboxMode to true", options, StringComparison.Ordinal);
        Assert.Contains("Manual Payment production environment must set SandboxMode to false", options, StringComparison.Ordinal);
        Assert.Contains("Manual Payment sandbox execution must be explicitly enabled by the sandbox feature flag", options, StringComparison.Ordinal);
        Assert.Contains("Manual Payment production execution must be explicitly enabled by the production feature flag", options, StringComparison.Ordinal);
        Assert.Contains("paymentMethod.IsSandbox != expectedSandbox", application, StringComparison.Ordinal);
        Assert.Contains("paymentMethod.IsSandbox != options.SandboxMode", application, StringComparison.Ordinal);

        AssertValid(ValidSandboxOptions());
        AssertInvalid(ValidSandboxOptions() with { SandboxMode = false }, "SandboxMode");
        AssertInvalid(ValidSandboxOptions() with
        {
            Environment = ManualPaymentEnvironment.Production,
            SandboxMode = true,
            FeatureFlags = new ManualPaymentFeatureFlags { Enabled = true, ProductionEnabled = true }
        }, "SandboxMode");
        AssertInvalid(ValidSandboxOptions() with
        {
            FeatureFlags = new ManualPaymentFeatureFlags { Enabled = true, SandboxEnabled = false }
        }, "sandbox feature flag");
    }

    [Fact]
    public void ConfigurationPolicy_MustCoverInstructionsChannelsReferenceProofExpirationAndCapabilities()
    {
        var options = CertificationSource.ReadRepoFile("PointOfSale.API/Configuration/ManualPaymentOptions.cs");
        var application = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");
        var advanced = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentAdvancedOperationsAdapter.cs");

        foreach (var required in new[]
        {
            "SupportsTenantScopedConfiguration",
            "SupportsInstructionConfiguration",
            "SupportsReceiptProofUpload",
            "SupportsAcceptedProofTypes",
            "SupportsExpirationPolicy",
            "ProviderAdapterCertified",
            "RuntimeBridgeCertified",
            "EvidenceAdapterCertified",
            "RequireReferenceNumber",
            "RequireCustomerProof",
            "AcceptedContentTypes",
            "MaximumUploadBytes",
            "PendingPaymentTtl"
        })
        {
            Assert.Contains(required, options + application, StringComparison.Ordinal);
        }

        foreach (var channel in new[]
        {
            "BankTransfer",
            "VodafoneCash",
            "Instapay",
            "WalletTransfer",
            "CashDeposit",
            "CustomInstructions",
            "OfflineInvoice"
        })
        {
            Assert.Contains(channel, options, StringComparison.Ordinal);
        }

        Assert.Contains("ManualPaymentOperationalCapability.ReferencePaymentInitialization", application, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentOperationalCapability.EvidenceAdmission", application, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentAdvancedOperation.CancelPendingPayment", advanced, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentAdvancedOperation.ExpirePendingPayment", advanced, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentAdvancedOperation.PaymentStatusInquiry", advanced, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentAdvancedOperation.EvidenceStatusInquiry", advanced, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentAdvancedOperation.ApprovalExecution", advanced, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentAdvancedOperation.ReviewExecution", advanced, StringComparison.Ordinal);
        Assert.Contains("Unsupported", advanced, StringComparison.Ordinal);
        Assert.Contains("translation_only", advanced, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceFlow_MustRemainEvidenceOnlyAndValidateMetadataBeforeRuntimeDelegation()
    {
        var application = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");
        var evidenceAdapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentEvidenceAdapter.cs");
        var bridge = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs");

        Assert.Contains("ResolveAsync(", application, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentOperationalCapability.EvidenceAdmission", application, StringComparison.Ordinal);
        Assert.Contains("_evidenceAdapter.Translate", application, StringComparison.Ordinal);
        Assert.Contains("_runtimeBridge.ExecuteEvidenceAsync", application, StringComparison.Ordinal);
        Assert.Contains("ProofIdentifier", application + evidenceAdapter, StringComparison.Ordinal);
        Assert.Contains("ProofMetadata", application + evidenceAdapter, StringComparison.Ordinal);
        Assert.Contains("ReferenceNumberRequired", application + evidenceAdapter, StringComparison.Ordinal);
        Assert.Contains("MANUAL_PAYMENT_EVIDENCE_PROOF_MISSING", application, StringComparison.Ordinal);
        Assert.Contains("MANUAL_PAYMENT_EVIDENCE_REFERENCE_MISSING", application, StringComparison.Ordinal);
        Assert.Contains("PendingReview", evidenceAdapter, StringComparison.Ordinal);
        Assert.Contains("ManualReview", evidenceAdapter, StringComparison.Ordinal);

        Assert.DoesNotContain("PaymentLifecycleService", evidenceAdapter + bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", evidenceAdapter + bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", evidenceAdapter + bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ApproveAsync", evidenceAdapter + bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("RejectAsync", evidenceAdapter + bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityBoundaries_MustKeepHealthTelemetryAndRuntimeFreeOfSensitiveManualArtifacts()
    {
        var health = CertificationSource.ReadRepoFile("PointOfSale.API/HealthChecks/ManualPaymentOperationalReadinessHealthCheck.cs");
        var telemetry = CertificationSource.ReadRepoFile("PointOfSale.API/Instrumentation/ManualPaymentOperationalTelemetry.cs");
        var readiness = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentOperationalReadiness.cs");
        var runtime = ReadRepoFiles("PointOfSale.Core/Payments/Runtime");

        foreach (var metric in ManualPaymentOperationalTelemetry.RequiredMetricNames)
        {
            Assert.Contains(metric, telemetry, StringComparison.Ordinal);
        }

        Assert.Contains("tenant_bucket", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant_id", telemetry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DefaultInstructions", health, StringComparison.Ordinal);
        Assert.DoesNotContain("account", health + telemetry + readiness, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wallet", health + telemetry + readiness, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("proof content", health + telemetry + readiness, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", health + telemetry + readiness, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", telemetry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RuntimeResult", health + telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPayment", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualWallet", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveRuntimeAndExcludeDeploymentWorkflowSqlChanges()
    {
        var manualSources = ReadRepoFiles("PointOfSale.API/Services/Payments/ManualPayment");
        var migrations = ReadRepoFiles("PointOfSale.Infrastructure/Migrations");
        var runtime = ReadRepoFiles("PointOfSale.Core/Payments/Runtime");
        var document = CertificationSource.ReadRepoFile("docs/architecture/MANUAL_PRODUCTION_2_SANDBOX_VALIDATION.md");

        Assert.DoesNotContain("ManualPayment", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlCommand", manualSources, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", manualSources, StringComparison.Ordinal);
        Assert.DoesNotContain("MigrationBuilder", manualSources, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPayment", migrations, StringComparison.Ordinal);
        Assert.Contains("NO Runtime changes", document, StringComparison.Ordinal);
        Assert.Contains("NO SQL", document, StringComparison.Ordinal);
        Assert.Contains("NO approval workflow", document, StringComparison.Ordinal);
        Assert.Contains("NO review workflow", document, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifact_MustSeparateSourceDeploymentEnvironmentLiveSandboxAndProductionDecisions()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/MANUAL_PRODUCTION_2_SANDBOX_VALIDATION.md");

        foreach (var section in new[]
        {
            "Executive Summary",
            "Sandbox Scope",
            "Environment Validation",
            "Security Review",
            "Operational Review",
            "Deployment Classification",
            "Regression",
            "Certification Decision",
            "Architecture Exception"
        })
        {
            Assert.Contains(section, document, StringComparison.Ordinal);
        }

        Assert.Contains("SOURCE CERTIFIED", document, StringComparison.Ordinal);
        Assert.Contains("DEPLOYMENT PENDING", document, StringComparison.Ordinal);
        Assert.Contains("LIVE SANDBOX NOT CERTIFIED", document, StringComparison.Ordinal);
        Assert.Contains("Live Production readiness | Not certified", document, StringComparison.Ordinal);
        Assert.Contains("Manual Payment source readiness for Sandbox execution is certified", document, StringComparison.Ordinal);
        Assert.Contains("Live success must not be inferred from source code", document, StringComparison.Ordinal);
        Assert.Contains("Recommend MANUAL-FINAL-RECERTIFICATION", document, StringComparison.Ordinal);
    }

    private static void AssertValid(ManualPaymentOptions options)
    {
        var result = ManualPaymentConfigurationValidator.Validate(options);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    private static void AssertInvalid(ManualPaymentOptions options, string expectedError)
    {
        var result = ManualPaymentConfigurationValidator.Validate(options);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains(expectedError, StringComparison.OrdinalIgnoreCase));
    }

    private static ManualPaymentOptions ValidSandboxOptions() => new()
    {
        Enabled = true,
        Environment = ManualPaymentEnvironment.Sandbox,
        SandboxMode = true,
        FeatureFlags = new ManualPaymentFeatureFlags
        {
            Enabled = true,
            SandboxEnabled = true
        },
        Capabilities = ManualPaymentCapabilityModel.FoundationOnly with
        {
            ProviderAdapterCertified = true,
            RuntimeBridgeCertified = true,
            EvidenceAdapterCertified = true
        },
        Instructions = new ManualPaymentInstructionConfiguration
        {
            Configured = true,
            DefaultInstructions = "Use the exact payment reference in your transfer.",
            RequireReferenceNumber = true,
            RequireCustomerProof = true
        },
        Proof = new ManualPaymentProofConfiguration
        {
            AcceptedContentTypes = new[] { "image/jpeg", "image/png", "application/pdf" },
            MaximumUploadBytes = 5 * 1024 * 1024
        },
        Expiration = new ManualPaymentExpirationPolicy
        {
            Configured = true,
            PendingPaymentTtl = TimeSpan.FromDays(2),
            ProofAccessTtl = TimeSpan.FromDays(7)
        },
        Channels = new[]
        {
            new ManualPaymentChannelConfiguration
            {
                Channel = ManualPaymentChannel.BankTransfer,
                Enabled = true,
                DisplayName = "Bank Transfer",
                Instructions = "Transfer to the configured account and upload proof."
            }
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
