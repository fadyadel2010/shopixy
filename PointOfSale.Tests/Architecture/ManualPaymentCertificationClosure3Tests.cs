using Microsoft.EntityFrameworkCore;
using Moq;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentCertificationClosure3Tests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PaymentIntentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime CreatedAtUtc = DateTime.Parse("2026-07-16T10:00:00Z").ToUniversalTime();

    [Fact]
    public async Task PendingPaymentExpires_ThroughLifecycleAuthority()
    {
        await using var dbContext = CreateDbContext();
        dbContext.PaymentIntents.Add(CreateManualIntent(
            PaymentIntentStatus.AwaitingVerification,
            CreatedAtUtc.AddMinutes(30)));
        await dbContext.SaveChangesAsync();

        var lifecycle = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        lifecycle
            .Setup(service => service.MarkPaymentExpiredAsync(
                TenantId,
                OrderId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentTransitionResult.Succeeded(PaymentStatus.Expired));

        var authority = new ManualPaymentExpirationAuthority(dbContext, lifecycle.Object);

        await authority.ExpireElapsedPendingPaymentsAsync(
            TenantId,
            new DateTimeOffset(CreatedAtUtc.AddHours(1), TimeSpan.Zero));

        lifecycle.Verify(service => service.MarkPaymentExpiredAsync(
            TenantId,
            OrderId,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApprovalAfterExpiry_IsRejectedBeforeRuntimeAdmission()
    {
        await using var dbContext = CreateDbContext();
        var intent = CreateManualIntent(
            PaymentIntentStatus.AwaitingVerification,
            CreatedAtUtc.AddMinutes(30));

        var lifecycle = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        lifecycle
            .Setup(service => service.MarkPaymentExpiredAsync(
                TenantId,
                OrderId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentTransitionResult.Succeeded(PaymentStatus.Expired));

        var authority = new ManualPaymentExpirationAuthority(dbContext, lifecycle.Object);

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            authority.EnsureReviewAdmissionAsync(
                intent,
                new DateTimeOffset(CreatedAtUtc.AddHours(1), TimeSpan.Zero)));

        Assert.Equal("MANUAL_PAYMENT_EXPIRED", exception.Code);
        lifecycle.Verify(service => service.MarkPaymentExpiredAsync(
            TenantId,
            OrderId,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MissingExpiration_IsRejectedWithoutLifecycleMutation()
    {
        await using var dbContext = CreateDbContext();
        var intent = CreateManualIntent(PaymentIntentStatus.AwaitingVerification, expiresAtUtc: null);
        var lifecycle = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        var authority = new ManualPaymentExpirationAuthority(dbContext, lifecycle.Object);

        var exception = await Assert.ThrowsAsync<ManualPaymentApplicationException>(() =>
            authority.EnsureReviewAdmissionAsync(
                intent,
                new DateTimeOffset(CreatedAtUtc.AddMinutes(10), TimeSpan.Zero)));

        Assert.Equal("MANUAL_PAYMENT_EXPIRATION_MISSING", exception.Code);
        lifecycle.Verify(service => service.MarkPaymentExpiredAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ExpirationAdmission_MustPrecedeRuntimeAndReviewListPresentation()
    {
        var application = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");
        var admin = ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs");

        Assert.Contains("public interface IManualPaymentExpirationAuthority", application, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentExpirationAuthority", application, StringComparison.Ordinal);
        Assert.Contains("_expirationAuthority.EnsureReviewAdmissionAsync", application, StringComparison.Ordinal);
        var reviewApplication = application[
            application.IndexOf("public sealed class ManualPaymentReviewApplicationService", StringComparison.Ordinal)..];
        Assert.True(
            reviewApplication.IndexOf("_expirationAuthority.EnsureReviewAdmissionAsync", StringComparison.Ordinal) <
            reviewApplication.IndexOf("_runtimeBridge.ExecuteEvidenceAsync", StringComparison.Ordinal));

        Assert.Contains("_manualPaymentExpirationAuthority.ExpireElapsedPendingPaymentsAsync", admin, StringComparison.Ordinal);
        Assert.Contains("pi.ExpiresAtUtc.HasValue", admin, StringComparison.Ordinal);
        Assert.Contains("pi.ExpiresAtUtc.Value > observedAtUtc.UtcDateTime", admin, StringComparison.Ordinal);
    }

    [Fact]
    public void LifecycleMutation_MustEnforceExpirationAuthority()
    {
        var lifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var stateMachine = ReadRepoFile("PointOfSale.API/Services/Payments/ManualWalletStateMachine.cs");

        Assert.Contains("ValidateManualWalletExpirationAuthority", lifecycle, StringComparison.Ordinal);
        Assert.Contains("MANUAL_WALLET_EXPIRATION_MISSING", lifecycle, StringComparison.Ordinal);
        Assert.Contains("MANUAL_WALLET_EXPIRATION_INVALID", lifecycle, StringComparison.Ordinal);
        Assert.Contains("MANUAL_WALLET_CLOCK_INCONSISTENT", lifecycle, StringComparison.Ordinal);
        Assert.Contains("MANUAL_WALLET_EXPIRED", lifecycle, StringComparison.Ordinal);
        Assert.Contains("request.Source == PaymentLifecycleSource.PaymentReviewApproval", lifecycle, StringComparison.Ordinal);
        Assert.Contains("request.Source == PaymentLifecycleSource.PaymentReviewRejection", lifecycle, StringComparison.Ordinal);
        Assert.Contains("next == PaymentIntentStatus.Expired", stateMachine, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveRuntimeBridgeAdaptersAndEvidenceAdapters()
    {
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var bridge = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs");
        var providerAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentProviderAdapter.cs");
        var evidenceAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentEvidenceAdapter.cs");

        Assert.DoesNotContain("ManualPaymentExpirationAuthority", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPaymentExpirationAuthority", providerAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPaymentExpirationAuthority", evidenceAdapter, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("ManualPaymentExpirationAuthority", source, StringComparison.Ordinal);
            Assert.DoesNotContain("MANUAL_WALLET_EXPIRED", source, StringComparison.Ordinal);
        }
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static PaymentIntent CreateManualIntent(
        PaymentIntentStatus status,
        DateTime? expiresAtUtc) =>
        new()
        {
            Id = PaymentIntentId,
            TenantId = TenantId,
            OrderId = OrderId,
            Provider = PaymentProvider.ManualWallet,
            PaymentMethod = PaymentMethod.BankTransfer,
            Status = status,
            Amount = 100m,
            Currency = "EGP",
            PaymentReference = "PAY-MANUAL-EXPIRY",
            IdempotencyKey = "manual-expiry",
            CreatedAt = CreatedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            ProofImageUrl = $"tenants/{TenantId:D}/manual-wallet-proofs/proof.png"
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
