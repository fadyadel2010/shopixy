using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentCertificationClosure2Tests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PaymentMethodId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task CapabilityAdmission_WhenCapabilityEnabled_AdmitsCurrentTenant()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod());
        await dbContext.SaveChangesAsync();

        var admission = await CreateAuthority(dbContext, EnabledOptions())
            .AdmitAsync(TenantId, ManualPaymentOperationalCapability.ProofUpload);

        Assert.Equal(TenantId, admission.TenantId);
        Assert.Equal(PaymentMethodId, admission.TenantPaymentMethodId);
        Assert.Equal(ManualPaymentChannel.BankTransfer, admission.Channel);
    }

    [Fact]
    public async Task CapabilityAdmission_MustUseCurrentTenantOnlyWithoutGlobalFallback()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(id: Guid.NewGuid(), tenantId: OtherTenantId));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            CreateAuthority(dbContext, EnabledOptions()).AdmitAsync(TenantId, ManualPaymentOperationalCapability.ProofUpload));

        Assert.Equal("MANUAL_PAYMENT_CONFIGURATION_MISSING", exception.Code);
    }

    [Fact]
    public async Task CapabilityAdmission_MustRejectDisabledProviderBeforeExecution()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(isEnabled: false));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            CreateAuthority(dbContext, EnabledOptions()).AdmitAsync(TenantId, ManualPaymentOperationalCapability.ProofUpload));

        Assert.Equal("MANUAL_PAYMENT_PROVIDER_DISABLED", exception.Code);
    }

    [Fact]
    public async Task CapabilityAdmission_MustRejectDisabledCapabilityBeforeExecution()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(supportsOffline: false));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            CreateAuthority(dbContext, EnabledOptions()).AdmitAsync(TenantId, ManualPaymentOperationalCapability.ProofUpload));

        Assert.Equal("MANUAL_PAYMENT_CAPABILITY_DISABLED", exception.Code);
    }

    [Fact]
    public async Task CapabilityAdmission_MustRejectDisabledCertificationGateBeforeExecution()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod());
        await dbContext.SaveChangesAsync();
        var options = EnabledOptions() with
        {
            Capabilities = EnabledOptions().Capabilities with { EvidenceAdapterCertified = false }
        };

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            CreateAuthority(dbContext, options).AdmitAsync(TenantId, ManualPaymentOperationalCapability.ProofUpload));

        Assert.Equal("MANUAL_PAYMENT_CAPABILITY_DISABLED", exception.Code);
    }

    [Fact]
    public async Task CapabilityAdmission_MustRejectEnvironmentMismatchBeforeExecution()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(isSandbox: false));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            CreateAuthority(dbContext, EnabledOptions()).AdmitAsync(TenantId, ManualPaymentOperationalCapability.ProofUpload));

        Assert.Equal("MANUAL_PAYMENT_ENVIRONMENT_MISMATCH", exception.Code);
    }

    [Fact]
    public async Task CapabilityAdmission_MustRejectUnknownOperationBeforeExecution()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod());
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            CreateAuthority(dbContext, EnabledOptions()).AdmitAsync(TenantId, (ManualPaymentOperationalCapability)999));

        Assert.Equal("MANUAL_PAYMENT_CAPABILITY_DISABLED", exception.Code);
    }

    [Fact]
    public void ControllerIngress_MustUseCanonicalCapabilityAuthority()
    {
        var storefront = ReadRepoFile("PointOfSale.API/Controllers/StorefrontManualWalletController.cs");
        var admin = ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs");

        Assert.Contains("IManualPaymentCapabilityAdmissionAuthority", storefront, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentOperationalCapability.ReferencePaymentInitialization", storefront, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentOperationalCapability.ProofUpload", storefront, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentOperationalCapability.ProofClear", storefront, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentCapabilityAdmissionAuthority.AdmitAsync", storefront, StringComparison.Ordinal);

        Assert.Contains("IManualPaymentCapabilityAdmissionAuthority", admin, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentOperationalCapability.PendingReviewRead", admin, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentOperationalCapability.ProofAccess", admin, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentCapabilityAdmissionAuthority.AdmitAsync", admin, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveRuntimeAndBlockControllerBypass()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var runtimeFiles = Directory.GetFiles(Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"), "*.cs", SearchOption.AllDirectories);
        var controllers = string.Concat(
            ReadRepoFile("PointOfSale.API/Controllers/StorefrontManualWalletController.cs"),
            ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs"),
            ReadRepoFile("PointOfSale.API/Controllers/PaymentLifecycleController.cs"));
        var application = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");

        Assert.Contains("public interface IManualPaymentCapabilityAdmissionAuthority", application, StringComparison.Ordinal);
        Assert.Contains("_capabilityAdmissionAuthority.AdmitAsync", application, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", controllers, StringComparison.Ordinal);
        Assert.DoesNotContain("IManualPaymentRuntimeBridge", controllers, StringComparison.Ordinal);
        Assert.DoesNotContain("IManualPaymentProviderAdapter", controllers, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("ManualPaymentCapabilityAdmissionAuthority", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ManualPaymentOperationalCapability", source, StringComparison.Ordinal);
            Assert.DoesNotContain("manual_payment", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static ManualPaymentTenantConfigurationResolver CreateAuthority(
        ApplicationDbContext dbContext,
        ManualPaymentOptions options) =>
        new(dbContext, Options.Create(options));

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
                ConfigurationValidationEnabled = true,
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
                ApprovalWorkflowCertified = true
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
