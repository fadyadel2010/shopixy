using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.Payments.Cod;

namespace PointOfSale.Tests.Architecture;

public sealed class CodReferenceWorkflowFoundationCertificationTests
{
    [Fact]
    public void ConfigurationBinding_MustProduceStronglyTypedDisabledByDefaultOptions()
    {
        var options = BindOptions(new Dictionary<string, string?>());

        Assert.False(options.Enabled);
        Assert.Equal(CodReferenceWorkflowEnvironment.Disabled, options.Environment);
        Assert.True(options.FeatureFlags.FoundationHealthEnabled);
        Assert.True(options.FeatureFlags.ConfigurationValidationEnabled);
        Assert.True(options.FeatureFlags.StartupValidationEnabled);
        Assert.False(options.FeatureFlags.RuntimeExecutionEnabled);
        Assert.False(options.FeatureFlags.EvidenceAdmissionEnabled);
        Assert.False(options.FeatureFlags.LifecycleMutationEnabled);
        Assert.False(options.FeatureFlags.SettlementExecutionEnabled);
        Assert.False(options.FeatureFlags.ReplayExecutionEnabled);
        Assert.False(options.FeatureFlags.RecoveryExecutionEnabled);
        Assert.False(options.FeatureFlags.RetentionExecutionEnabled);

        Assert.True(typeof(CodReferenceWorkflowOptions).IsSealed);
        Assert.True(typeof(CodReferenceWorkflowFeatureFlags).IsSealed);
        Assert.True(typeof(CodCollectionPolicyConfiguration).IsSealed);
        Assert.True(typeof(CodEligibilityPolicyConfiguration).IsSealed);
        Assert.True(typeof(CodSettlementPolicyConfiguration).IsSealed);
        Assert.True(typeof(CodEvidencePolicyConfiguration).IsSealed);
        Assert.True(typeof(CodReplayPolicyConfiguration).IsSealed);
        Assert.True(typeof(CodRecoveryPolicyConfiguration).IsSealed);
        Assert.True(typeof(CodRetentionPolicyConfiguration).IsSealed);
    }

    [Fact]
    public void ConfigurationValidation_MustPermitDisabledFoundationAndRejectExecutionFlags()
    {
        var disabled = CodReferenceWorkflowConfigurationValidator.Validate(new CodReferenceWorkflowOptions());
        var runtimeEnabled = CodReferenceWorkflowConfigurationValidator.Validate(new CodReferenceWorkflowOptions
        {
            FeatureFlags = new CodReferenceWorkflowFeatureFlags { RuntimeExecutionEnabled = true }
        });
        var lifecycleEnabled = CodReferenceWorkflowConfigurationValidator.Validate(new CodReferenceWorkflowOptions
        {
            FeatureFlags = new CodReferenceWorkflowFeatureFlags { LifecycleMutationEnabled = true }
        });
        var settlementEnabled = CodReferenceWorkflowConfigurationValidator.Validate(new CodReferenceWorkflowOptions
        {
            FeatureFlags = new CodReferenceWorkflowFeatureFlags { SettlementExecutionEnabled = true }
        });

        Assert.True(disabled.IsValid);
        Assert.False(disabled.Enabled);
        Assert.False(runtimeEnabled.IsValid);
        Assert.False(lifecycleEnabled.IsValid);
        Assert.False(settlementEnabled.IsValid);
        Assert.Contains(runtimeEnabled.Errors, error => error.Contains("Runtime execution", StringComparison.Ordinal));
        Assert.Contains(lifecycleEnabled.Errors, error => error.Contains("lifecycle mutation", StringComparison.Ordinal));
        Assert.Contains(settlementEnabled.Errors, error => error.Contains("settlement execution", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfigurationValidation_MustRequireAllCanonicalPoliciesBeforeEnabling()
    {
        var invalid = CodReferenceWorkflowConfigurationValidator.Validate(new CodReferenceWorkflowOptions
        {
            Enabled = true,
            Environment = CodReferenceWorkflowEnvironment.Sandbox
        });
        var valid = CodReferenceWorkflowConfigurationValidator.Validate(ValidOptions());

        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, error => error.Contains("every canonical policy", StringComparison.OrdinalIgnoreCase));
        Assert.True(valid.IsValid);
        Assert.True(valid.CollectionPolicyConfigured);
        Assert.True(valid.EligibilityPolicyConfigured);
        Assert.True(valid.SettlementPolicyConfigured);
        Assert.True(valid.EvidencePolicyConfigured);
        Assert.True(valid.ReplayPolicyConfigured);
        Assert.True(valid.RecoveryPolicyConfigured);
        Assert.True(valid.RetentionPolicyConfigured);
    }

    [Fact]
    public void CapabilityModel_MustDefineReferenceWorkflowCapabilitiesWithoutCertifyingExecution()
    {
        var capabilities = CodCapabilityModel.FoundationOnly;

        Assert.True(capabilities.ReferenceWorkflowIdentityDefined);
        Assert.True(capabilities.PaymentMethodSelection);
        Assert.True(capabilities.Collection);
        Assert.True(capabilities.Settlement);
        Assert.True(capabilities.EvidenceAdmission);
        Assert.True(capabilities.Replay);
        Assert.True(capabilities.Recovery);
        Assert.True(capabilities.Retention);
        Assert.True(capabilities.OperationalReview);
        Assert.True(capabilities.Dispute);
        Assert.True(capabilities.Reconciliation);
        Assert.True(capabilities.AuthorityContractsDefined);
        Assert.False(capabilities.RuntimeIntegrationCertified);
        Assert.False(capabilities.EvidenceProcessingCertified);
        Assert.False(capabilities.LifecycleMutationCertified);
        Assert.False(capabilities.SettlementExecutionCertified);
        Assert.False(capabilities.WorkflowApplicationCertified);
    }

    [Fact]
    public void AuthorityContracts_MustBeInterfacesOnlyAndRegisteredInCatalog()
    {
        var catalog = new CodAuthorityContractCatalog();
        var expected = new[]
        {
            typeof(ICodEligibilityAuthority),
            typeof(ICodCollectionAuthority),
            typeof(ICodSettlementAuthority),
            typeof(ICodEvidenceAuthority),
            typeof(ICodReplayAuthority),
            typeof(ICodRecoveryAuthority),
            typeof(ICodRetentionAuthority),
            typeof(ICodOperationalAuthority)
        };

        foreach (var contract in expected)
        {
            Assert.True(contract.IsInterface);
            Assert.Empty(contract.GetMethods());
            Assert.Contains(contract, catalog.AuthorityContracts);
        }
    }

    [Fact]
    public void DependencyInjection_MustRegisterFoundationOnlyAndRejectDuplicateRegistration()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(ValidConfiguration());

        services.AddCodReferenceWorkflowFoundation(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var options = provider.GetRequiredService<IOptions<CodReferenceWorkflowOptions>>().Value;
        var health = provider.GetRequiredService<ICodReferenceWorkflowHealthContributor>();
        var catalog = provider.GetRequiredService<ICodAuthorityContractCatalog>();
        var validators = provider.GetServices<IValidateOptions<CodReferenceWorkflowOptions>>();

        Assert.True(options.Enabled);
        Assert.True(health.GetStatus().Healthy);
        Assert.Equal(8, catalog.AuthorityContracts.Count);
        Assert.Contains(validators, validator => validator.GetType() == typeof(CodReferenceWorkflowOptionsValidator));
        Assert.Empty(provider.GetServices<ICodCollectionAuthority>());
        Assert.Empty(provider.GetServices<ICodSettlementAuthority>());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCodReferenceWorkflowFoundation(configuration));
        Assert.Contains("duplicate registration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthCheck_MustCertifyDisabledByDefaultWithoutRuntimeParticipation()
    {
        var services = new ServiceCollection();
        services.AddCodReferenceWorkflowFoundation(BuildConfiguration(new Dictionary<string, string?>()));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var healthCheck = new PointOfSale.API.HealthChecks.CodReferenceWorkflowFoundationHealthCheck(
            provider.GetRequiredService<ICodReferenceWorkflowHealthContributor>());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("COD Reference Workflow foundation configuration is valid.", result.Description);
        Assert.False((bool)result.Data["enabled"]);
        Assert.True((bool)result.Data["disabledByDefaultVerified"]);
        Assert.False((bool)result.Data["externalProvider"]);
        Assert.False((bool)result.Data["runtimeExecutionEnabled"]);
        Assert.False((bool)result.Data["evidenceAdmissionEnabled"]);
        Assert.False((bool)result.Data["lifecycleMutationEnabled"]);
        Assert.False((bool)result.Data["settlementExecutionEnabled"]);
    }

    [Fact]
    public void HealthStatus_MustBeSerializableWithoutOperationalEvidenceOrMutationData()
    {
        var contributor = new CodReferenceWorkflowHealthContributor(
            Options.Create(ValidOptions()),
            new CodAuthorityContractCatalog());
        var status = contributor.GetStatus();
        var json = JsonSerializer.Serialize(status);

        Assert.True(status.Healthy);
        Assert.False(status.DisabledByDefaultVerified);
        Assert.Contains("cod_reference_workflow", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPaymentLifecycleAuthority", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ShipmentCodCollectedEvent", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TelemetryDefinitions_MustRegisterNamesOnlyWithoutEmissionApi()
    {
        using var telemetry = new CodReferenceWorkflowTelemetryDefinitions();

        Assert.Equal("PointOfSale.Payments.Cod.ReferenceWorkflow", CodReferenceWorkflowTelemetryDefinitions.MeterName);
        Assert.Equal("PointOfSale.Payments.Cod.ReferenceWorkflow", CodReferenceWorkflowTelemetryDefinitions.ActivitySourceName);
        Assert.Contains("cod_reference_workflow_foundation_registered_total", CodReferenceWorkflowTelemetryDefinitions.RequiredMetricNames);
        Assert.Contains(CodReferenceWorkflowTelemetryDefinitions.AuthorityContractsRegisteredEventName, CodReferenceWorkflowTelemetryDefinitions.RequiredEventNames);
        Assert.Contains(CodReferenceWorkflowTelemetryDefinitions.DisabledByDefaultVerifiedEventName, CodReferenceWorkflowTelemetryDefinitions.RequiredEventNames);
        Assert.Empty(typeof(CodReferenceWorkflowTelemetryDefinitions)
            .GetMethods()
            .Where(method => method.Name.StartsWith("Record", StringComparison.Ordinal) ||
                             method.Name.StartsWith("Emit", StringComparison.Ordinal)));
    }

    [Fact]
    public void ArchitectureGuards_MustKeepRuntimeFrozenAndCodFreeOfProviderAdapterShape()
    {
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("CodReferenceWorkflowOptions", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CashOnDelivery", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CodRuntimeBridge", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CodEvidenceAdapter", source, StringComparison.Ordinal);
        }

        var codSources = new[]
        {
            ReadRepoFile("PointOfSale.API/Services/Payments/Cod/CodAuthorityContracts.cs"),
            ReadRepoFile("PointOfSale.API/Services/Payments/Cod/CodFoundationModels.cs"),
            ReadRepoFile("PointOfSale.API/Services/Payments/Cod/CodFoundationService.cs"),
            ReadRepoFile("PointOfSale.API/Services/Payments/Cod/CodFoundationServiceCollectionExtensions.cs"),
            ReadRepoFile("PointOfSale.API/Configuration/CodReferenceWorkflowOptions.cs"),
            ReadRepoFile("PointOfSale.API/HealthChecks/CodReferenceWorkflowFoundationHealthCheck.cs"),
            ReadRepoFile("PointOfSale.API/Instrumentation/CodReferenceWorkflowTelemetryDefinitions.cs")
        };

        foreach (var source in codSources)
        {
            Assert.DoesNotContain("PaymentRuntimeOrchestrator", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PaymentLifecycleService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CommerceHubPaymentLifecycleAuthority", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CodRuntimeBridge", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CodEvidenceAdapter", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Controller", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SettlementService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CollectionService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("LifecycleService", source, StringComparison.Ordinal);
        }
    }

    private static CodReferenceWorkflowOptions BindOptions(Dictionary<string, string?> values)
    {
        var configuration = BuildConfiguration(values);
        var options = new CodReferenceWorkflowOptions();
        configuration.GetSection(CodReferenceWorkflowOptions.SectionName).Bind(options);
        return options;
    }

    private static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["Payments:CashOnDelivery:ReferenceWorkflow:Enabled"] = "true",
        ["Payments:CashOnDelivery:ReferenceWorkflow:Environment"] = "Sandbox",
        ["Payments:CashOnDelivery:ReferenceWorkflow:CollectionPolicy:Configured"] = "true",
        ["Payments:CashOnDelivery:ReferenceWorkflow:EligibilityPolicy:Configured"] = "true",
        ["Payments:CashOnDelivery:ReferenceWorkflow:SettlementPolicy:Configured"] = "true",
        ["Payments:CashOnDelivery:ReferenceWorkflow:EvidencePolicy:Configured"] = "true",
        ["Payments:CashOnDelivery:ReferenceWorkflow:ReplayPolicy:Configured"] = "true",
        ["Payments:CashOnDelivery:ReferenceWorkflow:RecoveryPolicy:Configured"] = "true",
        ["Payments:CashOnDelivery:ReferenceWorkflow:RetentionPolicy:Configured"] = "true"
    };

    private static CodReferenceWorkflowOptions ValidOptions() => new()
    {
        Enabled = true,
        Environment = CodReferenceWorkflowEnvironment.Sandbox,
        CollectionPolicy = new CodCollectionPolicyConfiguration { Configured = true },
        EligibilityPolicy = new CodEligibilityPolicyConfiguration { Configured = true },
        SettlementPolicy = new CodSettlementPolicyConfiguration { Configured = true },
        EvidencePolicy = new CodEvidencePolicyConfiguration { Configured = true },
        ReplayPolicy = new CodReplayPolicyConfiguration { Configured = true },
        RecoveryPolicy = new CodRecoveryPolicyConfiguration { Configured = true },
        RetentionPolicy = new CodRetentionPolicyConfiguration { Configured = true }
    };

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
