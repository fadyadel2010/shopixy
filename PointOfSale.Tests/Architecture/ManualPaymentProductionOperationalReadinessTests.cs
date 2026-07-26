using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.Payments.ManualPayment;

namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentProductionOperationalReadinessTests
{
    [Fact]
    public void OperationalHealth_MustCoverRequiredManualPaymentReadinessComponents()
    {
        var health = CertificationSource.ReadRepoFile("PointOfSale.API/HealthChecks/ManualPaymentOperationalReadinessHealthCheck.cs");
        var readiness = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentOperationalReadiness.cs");
        var foundation = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentFoundationServiceCollectionExtensions.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        foreach (var component in new[]
        {
            "providerEnabledHealth",
            "configurationValidHealth",
            "supportedChannelsHealth",
            "instructionConfigurationHealth",
            "expirationPolicyHealth",
            "proofConfigurationHealth",
            "environmentConsistencyHealth",
            "secretExposureHealth",
            "runtimeBridgeHealth",
            "applicationServiceHealth",
            "evidenceApplicationServiceHealth",
            "providerAdapterHealth",
            "evidenceAdapterHealth",
            "tenantConfigurationResolverHealth",
            "alertDefinitionHealth",
            "telemetryDefinitionHealth"
        })
        {
            Assert.Contains(component, health + readiness, StringComparison.Ordinal);
        }

        Assert.Contains("IManualPaymentOperationalReadinessHealthContributor", readiness, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentOperationalReadinessHealthCheck", health, StringComparison.Ordinal);
        Assert.Contains("manual_payment_operational_readiness", program, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IManualPaymentOperationalReadinessHealthContributor", foundation, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", health + readiness, StringComparison.Ordinal);
        Assert.DoesNotContain("CanConnectAsync", health + readiness, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthCheck_MustReturnSafeSourceLevelReadinessData()
    {
        var services = new ServiceCollection();
        services.AddManualPaymentFoundation(BuildConfiguration());

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var healthCheck = new PointOfSale.API.HealthChecks.ManualPaymentOperationalReadinessHealthCheck(
            provider.GetRequiredService<IManualPaymentOperationalReadinessHealthContributor>(),
            provider.GetRequiredService<IServiceScopeFactory>());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Manual Payment operational readiness checks are green.", result.Description);
        Assert.True((bool)result.Data["providerEnabledHealth"]);
        Assert.True((bool)result.Data["runtimeBridgeHealth"]);
        Assert.Equal("manual_payment", result.Data["providerCode"]);
        Assert.False((bool)result.Data["externalNetworkRequired"]);
        Assert.DoesNotContain("DefaultInstructions", string.Join(',', result.Data.Keys), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartupValidation_MustFailClosedForRequiredManualPaymentConfigurationModes()
    {
        AssertInvalid(new ManualPaymentOptions
        {
            Enabled = true,
            FeatureFlags = new ManualPaymentFeatureFlags { Enabled = true, SandboxEnabled = true }
        }, "at least one enabled");

        AssertInvalid(ValidOptions() with
        {
            Environment = ManualPaymentEnvironment.Sandbox,
            SandboxMode = false
        }, "SandboxMode");

        AssertInvalid(ValidOptions() with
        {
            Environment = ManualPaymentEnvironment.Production,
            SandboxMode = true,
            FeatureFlags = new ManualPaymentFeatureFlags { Enabled = true, ProductionEnabled = true }
        }, "SandboxMode");

        AssertInvalid(ValidOptions() with
        {
            Environment = ManualPaymentEnvironment.Production,
            SandboxMode = false,
            FeatureFlags = new ManualPaymentFeatureFlags { Enabled = true, ProductionEnabled = false }
        }, "production feature flag");

        AssertInvalid(ValidOptions() with
        {
            Proof = new ManualPaymentProofConfiguration
            {
                AcceptedContentTypes = Array.Empty<string>(),
                MaximumUploadBytes = 5 * 1024 * 1024
            }
        }, "accepted proof content type");
    }

    [Fact]
    public void MetricsAndTelemetry_MustBeDefinedAndRegisteredWithoutSecretsOrRawTenantIdentifiers()
    {
        var telemetry = CertificationSource.ReadRepoFile("PointOfSale.API/Instrumentation/ManualPaymentOperationalTelemetry.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        foreach (var metric in ManualPaymentOperationalTelemetry.RequiredMetricNames)
        {
            Assert.Contains(metric, telemetry, StringComparison.Ordinal);
        }

        foreach (var eventName in ManualPaymentOperationalTelemetry.RequiredEventNames)
        {
            Assert.Contains(eventName, telemetry, StringComparison.Ordinal);
        }

        Assert.Contains("PointOfSale.Payments.ManualPayment", telemetry, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentOperationalTelemetry.MeterName", program, StringComparison.Ordinal);
        Assert.Contains("AddMeter(PointOfSale.API.Instrumentation.ManualPaymentOperationalTelemetry.MeterName)", program, StringComparison.Ordinal);
        Assert.Contains("tenant_bucket", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant_id", telemetry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", telemetry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", telemetry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("proof content", telemetry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AlertDefinitions_MustCoverManualPaymentOperationalFailures()
    {
        var names = ManualPaymentOperationalAlerts.Definitions.Select(alert => alert.Name).ToArray();

        Assert.Contains("ManualPayment.ConfigurationInvalid", names);
        Assert.Contains("ManualPayment.ProviderDisabled", names);
        Assert.Contains("ManualPayment.EnvironmentMismatch", names);
        Assert.Contains("ManualPayment.RepeatedRuntimeRejection", names);
        Assert.Contains("ManualPayment.EvidenceAdmissionFailures", names);
        Assert.Contains("ManualPayment.HighConfigurationFailures", names);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveRuntimeAndExcludeWorkflowDeploymentPersistence()
    {
        var runtimeDirectory = ReadRepoFiles("PointOfSale.Core/Payments/Runtime");
        var health = CertificationSource.ReadRepoFile("PointOfSale.API/HealthChecks/ManualPaymentOperationalReadinessHealthCheck.cs");
        var readiness = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentOperationalReadiness.cs");
        var telemetry = CertificationSource.ReadRepoFile("PointOfSale.API/Instrumentation/ManualPaymentOperationalTelemetry.cs");

        Assert.DoesNotContain("ManualPayment", runtimeDirectory, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualWallet", runtimeDirectory, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", health + readiness + telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", health + readiness + telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("Migration", health + readiness + telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("ControllerBase", health + readiness + telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("ApproveAsync", health + readiness + telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("RejectAsync", health + readiness + telemetry, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionReadinessDocument_MustIncludeOperationalCertificationSections()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/MANUAL_PRODUCTION_1_OPERATIONAL_READINESS.md");

        foreach (var section in new[]
        {
            "Executive Summary",
            "Operational Readiness",
            "Health",
            "Telemetry",
            "Metrics",
            "Alerts",
            "Runbooks",
            "Security Review",
            "Production Checklist",
            "Regression",
            "Certification Decision"
        })
        {
            Assert.Contains(section, document, StringComparison.Ordinal);
        }

        Assert.Contains("PointOfSale.Payments.ManualPayment", document, StringComparison.Ordinal);
        Assert.Contains("manual_payment_operational_readiness", document, StringComparison.Ordinal);
        Assert.Contains("Operational readiness certified", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No Runtime", document, StringComparison.Ordinal);
        Assert.Contains("No live drills were executed", document, StringComparison.Ordinal);
    }

    private static void AssertInvalid(ManualPaymentOptions options, string expectedError)
    {
        var result = ManualPaymentOperationalReadinessValidator.ValidateStartup(options);

        Assert.NotEmpty(result);
        Assert.Contains(result, error => error.Contains(expectedError, StringComparison.OrdinalIgnoreCase));
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(ValidConfiguration())
            .Build();

    private static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["CommerceHub:Payments:ManualPayment:Enabled"] = "true",
        ["CommerceHub:Payments:ManualPayment:Environment"] = "Sandbox",
        ["CommerceHub:Payments:ManualPayment:SandboxMode"] = "true",
        ["CommerceHub:Payments:ManualPayment:FeatureFlags:Enabled"] = "true",
        ["CommerceHub:Payments:ManualPayment:FeatureFlags:SandboxEnabled"] = "true",
        ["CommerceHub:Payments:ManualPayment:Capabilities:ProviderAdapterCertified"] = "true",
        ["CommerceHub:Payments:ManualPayment:Capabilities:RuntimeBridgeCertified"] = "true",
        ["CommerceHub:Payments:ManualPayment:Capabilities:EvidenceAdapterCertified"] = "true",
        ["CommerceHub:Payments:ManualPayment:Instructions:Configured"] = "true",
        ["CommerceHub:Payments:ManualPayment:Instructions:DefaultInstructions"] = "Use the exact payment reference in your transfer.",
        ["CommerceHub:Payments:ManualPayment:Instructions:RequireReferenceNumber"] = "true",
        ["CommerceHub:Payments:ManualPayment:Instructions:RequireCustomerProof"] = "true",
        ["CommerceHub:Payments:ManualPayment:Proof:AcceptedContentTypes:0"] = "image/jpeg",
        ["CommerceHub:Payments:ManualPayment:Proof:AcceptedContentTypes:1"] = "image/png",
        ["CommerceHub:Payments:ManualPayment:Proof:AcceptedContentTypes:2"] = "application/pdf",
        ["CommerceHub:Payments:ManualPayment:Proof:MaximumUploadBytes"] = "5242880",
        ["CommerceHub:Payments:ManualPayment:Expiration:Configured"] = "true",
        ["CommerceHub:Payments:ManualPayment:Expiration:PendingPaymentTtl"] = "2.00:00:00",
        ["CommerceHub:Payments:ManualPayment:Expiration:ProofAccessTtl"] = "7.00:00:00",
        ["CommerceHub:Payments:ManualPayment:Channels:0:Channel"] = "VodafoneCash",
        ["CommerceHub:Payments:ManualPayment:Channels:0:Enabled"] = "true",
        ["CommerceHub:Payments:ManualPayment:Channels:0:DisplayName"] = "Vodafone Cash",
        ["CommerceHub:Payments:ManualPayment:Channels:0:Instructions"] = "Send to wallet and upload proof."
    };

    private static ManualPaymentOptions ValidOptions() => new()
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
            RequireCustomerProof = true,
            RequireReferenceNumber = true
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
                Channel = ManualPaymentChannel.VodafoneCash,
                Enabled = true,
                DisplayName = "Vodafone Cash",
                Instructions = "Send to wallet and upload proof."
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
