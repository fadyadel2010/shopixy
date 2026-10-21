using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ManualPayment;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// Locks the canonical Manual Wallet activation contract.
///
/// Manual Wallet is a backend-owned, offline payment workflow with no gateway. A merchant
/// wallet must become available for checkout from the tenant-owned Enabled state plus a
/// valid recipient, with no Sandbox/Live toggle and no second deployment-level
/// runtime-enable switch, while genuine prerequisites (manual channel, instructions,
/// customer proof, expiration policy, and the certified review, approval and rejection
/// workflows) keep failing closed.
/// </summary>
public sealed class ManualPaymentFoundationActivationContractTests
{
    [Fact]
    public void FoundationValidation_MustNotDependOnADeploymentLevelRuntimeEnableSwitch()
    {
        var complete = CompleteOptions();

        var legacyDisabled = ManualPaymentConfigurationValidator.Validate(complete with
        {
            Enabled = false,
            FeatureFlags = complete.FeatureFlags with { Enabled = false }
        });

        Assert.True(legacyDisabled.IsValid, string.Join(Environment.NewLine, legacyDisabled.Errors));
        Assert.Empty(legacyDisabled.Errors);
        Assert.True(legacyDisabled.Enabled);
    }

    [Fact]
    public void FoundationValidation_MustNotLetSandboxOrLiveModeDecideAvailability()
    {
        var complete = CompleteOptions();

        var sandbox = ManualPaymentConfigurationValidator.Validate(complete with
        {
            Environment = ManualPaymentEnvironment.Sandbox,
            SandboxMode = true
        });
        var production = ManualPaymentConfigurationValidator.Validate(complete with
        {
            Environment = ManualPaymentEnvironment.Production,
            SandboxMode = false
        });

        Assert.True(sandbox.IsValid);
        Assert.True(production.IsValid);
        Assert.Empty(sandbox.Errors);
        Assert.Empty(production.Errors);
    }

    [Fact]
    public void FoundationValidation_MustRetainGenuinePrerequisitesAndFailClosedWhenTheyAreBroken()
    {
        var complete = CompleteOptions();

        // Manual channel, instructions, customer proof and expiration remain required.
        var noChannel = ManualPaymentConfigurationValidator.Validate(complete with { Channels = [] });
        var noInstructions = ManualPaymentConfigurationValidator.Validate(complete with
        {
            Instructions = complete.Instructions with { DefaultInstructions = "" }
        });
        var noCustomerProof = ManualPaymentConfigurationValidator.Validate(complete with
        {
            Instructions = complete.Instructions with { RequireCustomerProof = false }
        });
        var noExpiration = ManualPaymentConfigurationValidator.Validate(complete with
        {
            Expiration = complete.Expiration with { Configured = false }
        });
        var invalidExpiration = ManualPaymentConfigurationValidator.Validate(complete with
        {
            Expiration = complete.Expiration with { ProofAccessTtl = TimeSpan.Zero }
        });
        var invalidProof = ManualPaymentConfigurationValidator.Validate(complete with
        {
            Proof = new ManualPaymentProofConfiguration
            {
                AcceptedContentTypes = new[] { "text/plain" },
                MaximumUploadBytes = 50 * 1024 * 1024
            }
        });
        var unsupportedChannel = ManualPaymentConfigurationValidator.Validate(complete with
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

        Assert.False(noChannel.IsValid);
        Assert.Contains(noChannel.Errors, error => error.Contains("at least one enabled", StringComparison.OrdinalIgnoreCase));
        Assert.False(noInstructions.IsValid);
        Assert.Contains(noInstructions.Errors, error => error.Contains("customer instructions", StringComparison.OrdinalIgnoreCase));
        Assert.False(noCustomerProof.IsValid);
        Assert.Contains(noCustomerProof.Errors, error => error.Contains("customer proof", StringComparison.OrdinalIgnoreCase));
        Assert.False(noExpiration.IsValid);
        Assert.Contains(noExpiration.Errors, error => error.Contains("expiration policy", StringComparison.OrdinalIgnoreCase));
        Assert.False(invalidExpiration.IsValid);
        Assert.Contains(invalidExpiration.Errors, error => error.Contains("expiration policy", StringComparison.OrdinalIgnoreCase));
        Assert.False(invalidProof.IsValid);
        Assert.Contains(invalidProof.Errors, error => error.Contains("content type", StringComparison.OrdinalIgnoreCase));
        Assert.False(unsupportedChannel.IsValid);
        Assert.Contains(unsupportedChannel.Errors, error => error.Contains("unsupported payment channel", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompositionRoot_MustRegisterTheCapabilitiesTheFoundationRequires()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();

        services.AddManualPaymentFoundation(configuration);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IManualPaymentProviderAdapter));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IManualPaymentRuntimeBridge));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IManualPaymentEvidenceAdapter));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IManualPaymentReviewApplicationService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IManualPaymentCapabilityAdmissionAuthority));
    }

    [Fact]
    public void SharedConfiguration_MustShipACompleteManualPaymentFoundationForEveryEnvironment()
    {
        // The Manual Payment foundation lives in the shared base configuration, so every
        // environment - development, QA, staging, production - resolves the same genuine
        // prerequisites. This is what removes the cross-environment availability gap.
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(root, "PointOfSale.API"))
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var options = new ManualPaymentOptions();
        configuration.GetSection(ManualPaymentOptions.SectionName).Bind(options);

        var validation = ManualPaymentConfigurationValidator.Validate(options);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.True(validation.Enabled);
        Assert.NotEmpty(options.Channels);
        Assert.Contains(options.Channels, channel => channel is { Enabled: true } &&
            !string.IsNullOrWhiteSpace(channel.DisplayName) && !string.IsNullOrWhiteSpace(channel.Instructions));
        Assert.True(options.Instructions.Configured);
        Assert.False(string.IsNullOrWhiteSpace(options.Instructions.DefaultInstructions));
        Assert.True(options.Instructions.RequireCustomerProof);
        Assert.True(options.Expiration.Configured);
        Assert.True(options.Expiration.PendingPaymentTtl > TimeSpan.Zero);
        Assert.True(options.Expiration.ProofAccessTtl > TimeSpan.Zero);
        Assert.True(options.FeatureFlags.ProofUploadEnabled);
        Assert.True(options.FeatureFlags.ReviewExecutionEnabled);
        Assert.True(options.FeatureFlags.ApprovalExecutionEnabled);
        Assert.True(options.FeatureFlags.RejectionExecutionEnabled);
        Assert.True(options.Capabilities.ProviderAdapterCertified);
        Assert.True(options.Capabilities.RuntimeBridgeCertified);
        Assert.True(options.Capabilities.EvidenceAdapterCertified);
        Assert.True(options.Capabilities.ReviewWorkflowCertified);
        Assert.True(options.Capabilities.ApprovalWorkflowCertified);
    }

    [Fact]
    public void FoundationPolicy_MustNotChangeUnrelatedProviderContracts()
    {
        // Stripe/Paymob keep their own environment and runtime-enable contracts.
        var readiness = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/StorefrontPaymentRuntimeReadinessService.cs");
        var stripeSource = CertificationSource.ReadRepoFile("PointOfSale.API/Configuration/StripeOptions.cs");
        var paymobSource = CertificationSource.ReadRepoFile("PointOfSale.API/Configuration/CommerceHubPaymobOptions.cs");

        Assert.Contains("STRIPE_RUNTIME_DISABLED", readiness, StringComparison.Ordinal);
        Assert.Contains("PAYMOB_RUNTIME_DISABLED", readiness, StringComparison.Ordinal);
        Assert.Contains("Stripe sandbox environment must set SandboxMode to true.", stripeSource, StringComparison.Ordinal);
        Assert.Contains("Paymob sandbox environment must set SandboxMode to true.", paymobSource, StringComparison.Ordinal);
    }

    private static ManualPaymentOptions CompleteOptions() => new()
    {
        Enabled = true,
        Environment = ManualPaymentEnvironment.Sandbox,
        SandboxMode = true,
        FeatureFlags = new ManualPaymentFeatureFlags
        {
            Enabled = true,
            SandboxEnabled = true,
            ProofUploadEnabled = true,
            ReviewExecutionEnabled = true,
            ApprovalExecutionEnabled = true,
            RejectionExecutionEnabled = true
        },
        Capabilities = new ManualPaymentCapabilityModel
        {
            ProviderAdapterCertified = true,
            RuntimeBridgeCertified = true,
            EvidenceAdapterCertified = true,
            ReviewWorkflowCertified = true,
            ApprovalWorkflowCertified = true,
            PersistenceCertified = true
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
