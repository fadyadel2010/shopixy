using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.API.Services.Payments.Adapters;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryFoundationCertificationTests
{
    [Fact]
    public void ConfigurationBinding_MustProduceImmutableFawryOptions()
    {
        var options = BindOptions(new Dictionary<string, string?>
        {
            ["CommerceHub:Payments:Fawry:Enabled"] = "true",
            ["CommerceHub:Payments:Fawry:MerchantCode"] = "fawry_merchant_foundation",
            ["CommerceHub:Payments:Fawry:ApiKey"] = "fawry_api_key_foundation",
            ["CommerceHub:Payments:Fawry:Secret"] = "fawry_secure_key_foundation",
            ["CommerceHub:Payments:Fawry:Environment"] = "Sandbox",
            ["CommerceHub:Payments:Fawry:SandboxMode"] = "true",
            ["CommerceHub:Payments:Fawry:Timeout"] = "00:00:12",
            ["CommerceHub:Payments:Fawry:RetryCount"] = "2",
            ["CommerceHub:Payments:Fawry:FeatureFlags:Enabled"] = "true",
            ["CommerceHub:Payments:Fawry:FeatureFlags:SandboxEnabled"] = "true",
            ["CommerceHub:Payments:Fawry:FeatureFlags:ProductionEnabled"] = "false",
            ["CommerceHub:Payments:Fawry:Callback:Configured"] = "true",
            ["CommerceHub:Payments:Fawry:Callback:CallbackUrl"] = "https://pos.example.com/api/webhooks/fawry/callback",
            ["CommerceHub:Payments:Fawry:Callback:WebhookUrl"] = "https://pos.example.com/api/webhooks/fawry"
        });

        Assert.True(options.Enabled);
        Assert.Equal("fawry_merchant_foundation", options.MerchantCode);
        Assert.Equal("fawry_api_key_foundation", options.ApiKey);
        Assert.Equal("fawry_secure_key_foundation", options.Secret);
        Assert.Equal(FawryEnvironment.Sandbox, options.Environment);
        Assert.True(options.SandboxMode);
        Assert.Equal(TimeSpan.FromSeconds(12), options.Timeout);
        Assert.Equal(2, options.RetryCount);
        Assert.True(options.FeatureFlags.Enabled);
        Assert.True(options.FeatureFlags.SandboxEnabled);
        Assert.False(options.FeatureFlags.ProductionEnabled);
        Assert.True(options.Callback.Configured);
        Assert.True(options.Capabilities.SupportsOfficialHttpsApi);
        Assert.False(options.Capabilities.SupportsOfficialDotNetSdk);

        Assert.True(typeof(FawryOptions).IsSealed);
        Assert.True(typeof(FawryFeatureFlags).IsSealed);
        Assert.True(typeof(FawryCallbackConfiguration).IsSealed);
        Assert.True(typeof(FawryCapabilityModel).IsSealed);
    }

    [Fact]
    public void CredentialValidation_MustFailClosedForMissingEnabledCredentials()
    {
        var options = new FawryOptions
        {
            Enabled = true,
            Environment = FawryEnvironment.Sandbox,
            SandboxMode = true,
            Callback = ValidCallback()
        };

        var result = FawryCredentialValidator.Validate(options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("merchant code", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("API key", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CredentialValidation_MustEnforceEnvironmentSeparation()
    {
        var sandboxMismatch = FawryCredentialValidator.Validate(ValidOptions() with
        {
            Environment = FawryEnvironment.Sandbox,
            SandboxMode = false
        });
        var productionMismatch = FawryCredentialValidator.Validate(ValidOptions() with
        {
            Environment = FawryEnvironment.Production,
            SandboxMode = true,
            FeatureFlags = new FawryFeatureFlags { ProductionEnabled = true }
        });
        var productionFlagDisabled = FawryCredentialValidator.Validate(ValidOptions() with
        {
            Environment = FawryEnvironment.Production,
            SandboxMode = false,
            FeatureFlags = new FawryFeatureFlags { ProductionEnabled = false }
        });

        Assert.False(sandboxMismatch.IsValid);
        Assert.False(productionMismatch.IsValid);
        Assert.False(productionFlagDisabled.IsValid);
        Assert.Contains(sandboxMismatch.Errors, error => error.Contains("SandboxMode to true", StringComparison.Ordinal));
        Assert.Contains(productionMismatch.Errors, error => error.Contains("SandboxMode to false", StringComparison.Ordinal));
        Assert.Contains(productionFlagDisabled.Errors, error => error.Contains("production feature flag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StartupValidation_MustRejectInvalidCallbackAndUncertifiedExecutionFlags()
    {
        var missingCallback = FawryCredentialValidator.Validate(ValidOptions() with
        {
            Callback = new FawryCallbackConfiguration()
        });
        var executionFlag = FawryCredentialValidator.Validate(ValidOptions() with
        {
            FeatureFlags = new FawryFeatureFlags
            {
                Enabled = true,
                SandboxEnabled = true,
                PaymentExecutionEnabled = true
            }
        });
        var advancedFlag = FawryCredentialValidator.Validate(ValidOptions() with
        {
            FeatureFlags = new FawryFeatureFlags
            {
                Enabled = true,
                SandboxEnabled = true,
                RefundExecutionEnabled = true
            }
        });

        Assert.False(missingCallback.IsValid);
        Assert.Contains(missingCallback.Errors, error => error.Contains("callback and webhook configuration", StringComparison.OrdinalIgnoreCase));
        Assert.False(executionFlag.IsValid);
        Assert.Contains(executionFlag.Errors, error => error.Contains("certified payment execution", StringComparison.OrdinalIgnoreCase));
        Assert.False(advancedFlag.IsValid);
        Assert.Contains(advancedFlag.Errors, error => error.Contains("advanced operation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CapabilityModel_MustExposeOfficialFoundationCapabilitiesWithoutCertifyingExecution()
    {
        var capabilities = FawryCapabilityModel.FoundationOnly;

        Assert.True(capabilities.SupportsOfficialHttpsApi);
        Assert.True(capabilities.SupportsSha256Signature);
        Assert.True(capabilities.SupportsSandboxEnvironment);
        Assert.True(capabilities.SupportsProductionEnvironment);
        Assert.True(capabilities.SupportsConfiguredServerNotification);
        Assert.True(capabilities.SupportsTransactionInquiry);
        Assert.True(capabilities.SupportsExpiration);
        Assert.True(capabilities.SupportsRefund);
        Assert.True(capabilities.SupportsPartialRefund);
        Assert.True(capabilities.SupportsVoidUnpaidOrder);
        Assert.True(capabilities.SupportsAuthorizeAndCapture);
        Assert.False(capabilities.SupportsOfficialDotNetSdk);
        Assert.False(capabilities.PaymentExecutionCertified);
        Assert.False(capabilities.ProviderAdapterCertified);
        Assert.False(capabilities.WebhookCertified);
        Assert.False(capabilities.AdvancedOperationsCertified);
    }

    [Fact]
    public void DependencyInjection_MustRegisterOfficialHttpClientOptionsValidationAndHealthContributorOnly()
    {
        var configuration = BuildConfiguration(ValidConfiguration());
        var services = new ServiceCollection();

        services.AddFawryFoundation(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var options = provider.GetRequiredService<IOptions<FawryOptions>>().Value;
        var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(FawryFoundationServiceCollectionExtensions.HttpClientName);
        var contributor = provider.GetRequiredService<IFawryFoundationHealthContributor>();
        var validators = provider.GetServices<IValidateOptions<FawryOptions>>();

        Assert.Equal("fawry_api_key_foundation", options.ApiKey);
        Assert.Equal(FawryOptions.SandboxApiBaseUri, httpClient.BaseAddress);
        Assert.Contains(validators, validator => validator.GetType() == typeof(FawryOptionsValidator));
        Assert.True(contributor.GetStatus().Healthy);
        Assert.False(contributor.GetStatus().ConfigurationStatus.OfficialApiAvailability.UsesUnofficialWrapper);
        Assert.Empty(provider.GetServices<FawryPaymentAdapter>());
    }

    [Fact]
    public void DependencyInjection_MustFailForDuplicateFoundationRegistration()
    {
        var configuration = BuildConfiguration(ValidConfiguration());
        var services = new ServiceCollection();

        services.AddFawryFoundation(configuration);

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddFawryFoundation(configuration));
        Assert.Contains("duplicate registration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HealthStatus_MustBeConfigurationOnlyAndSerializableWithoutSecrets()
    {
        var options = Options.Create(ValidOptions());

        var status = new FawryFoundationHealthContributor(options).GetStatus();
        var json = JsonSerializer.Serialize(status);

        Assert.True(status.Healthy);
        Assert.DoesNotContain("fawry_merchant_foundation", json, StringComparison.Ordinal);
        Assert.DoesNotContain("fawry_api_key_foundation", json, StringComparison.Ordinal);
        Assert.DoesNotContain("fawry_secure_key_foundation", json, StringComparison.Ordinal);
        Assert.Contains("System.Net.Http.HttpClient", json, StringComparison.Ordinal);
        Assert.Contains("https://atfawry.fawrystaging.com/", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FawryFoundationHealthCheck_MustNotRequireFawryApiCalls()
    {
        var healthCheck = new PointOfSale.API.HealthChecks.FawryFoundationHealthCheck(
            new FawryFoundationHealthContributor(Options.Create(ValidOptions())));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Fawry foundation configuration is valid.", result.Description);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepRuntimeIndependentFromFawry()
    {
        var coreProject = ReadRepoFile("PointOfSale.Core/PointOfSale.Core.csproj");
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.DoesNotContain("Fawry", coreProject, StringComparison.OrdinalIgnoreCase);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("Fawry", source, StringComparison.Ordinal);
            Assert.DoesNotContain("fawrystaging", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("atfawry", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SourceGuards_MustKeepFoundationFreeOfPaymentExecutionObjects()
    {
        var foundationSources = new[]
        {
            "PointOfSale.API/Configuration/FawryOptions.cs",
            "PointOfSale.API/Services/Payments/Fawry/FawryFoundationModels.cs",
            "PointOfSale.API/Services/Payments/Fawry/FawryFoundationService.cs",
            "PointOfSale.API/Services/Payments/Fawry/FawryFoundationServiceCollectionExtensions.cs",
            "PointOfSale.API/HealthChecks/FawryFoundationHealthCheck.cs"
        }.Select(ReadRepoFile);

        foreach (var source in foundationSources)
        {
            Assert.DoesNotContain("InitiateAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("HandleWebhookAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PaymentRuntimeOrchestrator", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PaymentLifecycleService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
        }
    }

    private static FawryOptions BindOptions(Dictionary<string, string?> values)
    {
        var configuration = BuildConfiguration(values);
        var options = new FawryOptions();
        configuration.GetSection(FawryOptions.SectionName).Bind(options);
        return options;
    }

    private static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["CommerceHub:Payments:Fawry:Enabled"] = "true",
        ["CommerceHub:Payments:Fawry:MerchantCode"] = "fawry_merchant_foundation",
        ["CommerceHub:Payments:Fawry:ApiKey"] = "fawry_api_key_foundation",
        ["CommerceHub:Payments:Fawry:Secret"] = "fawry_secure_key_foundation",
        ["CommerceHub:Payments:Fawry:Environment"] = "Sandbox",
        ["CommerceHub:Payments:Fawry:SandboxMode"] = "true",
        ["CommerceHub:Payments:Fawry:Callback:Configured"] = "true",
        ["CommerceHub:Payments:Fawry:Callback:CallbackUrl"] = "https://pos.example.com/api/webhooks/fawry/callback",
        ["CommerceHub:Payments:Fawry:Callback:WebhookUrl"] = "https://pos.example.com/api/webhooks/fawry",
        ["CommerceHub:Payments:Fawry:FeatureFlags:Enabled"] = "true",
        ["CommerceHub:Payments:Fawry:FeatureFlags:SandboxEnabled"] = "true"
    };

    private static FawryOptions ValidOptions() => new()
    {
        Enabled = true,
        MerchantCode = "fawry_merchant_foundation",
        ApiKey = "fawry_api_key_foundation",
        Secret = "fawry_secure_key_foundation",
        Environment = FawryEnvironment.Sandbox,
        SandboxMode = true,
        Callback = ValidCallback(),
        FeatureFlags = new FawryFeatureFlags
        {
            Enabled = true,
            SandboxEnabled = true
        }
    };

    private static FawryCallbackConfiguration ValidCallback() => new()
    {
        Configured = true,
        CallbackUrl = new Uri("https://pos.example.com/api/webhooks/fawry/callback"),
        WebhookUrl = new Uri("https://pos.example.com/api/webhooks/fawry")
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
