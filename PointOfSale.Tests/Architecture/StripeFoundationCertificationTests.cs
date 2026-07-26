using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.Stripe;
using StripeClient = Stripe.StripeClient;

namespace PointOfSale.Tests.Architecture;

public sealed class StripeFoundationCertificationTests
{
    [Fact]
    public void ConfigurationBinding_MustProduceImmutableStripeOptions()
    {
        var options = BindOptions(new Dictionary<string, string?>
        {
            ["CommerceHub:Payments:Stripe:Enabled"] = "true",
            ["CommerceHub:Payments:Stripe:PublishableKey"] = "pk_test_foundation123456",
            ["CommerceHub:Payments:Stripe:SecretKey"] = "rk_test_foundation123456",
            ["CommerceHub:Payments:Stripe:WebhookSecret"] = "whsec_foundation123456",
            ["CommerceHub:Payments:Stripe:ApiVersion"] = "2026-06-24.dahlia",
            ["CommerceHub:Payments:Stripe:Environment"] = "Sandbox",
            ["CommerceHub:Payments:Stripe:Timeout"] = "00:00:12",
            ["CommerceHub:Payments:Stripe:RetryCount"] = "2",
            ["CommerceHub:Payments:Stripe:TelemetryEnabled"] = "false",
            ["CommerceHub:Payments:Stripe:SandboxMode"] = "true",
            ["CommerceHub:Payments:Stripe:FeatureFlags:Enabled"] = "true",
            ["CommerceHub:Payments:Stripe:FeatureFlags:SandboxEnabled"] = "true",
            ["CommerceHub:Payments:Stripe:FeatureFlags:ProductionEnabled"] = "false",
            ["CommerceHub:Payments:Stripe:FeatureFlags:RefundsEnabled"] = "false"
        });

        Assert.True(options.Enabled);
        Assert.Equal("pk_test_foundation123456", options.PublishableKey);
        Assert.Equal("rk_test_foundation123456", options.SecretKey);
        Assert.Equal("whsec_foundation123456", options.WebhookSecret);
        Assert.Equal("2026-06-24.dahlia", options.ApiVersion);
        Assert.Equal(StripeEnvironment.Sandbox, options.Environment);
        Assert.Equal(TimeSpan.FromSeconds(12), options.Timeout);
        Assert.Equal(2, options.RetryCount);
        Assert.False(options.TelemetryEnabled);
        Assert.True(options.SandboxMode);
        Assert.True(options.FeatureFlags.Enabled);
        Assert.True(options.FeatureFlags.SandboxEnabled);
        Assert.False(options.FeatureFlags.ProductionEnabled);
        Assert.False(options.FeatureFlags.RefundsEnabled);

        Assert.True(typeof(StripeOptions).IsSealed);
        Assert.True(typeof(StripeFeatureFlags).IsSealed);
    }

    [Fact]
    public void CredentialValidation_MustFailClosedForMissingEnabledCredentials()
    {
        var options = new StripeOptions
        {
            Enabled = true,
            Environment = StripeEnvironment.Sandbox,
            SandboxMode = true
        };

        var result = StripeCredentialValidator.Validate(options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("publishable key", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("secret key", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("webhook secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CredentialValidation_MustEnforceEnvironmentSeparation()
    {
        var sandboxWithLiveKeys = StripeCredentialValidator.Validate(new StripeOptions
        {
            Enabled = true,
            PublishableKey = "pk_live_foundation123456",
            SecretKey = "sk_live_foundation123456",
            WebhookSecret = "whsec_foundation123456",
            Environment = StripeEnvironment.Sandbox,
            SandboxMode = true
        });
        var productionWithTestKeys = StripeCredentialValidator.Validate(new StripeOptions
        {
            Enabled = true,
            PublishableKey = "pk_test_foundation123456",
            SecretKey = "rk_test_foundation123456",
            WebhookSecret = "whsec_foundation123456",
            Environment = StripeEnvironment.Production,
            SandboxMode = false
        });

        Assert.False(sandboxWithLiveKeys.IsValid);
        Assert.False(productionWithTestKeys.IsValid);
        Assert.Contains(sandboxWithLiveKeys.Errors, error => error.Contains("pk_test_", StringComparison.Ordinal));
        Assert.Contains(productionWithTestKeys.Errors, error => error.Contains("pk_live_", StringComparison.Ordinal));
    }

    [Fact]
    public void StartupValidation_MustRejectApiVersionAndEnvironmentFlagDrift()
    {
        var apiVersionDrift = StripeCredentialValidator.Validate(new StripeOptions
        {
            Enabled = true,
            PublishableKey = "pk_test_foundation123456",
            SecretKey = "rk_test_foundation123456",
            WebhookSecret = "whsec_foundation123456",
            ApiVersion = "2026-05-27.dahlia",
            Environment = StripeEnvironment.Sandbox,
            SandboxMode = true
        });
        var productionFlagDisabled = StripeCredentialValidator.Validate(new StripeOptions
        {
            Enabled = true,
            PublishableKey = "pk_live_foundation123456",
            SecretKey = "rk_live_foundation123456",
            WebhookSecret = "whsec_foundation123456",
            Environment = StripeEnvironment.Production,
            SandboxMode = false,
            FeatureFlags = new StripeFeatureFlags { ProductionEnabled = false }
        });

        Assert.False(apiVersionDrift.IsValid);
        Assert.Contains(apiVersionDrift.Errors, error => error.Contains(StripeOptions.CertifiedApiVersion, StringComparison.Ordinal));
        Assert.False(productionFlagDisabled.IsValid);
        Assert.Contains(productionFlagDisabled.Errors, error => error.Contains("production feature flag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DependencyInjection_MustRegisterOfficialSdkOptionsValidationAndHealthContributor()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["CommerceHub:Payments:Stripe:Enabled"] = "true",
            ["CommerceHub:Payments:Stripe:PublishableKey"] = "pk_test_foundation123456",
            ["CommerceHub:Payments:Stripe:SecretKey"] = "rk_test_foundation123456",
            ["CommerceHub:Payments:Stripe:WebhookSecret"] = "whsec_foundation123456",
            ["CommerceHub:Payments:Stripe:ApiVersion"] = "2026-06-24.dahlia",
            ["CommerceHub:Payments:Stripe:Environment"] = "Sandbox",
            ["CommerceHub:Payments:Stripe:SandboxMode"] = "true"
        });
        var services = new ServiceCollection();

        services.AddStripeFoundation(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var options = provider.GetRequiredService<IOptions<StripeOptions>>().Value;
        var client = provider.GetRequiredService<StripeClient>();
        var contributor = provider.GetRequiredService<IStripeFoundationHealthContributor>();
        var validators = provider.GetServices<IValidateOptions<StripeOptions>>();

        Assert.Equal("rk_test_foundation123456", options.SecretKey);
        Assert.NotNull(client);
        Assert.Contains(validators, validator => validator.GetType() == typeof(StripeOptionsValidator));
        Assert.True(contributor.GetStatus().Healthy);
        Assert.Equal("Stripe.net", contributor.GetStatus().ConfigurationStatus.SDKAvailability.PackageName);
    }

    [Fact]
    public void HealthStatus_MustBeConfigurationOnlyAndSerializableWithoutSecrets()
    {
        var options = Options.Create(new StripeOptions
        {
            Enabled = true,
            PublishableKey = "pk_test_foundation123456",
            SecretKey = "rk_test_foundation123456",
            WebhookSecret = "whsec_foundation123456",
            ApiVersion = "2026-06-24.dahlia",
            Environment = StripeEnvironment.Sandbox,
            SandboxMode = true
        });

        var status = new StripeFoundationHealthContributor(options).GetStatus();
        var json = JsonSerializer.Serialize(status);

        Assert.True(status.Healthy);
        Assert.DoesNotContain("rk_test_foundation123456", json, StringComparison.Ordinal);
        Assert.DoesNotContain("whsec_foundation123456", json, StringComparison.Ordinal);
        Assert.Contains("Stripe.net", json, StringComparison.Ordinal);
        Assert.Contains("2026-06-24.dahlia", json, StringComparison.Ordinal);
    }

    [Fact]
    public void StripeFoundationHealthCheck_MustNotRequireStripeApiCalls()
    {
        var options = Options.Create(new StripeOptions
        {
            Enabled = true,
            PublishableKey = "pk_test_foundation123456",
            SecretKey = "rk_test_foundation123456",
            WebhookSecret = "whsec_foundation123456",
            ApiVersion = "2026-06-24.dahlia",
            Environment = StripeEnvironment.Sandbox,
            SandboxMode = true
        });
        var healthCheck = new PointOfSale.API.HealthChecks.StripeFoundationHealthCheck(
            new StripeFoundationHealthContributor(options));

        var result = healthCheck.CheckHealthAsync(new HealthCheckContext()).GetAwaiter().GetResult();

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Stripe foundation configuration is valid.", result.Description);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepRuntimeIndependentFromStripeSdk()
    {
        var coreProject = ReadRepoFile("PointOfSale.Core/PointOfSale.Core.csproj");
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.DoesNotContain("Stripe.net", coreProject, StringComparison.OrdinalIgnoreCase);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("using Stripe", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Stripe.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PaymentIntentService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CheckoutSession", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SourceGuards_MustKeepFoundationFreeOfPaymentExecutionObjects()
    {
        var foundationSources = new[]
        {
            "PointOfSale.API/Configuration/StripeOptions.cs",
            "PointOfSale.API/Services/Payments/Stripe/StripeFoundationModels.cs",
            "PointOfSale.API/Services/Payments/Stripe/StripeFoundationService.cs",
            "PointOfSale.API/Services/Payments/Stripe/StripeFoundationServiceCollectionExtensions.cs",
            "PointOfSale.API/HealthChecks/StripeFoundationHealthCheck.cs"
        }.Select(ReadRepoFile);

        foreach (var source in foundationSources)
        {
            Assert.DoesNotContain("PaymentIntentService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SessionService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RefundService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ChargeService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ConstructEvent", source, StringComparison.Ordinal);
        }
    }

    private static StripeOptions BindOptions(Dictionary<string, string?> values)
    {
        var configuration = BuildConfiguration(values);
        var options = new StripeOptions();
        configuration.GetSection(StripeOptions.SectionName).Bind(options);
        return options;
    }

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
