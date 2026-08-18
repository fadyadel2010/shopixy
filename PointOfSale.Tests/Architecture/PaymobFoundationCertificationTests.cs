using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.Paymob;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobFoundationCertificationTests
{
    [Fact]
    public void ConfigurationBinding_MustProduceImmutablePaymobOptions()
    {
        var options = BindOptions(new Dictionary<string, string?>
        {
            ["Storefront:Payments:Paymob:Enabled"] = "true",
            ["Storefront:Payments:Paymob:Environment"] = "Sandbox",
            ["Storefront:Payments:Paymob:SandboxMode"] = "true",
            ["Storefront:Payments:Paymob:Timeout"] = "00:00:12",
            ["Storefront:Payments:Paymob:RetryCount"] = "2",
            ["Storefront:Payments:Paymob:FeatureFlags:Enabled"] = "true",
            ["Storefront:Payments:Paymob:FeatureFlags:SandboxEnabled"] = "true",
            ["Storefront:Payments:Paymob:FeatureFlags:ProductionEnabled"] = "false",
            ["Storefront:Payments:Paymob:Callback:Configured"] = "true",
            ["Storefront:Payments:Paymob:Callback:CallbackUrl"] = "https://pos.example.com/api/webhooks/paymob/callback",
            ["Storefront:Payments:Paymob:Callback:WebhookUrl"] = "https://pos.example.com/api/webhooks/paymob"
        });

        Assert.True(options.Enabled);
        Assert.Equal("Storefront:Payments:Paymob", PaymobOptions.SectionName);
        Assert.Equal(PaymobEnvironment.Sandbox, options.Environment);
        Assert.True(options.SandboxMode);
        Assert.Equal(TimeSpan.FromSeconds(12), options.Timeout);
        Assert.Equal(2, options.RetryCount);
        Assert.True(options.FeatureFlags.Enabled);
        Assert.True(options.FeatureFlags.SandboxEnabled);
        Assert.False(options.FeatureFlags.ProductionEnabled);
        Assert.True(options.Callback.Configured);
        Assert.True(options.Capabilities.SupportsOfficialHttpsApi);

        Assert.True(typeof(PaymobFeatureFlags).IsSealed);
        Assert.True(typeof(PaymobCallbackConfiguration).IsSealed);
        Assert.True(typeof(PaymobCapabilityModel).IsSealed);
    }

    [Fact]
    public void DevelopmentOverlay_MustEnableValidTenantScopedSandboxRuntime()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(root, "PointOfSale.API"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();
        var options = new PaymobOptions();
        configuration.GetSection(PaymobOptions.SectionName).Bind(options);

        var result = PaymobCredentialValidator.Validate(options);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.True(PaymobOptionsValidator.IsRuntimeEnabled(options));
        Assert.True(options.FeatureFlags.PaymentExecutionEnabled);
        Assert.True(options.FeatureFlags.CallbackExecutionEnabled);
        Assert.True(options.FeatureFlags.WebhookExecutionEnabled);
        Assert.True(options.Capabilities.PaymentExecutionCertified);
        Assert.True(options.Capabilities.CallbackCertified);
        Assert.True(options.Capabilities.ProviderAdapterCertified);
        Assert.False(PaymobOptionsValidator.AreCredentialsConfigured(options));
    }

    [Fact]
    public void GovernanceValidation_MustFailClosedForMissingEnabledCallbacks()
    {
        var options = new PaymobOptions
        {
            Enabled = true,
            Environment = PaymobEnvironment.Sandbox,
            SandboxMode = true
        };

        var result = PaymobCredentialValidator.Validate(options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("callback configuration", StringComparison.OrdinalIgnoreCase));
        Assert.False(result.CredentialsConfigured);
    }

    [Fact]
    public void CredentialValidation_MustEnforceEnvironmentSeparation()
    {
        var sandboxMismatch = PaymobCredentialValidator.Validate(ValidOptions() with
        {
            Environment = PaymobEnvironment.Sandbox,
            SandboxMode = false
        });
        var productionMismatch = PaymobCredentialValidator.Validate(ValidOptions() with
        {
            Environment = PaymobEnvironment.Production,
            SandboxMode = true,
            FeatureFlags = new PaymobFeatureFlags { ProductionEnabled = true }
        });
        var productionFlagDisabled = PaymobCredentialValidator.Validate(ValidOptions() with
        {
            Environment = PaymobEnvironment.Production,
            SandboxMode = false,
            FeatureFlags = new PaymobFeatureFlags { ProductionEnabled = false }
        });

        Assert.False(sandboxMismatch.IsValid);
        Assert.False(productionMismatch.IsValid);
        Assert.False(productionFlagDisabled.IsValid);
        Assert.Contains(sandboxMismatch.Errors, error => error.Contains("SandboxMode to true", StringComparison.Ordinal));
        Assert.Contains(productionMismatch.Errors, error => error.Contains("SandboxMode to false", StringComparison.Ordinal));
        Assert.Contains(productionFlagDisabled.Errors, error => error.Contains("production feature flag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StartupValidation_MustRejectInvalidIdentifiersCallbacksAndUncertifiedExecutionFlags()
    {
        var invalidIdentifiers = PaymobCredentialValidator.Validate(ValidOptions() with
        {
            IntegrationId = "not-numeric",
            IframeId = "also-not-numeric"
        });
        var missingCallback = PaymobCredentialValidator.Validate(ValidOptions() with
        {
            Callback = new PaymobCallbackConfiguration()
        });
        var executionFlag = PaymobCredentialValidator.Validate(ValidOptions() with
        {
            FeatureFlags = new PaymobFeatureFlags
            {
                Enabled = true,
                SandboxEnabled = true,
                PaymentExecutionEnabled = true
            }
        });

        Assert.False(invalidIdentifiers.IsValid);
        Assert.Contains(invalidIdentifiers.Errors, error => error.Contains("integration ID must be numeric", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invalidIdentifiers.Errors, error => error.Contains("iframe ID must be numeric", StringComparison.OrdinalIgnoreCase));
        Assert.False(missingCallback.IsValid);
        Assert.Contains(missingCallback.Errors, error => error.Contains("callback configuration", StringComparison.OrdinalIgnoreCase));
        Assert.False(executionFlag.IsValid);
        Assert.Contains(executionFlag.Errors, error => error.Contains("certified payment execution", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StartupValidation_MustRejectFeatureEnabledButProviderDisabled()
    {
        var result = PaymobCredentialValidator.Validate(ValidOptions() with
        {
            Enabled = false,
            FeatureFlags = new PaymobFeatureFlags
            {
                Enabled = true,
                SandboxEnabled = true
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("provider must be enabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DependencyInjection_MustRegisterOfficialHttpClientOptionsValidationAndHealthContributorOnly()
    {
        var configuration = BuildConfiguration(ValidConfiguration());
        var services = new ServiceCollection();

        services.AddPaymobFoundation(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var options = provider.GetRequiredService<IOptions<PaymobOptions>>().Value;
        var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(PaymobFoundationServiceCollectionExtensions.HttpClientName);
        var contributor = provider.GetRequiredService<IPaymobFoundationHealthContributor>();
        var validators = provider.GetServices<IValidateOptions<PaymobOptions>>();

        Assert.Equal("Storefront:Payments:Paymob", PaymobOptions.SectionName);
        Assert.Equal(PaymobOptions.OfficialApiBaseUri, httpClient.BaseAddress);
        Assert.Contains(validators, validator => validator.GetType() == typeof(PaymobOptionsValidator));
        Assert.True(contributor.GetStatus().Healthy);
        Assert.False(contributor.GetStatus().ConfigurationStatus.OfficialApiAvailability.UsesUnofficialWrapper);
        Assert.Empty(provider.GetServices<IPaymobPaymentService>());
    }

    [Fact]
    public void DependencyInjection_MustFailForDuplicateFoundationRegistration()
    {
        var configuration = BuildConfiguration(ValidConfiguration());
        var services = new ServiceCollection();

        services.AddPaymobFoundation(configuration);

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddPaymobFoundation(configuration));
        Assert.Contains("duplicate registration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HealthStatus_MustBeConfigurationOnlyAndSerializableWithoutSecrets()
    {
        var options = Options.Create(ValidOptions());

        var status = new PaymobFoundationHealthContributor(options).GetStatus();
        var json = JsonSerializer.Serialize(status);

        Assert.True(status.Healthy);
        Assert.DoesNotContain("paymob_api_key_foundation", json, StringComparison.Ordinal);
        Assert.DoesNotContain("paymob_hmac_foundation", json, StringComparison.Ordinal);
        Assert.Contains("System.Net.Http.HttpClient", json, StringComparison.Ordinal);
        Assert.Contains("https://accept.paymob.com/api/", json, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymobFoundationHealthCheck_MustNotRequirePaymobApiCalls()
    {
        var healthCheck = new PointOfSale.API.HealthChecks.PaymobFoundationHealthCheck(
            new PaymobFoundationHealthContributor(Options.Create(ValidOptions())));

        var result = healthCheck.CheckHealthAsync(new HealthCheckContext()).GetAwaiter().GetResult();

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Paymob foundation configuration is valid.", result.Description);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepRuntimeIndependentFromPaymob()
    {
        var coreProject = ReadRepoFile("PointOfSale.Core/PointOfSale.Core.csproj");
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.DoesNotContain("Paymob", coreProject, StringComparison.OrdinalIgnoreCase);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("Paymob", source, StringComparison.Ordinal);
            Assert.DoesNotContain("accept.paymob.com", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SourceGuards_MustKeepFoundationFreeOfPaymentExecutionObjects()
    {
        var foundationSources = new[]
        {
            "PointOfSale.API/Configuration/CommerceHubPaymobOptions.cs",
            "PointOfSale.API/Services/Payments/Paymob/PaymobFoundationModels.cs",
            "PointOfSale.API/Services/Payments/Paymob/PaymobFoundationService.cs",
            "PointOfSale.API/Services/Payments/Paymob/PaymobFoundationServiceCollectionExtensions.cs",
            "PointOfSale.API/HealthChecks/PaymobFoundationHealthCheck.cs"
        }.Select(ReadRepoFile);

        foreach (var source in foundationSources)
        {
            Assert.DoesNotContain("CreatePaymentIntentAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ValidateCallbackAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("HandleWebhookAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PaymentRuntimeOrchestrator", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PaymentLifecycleService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
        }
    }

    private static PaymobOptions BindOptions(Dictionary<string, string?> values)
    {
        var configuration = BuildConfiguration(values);
        var options = new PaymobOptions();
        configuration.GetSection(PaymobOptions.SectionName).Bind(options);
        return options;
    }

    private static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["CommerceHub:Payments:Paymob:Enabled"] = "true",
        ["CommerceHub:Payments:Paymob:ApiKey"] = "paymob_api_key_foundation",
        ["CommerceHub:Payments:Paymob:IntegrationId"] = "123456",
        ["CommerceHub:Payments:Paymob:IframeId"] = "654321",
        ["CommerceHub:Payments:Paymob:HmacSecret"] = "paymob_hmac_foundation",
        ["CommerceHub:Payments:Paymob:Environment"] = "Sandbox",
        ["CommerceHub:Payments:Paymob:SandboxMode"] = "true",
        ["CommerceHub:Payments:Paymob:Callback:Configured"] = "true",
        ["CommerceHub:Payments:Paymob:Callback:CallbackUrl"] = "https://pos.example.com/api/webhooks/paymob/callback",
        ["CommerceHub:Payments:Paymob:Callback:WebhookUrl"] = "https://pos.example.com/api/webhooks/paymob",
        ["CommerceHub:Payments:Paymob:FeatureFlags:Enabled"] = "true",
        ["CommerceHub:Payments:Paymob:FeatureFlags:SandboxEnabled"] = "true"
    };

    private static PaymobOptions ValidOptions() => new()
    {
        Enabled = true,
        ApiKey = "paymob_api_key_foundation",
        IntegrationId = "123456",
        IframeId = "654321",
        HmacSecret = "paymob_hmac_foundation",
        Environment = PaymobEnvironment.Sandbox,
        SandboxMode = true,
        Callback = ValidCallback(),
        FeatureFlags = new PaymobFeatureFlags
        {
            Enabled = true,
            SandboxEnabled = true
        }
    };

    private static PaymobCallbackConfiguration ValidCallback() => new()
    {
        Configured = true,
        CallbackUrl = new Uri("https://pos.example.com/api/webhooks/paymob/callback"),
        WebhookUrl = new Uri("https://pos.example.com/api/webhooks/paymob")
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
