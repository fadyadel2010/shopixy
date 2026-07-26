using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Services.Payments.Adapters;

namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentFoundationCertificationTests
{
    [Fact]
    public void ConfigurationBinding_MustProduceImmutableManualPaymentOptions()
    {
        var options = BindOptions(ValidConfiguration());

        Assert.True(options.Enabled);
        Assert.Equal(ManualPaymentEnvironment.Sandbox, options.Environment);
        Assert.True(options.SandboxMode);
        Assert.True(options.FeatureFlags.Enabled);
        Assert.True(options.Instructions.Configured);
        Assert.Equal("Use the exact payment reference in your transfer.", options.Instructions.DefaultInstructions);
        Assert.True(options.Expiration.Configured);
        Assert.Equal(TimeSpan.FromDays(2), options.Expiration.PendingPaymentTtl);
        Assert.Equal(2, options.Channels.Count);
        Assert.Contains(options.Channels, channel => channel.Channel == ManualPaymentChannel.VodafoneCash);
        Assert.Contains(options.Proof.AcceptedContentTypes, type => type == "application/pdf");

        Assert.True(typeof(ManualPaymentOptions).IsSealed);
        Assert.True(typeof(ManualPaymentFeatureFlags).IsSealed);
        Assert.True(typeof(ManualPaymentCapabilityModel).IsSealed);
        Assert.True(typeof(ManualPaymentInstructionConfiguration).IsSealed);
        Assert.True(typeof(ManualPaymentProofConfiguration).IsSealed);
        Assert.True(typeof(ManualPaymentExpirationPolicy).IsSealed);
        Assert.True(typeof(ManualPaymentChannelConfiguration).IsSealed);
    }

    [Fact]
    public void Validation_MustFailClosedForMissingEnabledConfiguration()
    {
        var result = ManualPaymentConfigurationValidator.Validate(new ManualPaymentOptions
        {
            Enabled = true,
            FeatureFlags = new ManualPaymentFeatureFlags { Enabled = true, SandboxEnabled = true }
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("at least one enabled", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("customer instructions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("expiration policy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validation_MustFailClosedForDisabledProviderAndEnvironmentMismatch()
    {
        var disabledProvider = ManualPaymentConfigurationValidator.Validate(ValidOptions() with
        {
            Enabled = false,
            FeatureFlags = new ManualPaymentFeatureFlags { Enabled = true, SandboxEnabled = true }
        });
        var sandboxMismatch = ManualPaymentConfigurationValidator.Validate(ValidOptions() with
        {
            Environment = ManualPaymentEnvironment.Sandbox,
            SandboxMode = false
        });
        var productionFlagDisabled = ManualPaymentConfigurationValidator.Validate(ValidOptions() with
        {
            Environment = ManualPaymentEnvironment.Production,
            SandboxMode = false,
            FeatureFlags = new ManualPaymentFeatureFlags { Enabled = true, ProductionEnabled = false }
        });

        Assert.False(disabledProvider.IsValid);
        Assert.False(sandboxMismatch.IsValid);
        Assert.False(productionFlagDisabled.IsValid);
        Assert.Contains(disabledProvider.Errors, error => error.Contains("provider must be enabled", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sandboxMismatch.Errors, error => error.Contains("SandboxMode to true", StringComparison.Ordinal));
        Assert.Contains(productionFlagDisabled.Errors, error => error.Contains("production feature flag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validation_MustFailClosedForUnsupportedChannelAndInvalidProofConfiguration()
    {
        var unsupportedChannel = ManualPaymentConfigurationValidator.Validate(ValidOptions() with
        {
            Channels = new[]
            {
                new ManualPaymentChannelConfiguration
                {
                    Channel = (ManualPaymentChannel)999,
                    Enabled = true,
                    DisplayName = "Unknown",
                    Instructions = "Unknown instructions."
                }
            }
        });
        var invalidProof = ManualPaymentConfigurationValidator.Validate(ValidOptions() with
        {
            Proof = new ManualPaymentProofConfiguration
            {
                AcceptedContentTypes = new[] { "text/plain" },
                MaximumUploadBytes = 50 * 1024 * 1024
            }
        });

        Assert.False(unsupportedChannel.IsValid);
        Assert.False(invalidProof.IsValid);
        Assert.Contains(unsupportedChannel.Errors, error => error.Contains("unsupported payment channel", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invalidProof.Errors, error => error.Contains("content type", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invalidProof.Errors, error => error.Contains("25 MiB", StringComparison.Ordinal));
    }

    [Fact]
    public void CapabilityModel_MustExposeFoundationCapabilitiesWithoutCertifyingRuntimeOrApproval()
    {
        var capabilities = ManualPaymentCapabilityModel.FoundationOnly;

        Assert.True(capabilities.SupportsTenantScopedConfiguration);
        Assert.True(capabilities.SupportsInstructionConfiguration);
        Assert.True(capabilities.SupportsReceiptProofUpload);
        Assert.True(capabilities.SupportsBankTransfer);
        Assert.True(capabilities.SupportsVodafoneCash);
        Assert.True(capabilities.SupportsInstapay);
        Assert.True(capabilities.SupportsWalletTransfer);
        Assert.True(capabilities.SupportsCashDeposit);
        Assert.True(capabilities.SupportsCustomInstructions);
        Assert.True(capabilities.SupportsOfflineInvoice);
        Assert.True(capabilities.SupportsReferenceNumber);
        Assert.True(capabilities.SupportsExpirationPolicy);
        Assert.False(capabilities.ProviderAdapterCertified);
        Assert.False(capabilities.RuntimeBridgeCertified);
        Assert.False(capabilities.EvidenceAdapterCertified);
        Assert.False(capabilities.ReviewWorkflowCertified);
        Assert.False(capabilities.ApprovalWorkflowCertified);
        Assert.False(capabilities.PersistenceCertified);
    }

    [Fact]
    public void DependencyInjection_MustRegisterOptionsValidationAndHealthContributorOnly()
    {
        var configuration = BuildConfiguration(ValidConfiguration());
        var services = new ServiceCollection();

        services.AddManualPaymentFoundation(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var options = provider.GetRequiredService<IOptions<ManualPaymentOptions>>().Value;
        var contributor = provider.GetRequiredService<IManualPaymentFoundationHealthContributor>();
        var validators = provider.GetServices<IValidateOptions<ManualPaymentOptions>>();

        Assert.True(options.Enabled);
        Assert.Contains(validators, validator => validator.GetType() == typeof(ManualPaymentOptionsValidator));
        Assert.True(contributor.GetStatus().Healthy);
        Assert.Equal("manual_payment", contributor.GetStatus().ConfigurationStatus.ProviderRegistration.ProviderCode);
        Assert.False(contributor.GetStatus().ConfigurationStatus.ProviderRegistration.RuntimeExecutionEnabled);
        Assert.Empty(provider.GetServices<ManualWalletPaymentAdapter>());
    }

    [Fact]
    public void DependencyInjection_MustFailForDuplicateFoundationRegistration()
    {
        var configuration = BuildConfiguration(ValidConfiguration());
        var services = new ServiceCollection();

        services.AddManualPaymentFoundation(configuration);

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddManualPaymentFoundation(configuration));
        Assert.Contains("duplicate registration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HealthStatus_MustBeConfigurationOnlyAndSerializableWithoutInstructions()
    {
        var status = new ManualPaymentFoundationHealthContributor(Options.Create(ValidOptions())).GetStatus();
        var json = JsonSerializer.Serialize(status);

        Assert.True(status.Healthy);
        Assert.DoesNotContain("Use the exact payment reference", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Send to wallet", json, StringComparison.Ordinal);
        Assert.Contains("manual_payment", json, StringComparison.Ordinal);
        Assert.Contains("Backend-owned workflow provider", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManualPaymentFoundationHealthCheck_MustNotRequireRuntimeOrStorageCalls()
    {
        var healthCheck = new PointOfSale.API.HealthChecks.ManualPaymentFoundationHealthCheck(
            new ManualPaymentFoundationHealthContributor(Options.Create(ValidOptions())));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Manual Payment foundation configuration is valid.", result.Description);
        Assert.False((bool)result.Data["runtimeExecutionEnabled"]);
        Assert.False((bool)result.Data["externalNetworkRequired"]);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepRuntimeIndependentFromManualPaymentProvider()
    {
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("ManualPaymentOptions", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ManualWalletPaymentAdapter", source, StringComparison.Ordinal);
            Assert.DoesNotContain("VodafoneCash", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Instapay", source, StringComparison.Ordinal);
            Assert.DoesNotContain("BankTransfer", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CashDeposit", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SourceGuards_MustKeepFoundationFreeOfRuntimeApprovalPersistenceAndControllers()
    {
        var foundationSources = new[]
        {
            "PointOfSale.API/Configuration/ManualPaymentOptions.cs",
            "PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentFoundationModels.cs",
            "PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentFoundationService.cs",
            "PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentFoundationServiceCollectionExtensions.cs",
            "PointOfSale.API/HealthChecks/ManualPaymentFoundationHealthCheck.cs"
        }.Select(ReadRepoFile);

        foreach (var source in foundationSources)
        {
            Assert.DoesNotContain("InitiateAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ApproveAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RejectAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PaymentRuntimeOrchestrator", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PaymentLifecycleService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Controller", source, StringComparison.Ordinal);
        }
    }

    private static ManualPaymentOptions BindOptions(Dictionary<string, string?> values)
    {
        var configuration = BuildConfiguration(values);
        var options = new ManualPaymentOptions();
        configuration.GetSection(ManualPaymentOptions.SectionName).Bind(options);
        return options;
    }

    private static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["CommerceHub:Payments:ManualPayment:Enabled"] = "true",
        ["CommerceHub:Payments:ManualPayment:Environment"] = "Sandbox",
        ["CommerceHub:Payments:ManualPayment:SandboxMode"] = "true",
        ["CommerceHub:Payments:ManualPayment:FeatureFlags:Enabled"] = "true",
        ["CommerceHub:Payments:ManualPayment:FeatureFlags:SandboxEnabled"] = "true",
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
        ["CommerceHub:Payments:ManualPayment:Channels:0:Instructions"] = "Send to wallet and upload proof.",
        ["CommerceHub:Payments:ManualPayment:Channels:1:Channel"] = "Instapay",
        ["CommerceHub:Payments:ManualPayment:Channels:1:Enabled"] = "true",
        ["CommerceHub:Payments:ManualPayment:Channels:1:DisplayName"] = "Instapay",
        ["CommerceHub:Payments:ManualPayment:Channels:1:Instructions"] = "Send using Instapay and upload proof."
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
