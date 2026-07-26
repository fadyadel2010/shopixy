using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentApplicationContractCertificationTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PaymentMethodId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task ApplicationBoundary_MustResolveTenantPrepareRuntimeRequestAndProjectResult()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod());
        await dbContext.SaveChangesAsync();
        var resolver = CreateResolver(dbContext);
        var bridge = new ManualPaymentRuntimeBridge(new ManualPaymentProviderAdapter(), new PaymentRuntimeOrchestrator());
        var service = new ManualPaymentRuntimeApplicationService(
            resolver,
            bridge,
            Mock.Of<IManualPaymentOperationalTelemetryAuthority>(),
            NullLogger<ManualPaymentRuntimeApplicationService>.Instance);

        var result = await service.InitializeReferencePaymentAsync(ApplicationRequest());

        Assert.StartsWith("manual_payment:", result.InstructionIdentifier, StringComparison.Ordinal);
        Assert.Equal(ManualPaymentChannel.BankTransfer, result.Channel);
        Assert.Equal("bank_transfer", result.CanonicalChannel);
        Assert.Equal(DateTimeOffset.Parse("2026-07-16T12:00:00Z", CultureInfo.InvariantCulture), result.ExpiresAtUtc);
        Assert.True(result.ReferenceNumberRequired);
        Assert.True(result.ProofRequired);
        Assert.Equal("manual_payment", result.ProviderEvidence.Source.Value);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
        Assert.True(ManualPaymentRuntimeOutcomeValidator.IsExpectedReferencePaymentInitialization(result.RuntimeResult));
        Assert.Equal(nameof(ManualPaymentRuntimeApplicationService), result.ExecutionResult.ExecutionMetadata.Values["application_boundary"]);
        Assert.Equal(PaymentMethodId.ToString("N"), result.ExecutionResult.ExecutionMetadata.Values["tenant_payment_method_id"]);
    }

    [Fact]
    public async Task TenantResolution_MustUseCurrentTenantConfigurationOnlyWithoutGlobalFallback()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(
            id: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            tenantId: OtherTenantId));
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod());
        await dbContext.SaveChangesAsync();

        var configuration = await CreateResolver(dbContext)
            .ResolveAsync(TenantId, ManualPaymentOperationalCapability.ReferencePaymentInitialization, ManualPaymentChannel.BankTransfer);

        Assert.Equal(TenantId, configuration.TenantId);
        Assert.Equal(PaymentMethodId, configuration.TenantPaymentMethodId);
        Assert.DoesNotContain(OtherTenantId.ToString("N"), configuration.InstructionVersion, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, false, true, "MANUAL_PAYMENT_PROVIDER_DISABLED")]
    [InlineData(true, true, true, "MANUAL_PAYMENT_CONFIGURATION_DELETED")]
    [InlineData(true, false, false, "MANUAL_PAYMENT_CAPABILITY_DISABLED")]
    public async Task TenantResolution_MustRejectDisabledDeletedOrCapabilityDisabledConfiguration(
        bool isEnabled,
        bool isDeleted,
        bool supportsOffline,
        string expectedCode)
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(
            isEnabled: isEnabled,
            isDeleted: isDeleted,
            supportsOffline: supportsOffline));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            CreateResolver(dbContext).ResolveAsync(
                TenantId,
                ManualPaymentOperationalCapability.ReferencePaymentInitialization,
                ManualPaymentChannel.BankTransfer));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task TenantResolution_MustRejectMissingConfiguration()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            CreateResolver(dbContext).ResolveAsync(
                TenantId,
                ManualPaymentOperationalCapability.ReferencePaymentInitialization,
                ManualPaymentChannel.BankTransfer));

        Assert.Equal("MANUAL_PAYMENT_CONFIGURATION_MISSING", exception.Code);
    }

    [Fact]
    public async Task EnvironmentValidation_MustRejectTenantEnvironmentMismatch()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(isSandbox: false));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            CreateResolver(dbContext).ResolveAsync(
                TenantId,
                ManualPaymentOperationalCapability.ReferencePaymentInitialization,
                ManualPaymentChannel.BankTransfer));

        Assert.Equal("MANUAL_PAYMENT_ENVIRONMENT_MISMATCH", exception.Code);
    }

    [Fact]
    public async Task CapabilityValidation_MustRejectUnsupportedChannelBeforeBridgeDelegation()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod());
        await dbContext.SaveChangesAsync();
        var bridge = new Mock<IManualPaymentRuntimeBridge>(MockBehavior.Strict);
        var service = new ManualPaymentRuntimeApplicationService(
            CreateResolver(dbContext),
            bridge.Object,
            Mock.Of<IManualPaymentOperationalTelemetryAuthority>(),
            NullLogger<ManualPaymentRuntimeApplicationService>.Instance);

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            service.InitializeReferencePaymentAsync(ApplicationRequest(channel: ManualPaymentChannel.Instapay)));

        Assert.Equal("MANUAL_PAYMENT_CHANNEL_UNSUPPORTED", exception.Code);
        bridge.Verify(value => value.ExecuteAsync(It.IsAny<ManualPaymentExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FailureMapping_MustMapCanonicalProviderFailureToStableApplicationFailure()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod());
        await dbContext.SaveChangesAsync();
        var error = CanonicalProviderError.Configuration(
            ManualPaymentProviderReference.ProviderName,
            "MANUAL_PROVIDER_CONFIGURATION_INVALID",
            "Manual provider configuration invalid.");
        var bridge = new Mock<IManualPaymentRuntimeBridge>();
        bridge
            .Setup(value => value.ExecuteAsync(It.IsAny<ManualPaymentExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManualPaymentExecutionRequest request, CancellationToken _) =>
                ManualPaymentExecutionResultFactory.Create(
                    ManualPaymentExecutionFactory.ProviderErrorEvidence(request.Action, error),
                    new RuntimeResult(),
                    error,
                    request.Metadata));
        var service = new ManualPaymentRuntimeApplicationService(
            CreateResolver(dbContext),
            bridge.Object,
            Mock.Of<IManualPaymentOperationalTelemetryAuthority>(),
            NullLogger<ManualPaymentRuntimeApplicationService>.Instance);

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            service.InitializeReferencePaymentAsync(ApplicationRequest()));

        Assert.Equal("MANUAL_PAYMENT_PROVIDER_CONFIGURATION_INVALID", exception.Code);
        Assert.Equal("MANUAL_PROVIDER_CONFIGURATION_INVALID", exception.ProviderCode);
        Assert.False(exception.Retryable);
    }

    [Fact]
    public async Task InvalidConfiguration_MustFailClosedBeforeTenantLookup()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod());
        await dbContext.SaveChangesAsync();
        var options = EnabledOptions() with
        {
            Capabilities = EnabledOptions().Capabilities with { RuntimeBridgeCertified = false }
        };

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            CreateResolver(dbContext, options).ResolveAsync(
                TenantId,
                ManualPaymentOperationalCapability.ReferencePaymentInitialization,
                ManualPaymentChannel.BankTransfer));

        Assert.Equal("MANUAL_PAYMENT_CAPABILITY_DISABLED", exception.Code);
    }

    [Fact]
    public void DependencyInjection_MustRegisterApplicationContractWithoutDuplicateProviderComponents()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));

        services.AddManualPaymentFoundation(BuildConfiguration());

        Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(IManualPaymentRuntimeApplicationService)));
        Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(IManualPaymentTenantConfigurationResolver)));
        Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(IManualPaymentRuntimeBridge)));
        Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(IManualPaymentProviderAdapter)));
        Assert.Equal(
            ServiceLifetime.Scoped,
            services.Single(descriptor => descriptor.ServiceType == typeof(IManualPaymentRuntimeApplicationService)).Lifetime);
    }

    [Fact]
    public void DtoCompatibilityAndArchitectureGuards_MustPreserveFrozenBoundaries()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var application = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");
        var applicationContract = application[
            application.IndexOf("public sealed class ManualPaymentRuntimeApplicationService", StringComparison.Ordinal)..
            application.IndexOf("public sealed class ManualPaymentEvidenceApplicationService", StringComparison.Ordinal)];
        var bridge = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs");
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentProviderAdapter.cs");
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.Contains("public interface IManualPaymentRuntimeApplicationService", application, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentRuntimeApplicationRequest", applicationContract, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentRuntimeApplicationResult", applicationContract, StringComparison.Ordinal);
        Assert.Contains("InstructionIdentifier", application, StringComparison.Ordinal);
        Assert.Contains("ReferenceNumberRequired", application, StringComparison.Ordinal);
        Assert.Contains("ProofRequired", application, StringComparison.Ordinal);
        Assert.Contains("IManualPaymentCapabilityAdmissionAuthority", applicationContract, StringComparison.Ordinal);
        Assert.Contains("_capabilityAdmissionAuthority.AdmitAsync", applicationContract, StringComparison.Ordinal);
        Assert.Contains("_runtimeBridge.ExecuteAsync", applicationContract, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", applicationContract, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", applicationContract, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Controller", applicationContract, StringComparison.Ordinal);
        Assert.DoesNotContain("Webhook", applicationContract, StringComparison.Ordinal);
        Assert.DoesNotContain("Callback", applicationContract, StringComparison.Ordinal);
        Assert.DoesNotContain("Replay", applicationContract, StringComparison.Ordinal);
        Assert.DoesNotContain("Recovery", applicationContract, StringComparison.Ordinal);
        Assert.DoesNotContain("Checkpoint", applicationContract, StringComparison.Ordinal);
        Assert.DoesNotContain("Approval", applicationContract, StringComparison.Ordinal);
        Assert.DoesNotContain("ReviewWorkflow", applicationContract, StringComparison.Ordinal);

        Assert.Contains("TenantPaymentMethods", application, StringComparison.Ordinal);
        Assert.Contains("IgnoreQueryFilters()", application, StringComparison.Ordinal);
        Assert.DoesNotContain("options.Merchant", application, StringComparison.Ordinal);
        Assert.DoesNotContain("GlobalOptions", application, StringComparison.Ordinal);
        Assert.DoesNotContain("global fallback", application, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PaymentRuntimeOrchestrator", bridge, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentCanonicalRequest", adapter, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("ManualPaymentRuntimeApplicationService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("manual_payment", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MANUALWALLET", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static ManualPaymentTenantConfigurationResolver CreateResolver(
        ApplicationDbContext dbContext,
        ManualPaymentOptions? options = null) =>
        new(dbContext, Options.Create(options ?? EnabledOptions()));

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static TenantPaymentMethod CreatePaymentMethod(
        Guid? id = null,
        Guid? tenantId = null,
        bool isEnabled = true,
        bool isDeleted = false,
        bool supportsOffline = true,
        bool isSandbox = true) =>
        new()
        {
            Id = id ?? PaymentMethodId,
            TenantId = tenantId ?? TenantId,
            Code = ManualPaymentTenantConfigurationResolver.TenantPaymentMethodCode,
            DisplayName = "Manual Payment",
            IsEnabled = isEnabled,
            IsDeleted = isDeleted,
            SupportsOffline = supportsOffline,
            SupportsOnline = false,
            IsSandbox = isSandbox,
            CreatedAt = DateTime.Parse("2026-07-16T09:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
            UpdatedAtUtc = DateTime.Parse("2026-07-16T09:30:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
            Version = 7
        };

    private static ManualPaymentRuntimeApplicationRequest ApplicationRequest(
        ManualPaymentChannel channel = ManualPaymentChannel.BankTransfer) =>
        new(
            TenantId,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "ORD-1001",
            120.50m,
            "EGP",
            "MANUAL-1001",
            "manual-idempotency-1001",
            "manual-correlation-1001",
            DateTime.Parse("2026-07-16T09:55:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
            DateTimeOffset.Parse("2026-07-16T10:00:00Z", CultureInfo.InvariantCulture),
            channel,
            "customer-1001",
            "buyer@example.test",
            "01000000000");

    private static ManualPaymentOptions EnabledOptions() =>
        new()
        {
            Enabled = true,
            Environment = ManualPaymentEnvironment.Sandbox,
            SandboxMode = true,
            FeatureFlags = new ManualPaymentFeatureFlags
            {
                Enabled = true,
                SandboxEnabled = true,
                ProductionEnabled = false,
                FoundationHealthEnabled = true,
                ConfigurationValidationEnabled = true
            },
            Capabilities = new ManualPaymentCapabilityModel
            {
                ProviderAdapterCertified = true,
                RuntimeBridgeCertified = true,
                EvidenceAdapterCertified = true
            },
            Instructions = new ManualPaymentInstructionConfiguration
            {
                Configured = true,
                DefaultInstructions = "Transfer to the configured account and keep your reference.",
                RequireReferenceNumber = true,
                RequireCustomerProof = true
            },
            Expiration = new ManualPaymentExpirationPolicy
            {
                Configured = true,
                PendingPaymentTtl = TimeSpan.FromHours(2),
                ProofAccessTtl = TimeSpan.FromDays(7)
            },
            Proof = new ManualPaymentProofConfiguration(),
            Channels = new[]
            {
                new ManualPaymentChannelConfiguration
                {
                    Channel = ManualPaymentChannel.BankTransfer,
                    Enabled = true,
                    DisplayName = "Bank Transfer",
                    Instructions = "Use bank transfer reference MANUAL-1001."
                }
            }
        };

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CommerceHub:Payments:ManualPayment:Enabled"] = "true",
                ["CommerceHub:Payments:ManualPayment:FeatureFlags:Enabled"] = "true",
                ["CommerceHub:Payments:ManualPayment:Instructions:Configured"] = "true",
                ["CommerceHub:Payments:ManualPayment:Instructions:DefaultInstructions"] = "Transfer manually.",
                ["CommerceHub:Payments:ManualPayment:Expiration:Configured"] = "true",
                ["CommerceHub:Payments:ManualPayment:Capabilities:ProviderAdapterCertified"] = "true",
                ["CommerceHub:Payments:ManualPayment:Capabilities:RuntimeBridgeCertified"] = "true",
                ["CommerceHub:Payments:ManualPayment:Channels:0:Channel"] = "BankTransfer",
                ["CommerceHub:Payments:ManualPayment:Channels:0:Enabled"] = "true",
                ["CommerceHub:Payments:ManualPayment:Channels:0:DisplayName"] = "Bank Transfer",
                ["CommerceHub:Payments:ManualPayment:Channels:0:Instructions"] = "Transfer manually."
            })
            .Build();

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Solution root was not found.");
    }
}
