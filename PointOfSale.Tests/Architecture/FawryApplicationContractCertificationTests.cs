using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryApplicationContractCertificationTests
{
    [Fact]
    public async Task ApplicationBoundary_MustResolveTenantDelegateOnceAndProjectExecutionResult()
    {
        var resolver = new Mock<IFawryTenantConfigurationResolver>();
        resolver
            .Setup(value => value.ResolveCredentialsAsync(
                TenantId,
                FawryOperationalCapability.PaymentExecution,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantCredentials());
        var adapter = new ApplicationEvidenceAdapter();
        var bridge = new FawryRuntimeBridge(adapter, new PaymentRuntimeOrchestrator());
        var service = new FawryRuntimeApplicationService(
            resolver.Object,
            bridge,
            NullLogger<FawryRuntimeApplicationService>.Instance);

        var result = await service.CreatePaymentIntentAsync(ApplicationRequest());

        Assert.Equal(1, adapter.Calls);
        Assert.Equal("tenant-merchant-code", adapter.Credentials?.MerchantCode);
        Assert.Equal("tenant-api-key", adapter.Credentials?.ApiKey);
        Assert.Equal("tenant-secure-key-123", adapter.Credentials?.SecureKey);
        Assert.Equal(PaymentActionType.ReferencePayment, adapter.Action?.Type);
        Assert.Equal(TenantId.ToString("D"), adapter.Action?.Metadata.Values["tenantId"]);
        Assert.Equal("01000000000", adapter.Action?.Metadata.Values[FawryRequestMetadataKeys.CustomerMobile]);
        Assert.Equal("buyer@example.test", adapter.Action?.Metadata.Values[FawryRequestMetadataKeys.CustomerEmail]);
        Assert.Equal("en-gb", adapter.Action?.Metadata.Values[FawryRequestMetadataKeys.Language]);
        Assert.Equal("PayAtFawry", adapter.Action?.Metadata.Values[FawryRequestMetadataKeys.PaymentMethod]);
        Assert.Equal("reference-123", result.ReferenceCode);
        Assert.Equal("UNPAID", result.ProviderStatus);
        Assert.Equal("https://provider.example.test/reference-123", result.PaymentUrl);
        Assert.Equal(DateTimeOffset.Parse("2026-07-16T12:00:00Z", CultureInfo.InvariantCulture), result.ExpiresAtUtc);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
        Assert.True(FawryRuntimeOutcomeValidator.IsExpectedReferencePreparation(result.RuntimeResult));
    }

    [Fact]
    public async Task TenantResolution_MustReturnOnlyCurrentTenantCredentialsWithoutGlobalFallback()
    {
        await using var dbContext = CreateDbContext();
        var protection = new EphemeralDataProtectionProvider();
        var paymentMethod = CreatePaymentMethod(protection);
        dbContext.TenantPaymentMethods.Add(paymentMethod);
        await dbContext.SaveChangesAsync();
        var globalOptions = EnabledOptions() with
        {
            MerchantCode = "different-global-merchant",
            ApiKey = "different-global-api",
            Secret = "different-global-secret"
        };

        var credentials = await CreateResolver(dbContext, protection, globalOptions)
            .ResolveCredentialsAsync(TenantId, FawryOperationalCapability.PaymentExecution);

        Assert.Equal(TenantId, credentials.TenantId);
        Assert.Equal(paymentMethod.Id, credentials.TenantPaymentMethodId);
        Assert.Equal("tenant-merchant-code", credentials.MerchantCode);
        Assert.Equal("tenant-api-key", credentials.ApiKey);
        Assert.Equal("tenant-secure-key-123", credentials.SecureKey);
        Assert.True(credentials.IsSandbox);
    }

    [Theory]
    [InlineData(false, false, true, "FAWRY_PROVIDER_DISABLED")]
    [InlineData(true, true, true, "FAWRY_CONFIGURATION_DELETED")]
    [InlineData(true, false, false, "FAWRY_CAPABILITY_DISABLED")]
    public async Task TenantResolution_MustFailClosedForDisabledDeletedOrCapabilityDisabledConfiguration(
        bool isEnabled,
        bool isDeleted,
        bool supportsOnline,
        string expectedCode)
    {
        await using var dbContext = CreateDbContext();
        var protection = new EphemeralDataProtectionProvider();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(
            protection,
            isEnabled: isEnabled,
            isDeleted: isDeleted,
            supportsOnline: supportsOnline));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<FawryApplicationException>(() =>
            CreateResolver(dbContext, protection)
                .ResolveCredentialsAsync(TenantId, FawryOperationalCapability.PaymentExecution));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task TenantResolution_MustFailClosedForMissingConfiguration()
    {
        await using var dbContext = CreateDbContext();
        var protection = new EphemeralDataProtectionProvider();

        var exception = await Assert.ThrowsAsync<FawryApplicationException>(() =>
            CreateResolver(dbContext, protection)
                .ResolveCredentialsAsync(TenantId, FawryOperationalCapability.PaymentExecution));

        Assert.Equal("FAWRY_CONFIGURATION_MISSING", exception.Code);
    }

    [Fact]
    public async Task TenantResolution_MustFailClosedForEnvironmentMismatch()
    {
        await using var dbContext = CreateDbContext();
        var protection = new EphemeralDataProtectionProvider();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(protection, isSandbox: false));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<FawryApplicationException>(() =>
            CreateResolver(dbContext, protection)
                .ResolveCredentialsAsync(TenantId, FawryOperationalCapability.PaymentExecution));

        Assert.Equal("FAWRY_ENVIRONMENT_MISMATCH", exception.Code);
    }

    [Theory]
    [InlineData("short", "tenant-api-key", "tenant-secure-key-123")]
    [InlineData("tenant-merchant-code", "short", "tenant-secure-key-123")]
    [InlineData("tenant-merchant-code", "tenant-api-key", "short")]
    public async Task TenantResolution_MustFailClosedForInvalidCredentialShape(
        string merchantCode,
        string apiKey,
        string secureKey)
    {
        await using var dbContext = CreateDbContext();
        var protection = new EphemeralDataProtectionProvider();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(
            protection,
            merchantCode: merchantCode,
            apiKey: apiKey,
            secureKey: secureKey));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<FawryApplicationException>(() =>
            CreateResolver(dbContext, protection)
                .ResolveCredentialsAsync(TenantId, FawryOperationalCapability.PaymentExecution));

        Assert.Equal("FAWRY_CREDENTIALS_INVALID", exception.Code);
    }

    [Fact]
    public async Task CapabilityValidation_MustStopBeforeTenantLookupAndBridgeDelegation()
    {
        await using var dbContext = CreateDbContext();
        var protection = new EphemeralDataProtectionProvider();
        var options = EnabledOptions() with
        {
            FeatureFlags = EnabledOptions().FeatureFlags with { PaymentExecutionEnabled = false }
        };
        var resolver = CreateResolver(dbContext, protection, options);
        var bridge = new Mock<IFawryRuntimeBridge>();
        var service = new FawryRuntimeApplicationService(
            resolver,
            bridge.Object,
            NullLogger<FawryRuntimeApplicationService>.Instance);

        var exception = await Assert.ThrowsAsync<FawryApplicationException>(() =>
            service.CreatePaymentIntentAsync(ApplicationRequest()));

        Assert.Equal("FAWRY_CAPABILITY_DISABLED", exception.Code);
        bridge.Verify(
            value => value.ExecuteAsync(It.IsAny<FawryExecutionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FailureMapping_MustMapCanonicalProviderFailureWithoutLeakingCredentials()
    {
        var resolver = new Mock<IFawryTenantConfigurationResolver>();
        resolver
            .Setup(value => value.ResolveCredentialsAsync(
                TenantId,
                FawryOperationalCapability.PaymentExecution,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantCredentials());
        var error = CanonicalProviderError.Create(
            "fawry",
            CanonicalProviderErrorType.ProviderUnavailable,
            "FAWRY_TIMEOUT",
            "Fawry is temporarily unavailable.");
        var service = new FawryRuntimeApplicationService(
            resolver.Object,
            new FawryRuntimeBridge(new ApplicationEvidenceAdapter(error), new PaymentRuntimeOrchestrator()),
            NullLogger<FawryRuntimeApplicationService>.Instance);

        var exception = await Assert.ThrowsAsync<FawryApplicationException>(() =>
            service.CreatePaymentIntentAsync(ApplicationRequest()));

        Assert.Equal("FAWRY_PROVIDER_UNAVAILABLE", exception.Code);
        Assert.Equal("FAWRY_TIMEOUT", exception.ProviderCode);
        Assert.True(exception.Retryable);
        var publicFailure = string.Join('|', exception.Code, exception.Message, exception.ProviderCode);
        Assert.DoesNotContain("tenant-secure-key-123", publicFailure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeFailure_MustMapToApplicationFailureWithoutProjection()
    {
        var resolver = new Mock<IFawryTenantConfigurationResolver>();
        resolver
            .Setup(value => value.ResolveCredentialsAsync(
                TenantId,
                FawryOperationalCapability.PaymentExecution,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantCredentials());
        var bridge = new Mock<IFawryRuntimeBridge>();
        bridge
            .Setup(value => value.ExecuteAsync(It.IsAny<FawryExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FawryExecutionRequest request, CancellationToken _) =>
                FawryExecutionResultFactory.Create(
                    CreateEvidence(request.Action),
                    new RuntimeResult()));
        var service = new FawryRuntimeApplicationService(
            resolver.Object,
            bridge.Object,
            NullLogger<FawryRuntimeApplicationService>.Instance);

        var exception = await Assert.ThrowsAsync<FawryApplicationException>(() =>
            service.CreatePaymentIntentAsync(ApplicationRequest()));

        Assert.Equal("FAWRY_RUNTIME_OUTCOME_REJECTED", exception.Code);
    }

    [Fact]
    public void DependencyInjection_MustRegisterOneScopedApplicationBoundary()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();

        services.AddFawryApplicationContract();

        Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(IFawryRuntimeApplicationService)));
        Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(IFawryTenantConfigurationResolver)));
        Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(IFawryRuntimeBridge)));
        Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(IFawryPaymentAdapter)));
        Assert.Equal(
            ServiceLifetime.Scoped,
            services.Single(descriptor => descriptor.ServiceType == typeof(IFawryRuntimeApplicationService)).Lifetime);
    }

    [Fact]
    public void DependencyInjection_MustRejectDuplicateApplicationContractRegistration()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddFawryApplicationContract();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddFawryApplicationContract());

        Assert.Contains("duplicate registration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackwardCompatibilityAndArchitectureIsolation_MustPreserveFrozenBoundaries()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var application = ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryRuntimeApplicationService.cs");
        var resolver = ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryTenantConfigurationResolver.cs");
        var bridge = ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryRuntimeBridge.cs");
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/Adapters/FawryPaymentAdapter.cs");
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.Contains("public interface IFawryRuntimeApplicationService", application, StringComparison.Ordinal);
        Assert.Contains("ReferenceCode", application, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc", application, StringComparison.Ordinal);
        Assert.Contains("ResolveCredentialsAsync", application, StringComparison.Ordinal);
        Assert.Contains("_runtimeBridge.ExecuteAsync", application, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethods", application, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", application, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", application, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", application, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IFawryWebhookEvidenceAdapter", application, StringComparison.Ordinal);
        Assert.DoesNotContain("Replay", application, StringComparison.Ordinal);
        Assert.DoesNotContain("AdvancedOperations", application, StringComparison.Ordinal);
        Assert.DoesNotContain("PointOfSale.API.DTOs", application, StringComparison.Ordinal);
        Assert.DoesNotContain("Controllers", application, StringComparison.Ordinal);

        Assert.Contains("TenantPaymentMethods", resolver, StringComparison.Ordinal);
        Assert.Contains("IgnoreQueryFilters()", resolver, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodConfigurationValidator.Validate", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("options.MerchantCode", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("options.ApiKey", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("options.Secret", resolver, StringComparison.Ordinal);

        Assert.Equal(
            File.ReadAllText(Path.Combine(root, "PointOfSale.API", "Services", "Payments", "Fawry", "FawryRuntimeBridge.cs")),
            bridge);
        Assert.Equal(
            File.ReadAllText(Path.Combine(root, "PointOfSale.API", "Services", "Payments", "Adapters", "FawryPaymentAdapter.cs")),
            adapter);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("Fawry", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("atfawry", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static FawryTenantConfigurationResolver CreateResolver(
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        FawryOptions? options = null)
    {
        return new FawryTenantConfigurationResolver(
            dbContext,
            dataProtectionProvider,
            Options.Create(options ?? EnabledOptions()),
            NullLogger<FawryTenantConfigurationResolver>.Instance);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static TenantPaymentMethod CreatePaymentMethod(
        IDataProtectionProvider protection,
        bool isEnabled = true,
        bool isDeleted = false,
        bool supportsOnline = true,
        bool isSandbox = true,
        string merchantCode = "tenant-merchant-code",
        string apiKey = "tenant-api-key",
        string secureKey = "tenant-secure-key-123")
    {
        var configuration = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["merchantCode"] = merchantCode,
            ["apiKey"] = apiKey,
            ["webhookSecret"] = secureKey
        });

        return new TenantPaymentMethod
        {
            Id = TenantPaymentMethodId,
            TenantId = TenantId,
            Code = "FAWRY",
            DisplayName = "Fawry",
            IsEnabled = isEnabled,
            SupportsOnline = supportsOnline,
            IsSandbox = isSandbox,
            IsDeleted = isDeleted,
            ConfigEncrypted = protection.CreateProtector("PaymentMethodSecrets").Protect(configuration),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static FawryOptions EnabledOptions() => new()
    {
        Enabled = true,
        MerchantCode = "unused-global-merchant",
        ApiKey = "unused-global-api",
        Secret = "unused-global-secret",
        Environment = FawryEnvironment.Sandbox,
        SandboxMode = true,
        FeatureFlags = new FawryFeatureFlags
        {
            Enabled = true,
            SandboxEnabled = true,
            CredentialValidationEnabled = true,
            EnvironmentValidationEnabled = true,
            PaymentExecutionEnabled = true
        },
        Capabilities = new FawryCapabilityModel
        {
            SupportsOfficialHttpsApi = true,
            SupportsSha256Signature = true,
            SupportsTenantScopedCredentials = true,
            SupportsSandboxEnvironment = true,
            PaymentExecutionCertified = true,
            ProviderAdapterCertified = true
        }
    };

    private static FawryTenantRuntimeCredentials TenantCredentials() => new(
        TenantId,
        TenantPaymentMethodId,
        "tenant-merchant-code",
        "tenant-api-key",
        "tenant-secure-key-123",
        true);

    private static FawryRuntimePaymentIntentApplicationRequest ApplicationRequest() => new(
        TenantId,
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        "ORDER-123",
        100m,
        "EGP",
        "merchant-reference-123",
        "idempotency-123",
        "correlation-123",
        DateTime.Parse("2026-07-15T10:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
        DateTimeOffset.Parse("2026-07-15T10:01:00Z", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-07-16T12:00:00Z", CultureInfo.InvariantCulture),
        CustomerReference: "customer-123",
        CustomerName: "Buyer",
        CustomerMobile: "01000000000",
        CustomerEmail: "buyer@example.test",
        ReturnUrl: "https://store.example.test/checkout/success",
        OrderWebhookUrl: "https://api.example.test/webhooks/fawry/order",
        Language: "en-gb");

    private static ProviderEvidence CreateEvidence(PaymentAction action)
    {
        return ProviderEvidenceFactory.Create(
            new EvidenceId(Guid.Parse("44444444-4444-5444-8444-444444444444")),
            action.Id,
            EvidenceType.ProviderNotification,
            EvidenceStatus.Recorded,
            new EvidenceReference("reference-123"),
            new EvidenceSource("fawry"),
            new EvidenceTimestamp(action.LastUpdatedUtc.AddMinutes(1)),
            new EvidenceHash("fawry-application-contract-evidence-hash"),
            new ProviderPayloadReference("fawry:charge:reference-123"),
            action.CorrelationId,
            new EvidenceMetadata(new Dictionary<string, string>
            {
                ["provider_reference"] = "reference-123",
                ["reference_number"] = "reference-123",
                ["provider_status"] = "UNPAID",
                ["canonical_status"] = "AwaitingCustomer",
                ["payment_url"] = "https://provider.example.test/reference-123",
                ["expiration_time"] = "2026-07-16T12:00:00Z"
            }));
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

    private sealed class ApplicationEvidenceAdapter : IFawryPaymentAdapter
    {
        private readonly CanonicalProviderError? _error;

        public ApplicationEvidenceAdapter(CanonicalProviderError? error = null)
        {
            _error = error;
        }

        public int Calls { get; private set; }

        public PaymentAction? Action { get; private set; }

        public FawryPaymentCredentials? Credentials { get; private set; }

        public Task<FawryPaymentAdapterResult> InitializePaymentAsync(
            PaymentAction action,
            FawryPaymentCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Action = action;
            Credentials = credentials;
            return Task.FromResult(_error is null
                ? FawryPaymentAdapterResult.Success(CreateEvidence(action), null!, null!)
                : FawryPaymentAdapterResult.Failed(_error));
        }
    }

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantPaymentMethodId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
}
