using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentCertificationClosure5Tests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PaymentIntentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void RetentionAuthority_AppliesConfiguredProofAccessTtl()
    {
        var authority = CreateAuthority(EnabledOptions(TimeSpan.FromDays(5)));
        var observedAt = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

        var decision = authority.ClassifyAndRetain(
            ManualPaymentRetentionArtifactClass.SubmittedProof,
            observedAt);

        Assert.True(decision.Retain);
        Assert.False(decision.Expire);
        Assert.Equal(ManualPaymentRetentionOperation.Admission, decision.Operation);
        Assert.Equal(observedAt.UtcDateTime.AddDays(5), decision.ExpiresAtUtc);
    }

    [Fact]
    public void RetentionAuthority_WhenPolicyMissingOrInvalid_Rejects()
    {
        var missingPolicy = EnabledOptions(TimeSpan.FromDays(7)) with
        {
            Expiration = new ManualPaymentExpirationPolicy
            {
                Configured = false,
                PendingPaymentTtl = TimeSpan.FromHours(2),
                ProofAccessTtl = TimeSpan.FromDays(7)
            }
        };

        Assert.Throws<ManualPaymentApplicationException>(() =>
            CreateAuthority(missingPolicy).ClassifyAndRetain(
                ManualPaymentRetentionArtifactClass.SubmittedProof,
                DateTimeOffset.UtcNow));

        var invalidPolicy = EnabledOptions(TimeSpan.FromDays(7)) with
        {
            Expiration = new ManualPaymentExpirationPolicy
            {
                Configured = true,
                PendingPaymentTtl = TimeSpan.FromHours(2),
                ProofAccessTtl = TimeSpan.Zero
            }
        };

        Assert.Throws<ManualPaymentApplicationException>(() =>
            CreateAuthority(invalidPolicy).ClassifyAndRetain(
                ManualPaymentRetentionArtifactClass.SubmittedProof,
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RetentionAuthority_ProtectsUnexpiredProofAndExpiresElapsedProof()
    {
        var authority = CreateAuthority(EnabledOptions(TimeSpan.FromDays(7)));
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var protectedIntent = CreatePaymentIntent(PaymentIntentStatus.AwaitingVerification, now.UtcDateTime.AddHours(1));

        var access = authority.EvaluateProofAccess(protectedIntent, now);
        var cleanup = authority.EvaluateCleanup(protectedIntent, now);

        Assert.True(access.Retain);
        Assert.False(access.Expire);
        Assert.True(cleanup.Retain);
        Assert.False(cleanup.Expire);

        var expiredIntent = CreatePaymentIntent(PaymentIntentStatus.AwaitingVerification, now.UtcDateTime.AddMinutes(-1));
        Assert.Throws<ManualPaymentApplicationException>(() => authority.EvaluateProofAccess(expiredIntent, now));

        var expiredCleanup = authority.EvaluateCleanup(expiredIntent, now);
        Assert.False(expiredCleanup.Retain);
        Assert.True(expiredCleanup.Expire);
    }

    [Fact]
    public void RetentionAuthority_WhenArtifactDeletedOrUnsupported_Rejects()
    {
        var authority = CreateAuthority(EnabledOptions(TimeSpan.FromDays(7)));
        var now = DateTimeOffset.UtcNow;

        var deletedIntent = CreatePaymentIntent(PaymentIntentStatus.AwaitingVerification, now.UtcDateTime.AddHours(1));
        deletedIntent.ProofImageDeleted = true;

        Assert.Throws<ManualPaymentApplicationException>(() =>
            authority.EvaluateProofAccess(deletedIntent, now));

        var unsupportedIntent = CreatePaymentIntent(PaymentIntentStatus.Skipped, now.UtcDateTime.AddHours(1));
        Assert.Throws<ManualPaymentApplicationException>(() =>
            authority.EvaluateProofAccess(unsupportedIntent, now));
    }

    [Fact]
    public void LifecycleAndApplication_MustUseExecutableRetentionAuthority()
    {
        var lifecycleInterface = ReadRepoFile("PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs");
        var lifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");
        var reviewApplication = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs");
        var cleanup = ReadRepoFile("PointOfSale.API/Jobs/ManualWalletProofCleanupJob.cs");

        Assert.Contains("IManualPaymentRetentionAuthority", paymentService, StringComparison.Ordinal);
        Assert.Contains("ClassifyAndRetain", paymentService, StringComparison.Ordinal);
        Assert.Contains("IManualPaymentRetentionAuthority", reviewApplication, StringComparison.Ordinal);
        Assert.Contains("ReviewProofRetentionExpiresAtUtc", lifecycleInterface, StringComparison.Ordinal);
        Assert.Contains("MANUAL_WALLET_RETENTION_INVALID", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("now.AddDays(7)", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("now.AddHours(24)", lifecycle, StringComparison.Ordinal);

        Assert.Contains("_manualPaymentRetentionAuthority.EvaluateProofAccess", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ProofImageExpiresAtUtc <=", controller, StringComparison.Ordinal);
        Assert.Contains("retentionAuthority.EvaluateCleanup", cleanup, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveRuntimeAndAdapters()
    {
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var bridge = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs");
        var providerAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentProviderAdapter.cs");
        var evidenceAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentEvidenceAdapter.cs");

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("ManualPaymentRetention", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ProofAccessTtl", source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("ManualPaymentRetention", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPaymentRetention", providerAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPaymentRetention", evidenceAdapter, StringComparison.Ordinal);
    }

    private static IManualPaymentRetentionAuthority CreateAuthority(ManualPaymentOptions options) =>
        new ManualPaymentRetentionAuthority(Options.Create(options));

    private static PaymentIntent CreatePaymentIntent(PaymentIntentStatus status, DateTime expiresAtUtc) => new()
    {
        Id = PaymentIntentId,
        TenantId = TenantId,
        OrderId = OrderId,
        Provider = PaymentProvider.ManualWallet,
        PaymentMethod = PaymentMethod.BankTransfer,
        Status = status,
        Amount = 100m,
        Currency = "EGP",
        PaymentReference = "PAY-MANUAL-RETENTION",
        IdempotencyKey = "manual-retention-test",
        CreatedAt = new DateTime(2026, 7, 16, 10, 0, 0, DateTimeKind.Utc),
        ProofImageUrl = $"tenants/{TenantId:D}/manual-wallet-proofs/proof.png",
        ProofImageExpiresAtUtc = expiresAtUtc,
        ProofImageDeleted = false
    };

    private static ManualPaymentOptions EnabledOptions(TimeSpan proofAccessTtl) => new()
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
            SupportsReceiptProofUpload = true,
            SupportsExpirationPolicy = true
        },
        Instructions = new ManualPaymentInstructionConfiguration
        {
            Configured = true,
            DefaultInstructions = "Transfer manually.",
            RequireCustomerProof = true
        },
        Expiration = new ManualPaymentExpirationPolicy
        {
            Configured = true,
            PendingPaymentTtl = TimeSpan.FromHours(2),
            ProofAccessTtl = proofAccessTtl
        },
        Channels =
        [
            new ManualPaymentChannelConfiguration
            {
                Channel = ManualPaymentChannel.BankTransfer,
                Enabled = true,
                DisplayName = "Bank transfer",
                Instructions = "Transfer manually."
            }
        ]
    };

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
