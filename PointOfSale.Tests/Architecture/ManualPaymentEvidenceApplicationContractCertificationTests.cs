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

public sealed class ManualPaymentEvidenceApplicationContractCertificationTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PaymentMethodId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task EvidenceApplicationBoundary_MustResolveTenantValidateEvidenceAndDelegateThroughBridge()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod());
        await dbContext.SaveChangesAsync();
        var service = new ManualPaymentEvidenceApplicationService(
            CreateResolver(dbContext),
            new ManualPaymentEvidenceAdapter(),
            new ManualPaymentRuntimeBridge(new ManualPaymentProviderAdapter(), new PaymentRuntimeOrchestrator()),
            Mock.Of<IManualPaymentOperationalTelemetryAuthority>(),
            NullLogger<ManualPaymentEvidenceApplicationService>.Instance);

        var result = await service.AdmitEvidenceAsync(EvidenceRequest());

        Assert.Equal("manual_payment", result.ProviderEvidence.Source.Value);
        Assert.Equal(EvidenceStatus.PendingReview, result.ProviderEvidence.Status);
        Assert.Equal("ManualReview", result.ProviderEvidence.Metadata.Values["canonical_status"]);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
        Assert.Equal(FailureStage.Transaction, result.RuntimeResult.Failure?.FailureStage);
        Assert.Equal(VerificationStatus.Verified, result.RuntimeResult.VerificationResult?.Status);
        Assert.Equal(PaymentStatus.Verified, result.RuntimeResult.TransitionResult?.Session.Status);
        Assert.Equal(nameof(ManualPaymentEvidenceApplicationService), result.ExecutionResult.ExecutionMetadata.Values["application_boundary"]);
        Assert.Equal(PaymentMethodId.ToString("N"), result.ExecutionResult.ExecutionMetadata.Values["tenant_payment_method_id"]);
    }

    [Fact]
    public async Task TenantResolution_MustUseCurrentTenantOnlyWithoutGlobalFallback()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(id: Guid.Parse("44444444-4444-4444-4444-444444444444"), tenantId: OtherTenantId));
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod());
        await dbContext.SaveChangesAsync();

        var configuration = await CreateResolver(dbContext)
            .ResolveAsync(TenantId, ManualPaymentOperationalCapability.EvidenceAdmission, ManualPaymentChannel.BankTransfer);

        Assert.Equal(TenantId, configuration.TenantId);
        Assert.Equal(PaymentMethodId, configuration.TenantPaymentMethodId);
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
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(isEnabled: isEnabled, isDeleted: isDeleted, supportsOffline: supportsOffline));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            CreateResolver(dbContext).ResolveAsync(TenantId, ManualPaymentOperationalCapability.EvidenceAdmission, ManualPaymentChannel.BankTransfer));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task EnvironmentValidation_MustRejectMismatchBeforeAdapterOrBridge()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(isSandbox: false));
        await dbContext.SaveChangesAsync();
        var adapter = new Mock<IManualPaymentEvidenceAdapter>(MockBehavior.Strict);
        var bridge = new Mock<IManualPaymentRuntimeBridge>(MockBehavior.Strict);
        var service = new ManualPaymentEvidenceApplicationService(
            CreateResolver(dbContext),
            adapter.Object,
            bridge.Object,
            Mock.Of<IManualPaymentOperationalTelemetryAuthority>(),
            NullLogger<ManualPaymentEvidenceApplicationService>.Instance);

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            service.AdmitEvidenceAsync(EvidenceRequest()));

        Assert.Equal("MANUAL_PAYMENT_ENVIRONMENT_MISMATCH", exception.Code);
        adapter.Verify(value => value.Translate(It.IsAny<ManualPaymentEvidenceSubmission>()), Times.Never);
        bridge.Verify(value => value.ExecuteEvidenceAsync(It.IsAny<ManualPaymentEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvidenceValidation_MustRejectMissingProofMetadataBeforeBridge()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod());
        await dbContext.SaveChangesAsync();
        var bridge = new Mock<IManualPaymentRuntimeBridge>(MockBehavior.Strict);
        var service = new ManualPaymentEvidenceApplicationService(
            CreateResolver(dbContext),
            new ManualPaymentEvidenceAdapter(),
            bridge.Object,
            Mock.Of<IManualPaymentOperationalTelemetryAuthority>(),
            NullLogger<ManualPaymentEvidenceApplicationService>.Instance);

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            service.AdmitEvidenceAsync(EvidenceRequest(proofMetadata: new ManualPaymentProofMetadata())));

        Assert.Equal("MANUAL_PAYMENT_EVIDENCE_PROOF_MISSING", exception.Code);
        bridge.Verify(value => value.ExecuteEvidenceAsync(It.IsAny<ManualPaymentEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReferenceValidation_MustRejectMissingRequiredReferenceBeforeBridge()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod());
        await dbContext.SaveChangesAsync();
        var bridge = new Mock<IManualPaymentRuntimeBridge>(MockBehavior.Strict);
        var service = new ManualPaymentEvidenceApplicationService(
            CreateResolver(dbContext),
            new ManualPaymentEvidenceAdapter(),
            bridge.Object,
            Mock.Of<IManualPaymentOperationalTelemetryAuthority>(),
            NullLogger<ManualPaymentEvidenceApplicationService>.Instance);

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            service.AdmitEvidenceAsync(EvidenceRequest(referenceNumber: null)));

        Assert.Equal("MANUAL_PAYMENT_EVIDENCE_REFERENCE_MISSING", exception.Code);
        bridge.Verify(value => value.ExecuteEvidenceAsync(It.IsAny<ManualPaymentEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CapabilityValidation_MustRejectDisabledEvidenceCapabilityBeforeAdapterOrBridge()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod());
        await dbContext.SaveChangesAsync();
        var options = EnabledOptions() with { Capabilities = EnabledOptions().Capabilities with { EvidenceAdapterCertified = false } };
        var adapter = new Mock<IManualPaymentEvidenceAdapter>(MockBehavior.Strict);
        var bridge = new Mock<IManualPaymentRuntimeBridge>(MockBehavior.Strict);
        var service = new ManualPaymentEvidenceApplicationService(
            CreateResolver(dbContext, options),
            adapter.Object,
            bridge.Object,
            Mock.Of<IManualPaymentOperationalTelemetryAuthority>(),
            NullLogger<ManualPaymentEvidenceApplicationService>.Instance);

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            service.AdmitEvidenceAsync(EvidenceRequest()));

        Assert.Equal("MANUAL_PAYMENT_CAPABILITY_DISABLED", exception.Code);
        adapter.Verify(value => value.Translate(It.IsAny<ManualPaymentEvidenceSubmission>()), Times.Never);
        bridge.Verify(value => value.ExecuteEvidenceAsync(It.IsAny<ManualPaymentEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FailureMapping_MustMapAdapterConfigurationErrorToStableApplicationFailure()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod());
        await dbContext.SaveChangesAsync();
        var adapter = new Mock<IManualPaymentEvidenceAdapter>();
        adapter
            .Setup(value => value.Translate(It.IsAny<ManualPaymentEvidenceSubmission>()))
            .Returns(ManualPaymentEvidenceAdapterResult.Failed(CanonicalProviderError.Configuration(
                ManualPaymentProviderReference.ProviderName,
                "MANUAL_EVIDENCE_CONFIGURATION_MISMATCH",
                "Configuration mismatch.")));
        var bridge = new Mock<IManualPaymentRuntimeBridge>(MockBehavior.Strict);
        var service = new ManualPaymentEvidenceApplicationService(
            CreateResolver(dbContext),
            adapter.Object,
            bridge.Object,
            Mock.Of<IManualPaymentOperationalTelemetryAuthority>(),
            NullLogger<ManualPaymentEvidenceApplicationService>.Instance);

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            service.AdmitEvidenceAsync(EvidenceRequest()));

        Assert.Equal("MANUAL_PAYMENT_EVIDENCE_CONFIGURATION_INVALID", exception.Code);
        Assert.Equal("MANUAL_EVIDENCE_CONFIGURATION_MISMATCH", exception.ProviderCode);
        bridge.Verify(value => value.ExecuteEvidenceAsync(It.IsAny<ManualPaymentEvidenceExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void DependencyInjection_MustRegisterEvidenceApplicationContract()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));

        services.AddManualPaymentFoundation(BuildConfiguration());

        Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(IManualPaymentEvidenceApplicationService)));
        Assert.Equal(
            ServiceLifetime.Scoped,
            services.Single(descriptor => descriptor.ServiceType == typeof(IManualPaymentEvidenceApplicationService)).Lifetime);
    }

    [Fact]
    public void DtoCompatibilityAndArchitectureGuards_MustPreserveFrozenEvidenceBoundaries()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var application = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");
        var evidenceApplication = application[
            application.IndexOf("public sealed class ManualPaymentEvidenceApplicationService", StringComparison.Ordinal)..
            application.IndexOf("public sealed record ManualPaymentTenantConfiguration", StringComparison.Ordinal)];
        var bridge = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs");
        var evidenceAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentEvidenceAdapter.cs");
        var runtimeFiles = Directory.GetFiles(Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"), "*.cs", SearchOption.AllDirectories);

        Assert.Contains("public interface IManualPaymentEvidenceApplicationService", application, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentEvidenceApplicationRequest", application, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentEvidenceApplicationResult", application, StringComparison.Ordinal);
        Assert.Contains("IManualPaymentCapabilityAdmissionAuthority", application, StringComparison.Ordinal);
        Assert.Contains("_capabilityAdmissionAuthority.AdmitAsync", application, StringComparison.Ordinal);
        Assert.Contains("_evidenceAdapter.Translate", application, StringComparison.Ordinal);
        Assert.Contains("_runtimeBridge.ExecuteEvidenceAsync", application, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentEvidenceExecutionRequest", bridge, StringComparison.Ordinal);
        Assert.Contains("PaymentRuntimeOrchestrator", bridge, StringComparison.Ordinal);
        Assert.Contains("ProviderEvidenceFactory.Create", evidenceAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", evidenceApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", evidenceApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", evidenceApplication, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SaveChanges", evidenceApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", evidenceApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("Webhook", evidenceApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("Callback", evidenceApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("Replay", evidenceApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("Recovery", evidenceApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("Checkpoint", evidenceApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("Approval", evidenceApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("ReviewWorkflow", evidenceApplication, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("ManualPaymentEvidenceApplicationService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ManualPaymentEvidenceAdapter", source, StringComparison.Ordinal);
            Assert.DoesNotContain("manual_payment", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static ManualPaymentTenantConfigurationResolver CreateResolver(ApplicationDbContext dbContext, ManualPaymentOptions? options = null) =>
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

    private static ManualPaymentEvidenceApplicationRequest EvidenceRequest(
        ManualPaymentChannel channel = ManualPaymentChannel.BankTransfer,
        string? referenceNumber = "REF-123",
        string? proofIdentifier = "proof-123",
        ManualPaymentProofMetadata? proofMetadata = null) =>
        new(
            TenantId,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "MANUAL-1001",
            120.50m,
            "EGP",
            "manual-evidence-correlation-1001",
            DateTimeOffset.Parse("2026-07-16T09:55:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-07-16T10:00:00Z", CultureInfo.InvariantCulture),
            channel,
            referenceNumber,
            proofIdentifier,
            proofMetadata ?? new ManualPaymentProofMetadata(new Dictionary<string, string>
            {
                ["content_type"] = "image/png",
                ["size_bytes"] = "4096"
            }),
            DateTimeOffset.Parse("2026-07-16T12:00:00Z", CultureInfo.InvariantCulture),
            "customer note supplied");

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
                ["CommerceHub:Payments:ManualPayment:Capabilities:EvidenceAdapterCertified"] = "true",
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
