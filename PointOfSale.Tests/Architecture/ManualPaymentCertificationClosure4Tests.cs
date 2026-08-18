using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Checkout;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;

namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentCertificationClosure4Tests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PaymentIntentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10);

    [Fact]
    public async Task Recovery_ResumesExistingManualWalletPaymentIntent()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Orders.Add(CreateManualWalletOrder(CheckoutState.PendingPaymentIntent));
        dbContext.PaymentIntents.Add(CreateManualWalletIntent(PaymentIntentStatus.Pending));
        await dbContext.SaveChangesAsync();
        Assert.Equal(1, await CountActiveManualWalletIntentsAsync(dbContext));

        var paymentService = new Mock<IPaymentService>();
        var durableEvidenceAuthority = new Mock<IManualPaymentDurableEvidenceAuthority>(MockBehavior.Strict);
        durableEvidenceAuthority
            .Setup(authority => authority.ValidateReceiptEvidence(
                It.IsAny<PaymentIntent>(),
                ManualPaymentDurableEvidencePurpose.Recovery))
            .Returns(CreateDurableEvidenceFacts());
        paymentService
            .Setup(service => service.ResumeExistingPaymentIntentAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<PaymentProvider>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentIntentResponseDto
            {
                PaymentIntentId = PaymentIntentId,
                OrderId = OrderId,
                Provider = PaymentProvider.ManualWallet.ToString(),
                Status = PaymentIntentStatus.AwaitingVerification.ToString()
            });

        var service = CreateRecoveryService(
            dbContext,
            paymentService.Object,
            durableEvidenceAuthority.Object,
            new RethrowLoggedErrorLogger());
        var order = await dbContext.Orders.IgnoreQueryFilters().SingleAsync(order => order.Id == OrderId);

        var repaired = await InvokeEnsureOrderHasPaymentIntentAsync(service, order);

        Assert.True(repaired);
        Assert.Equal(1, await dbContext.PaymentIntents.IgnoreQueryFilters().CountAsync());
        paymentService.Verify(service => service.ResumeExistingPaymentIntentAsync(
            TenantId,
            OrderId,
            PaymentIntentId,
            PaymentProvider.ManualWallet,
            false,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        paymentService.Verify(service => service.CreatePaymentIntentAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<CreatePaymentIntentRequestDto>(),
            It.IsAny<bool>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        durableEvidenceAuthority.Verify(authority => authority.ValidateReceiptEvidence(
            It.Is<PaymentIntent>(intent => intent.Id == PaymentIntentId),
            ManualPaymentDurableEvidencePurpose.Recovery), Times.Once);
    }

    [Fact]
    public async Task Recovery_WhenManualWalletFactsAreMissing_FailsClosedWithoutCreation()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Orders.Add(CreateManualWalletOrder(CheckoutState.PendingPaymentIntent));
        await dbContext.SaveChangesAsync();
        Assert.Equal(0, await CountActiveManualWalletIntentsAsync(dbContext));

        var paymentService = new Mock<IPaymentService>(MockBehavior.Strict);
        var service = CreateRecoveryService(dbContext, paymentService.Object);
        var order = await dbContext.Orders.IgnoreQueryFilters().SingleAsync(order => order.Id == OrderId);

        var repaired = await InvokeEnsureOrderHasPaymentIntentAsync(service, order);

        Assert.False(repaired);
        Assert.Empty(await dbContext.PaymentIntents.IgnoreQueryFilters().ToListAsync());
        paymentService.Verify(service => service.ResumeExistingPaymentIntentAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<PaymentProvider>(),
            It.IsAny<bool>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        paymentService.Verify(service => service.CreatePaymentIntentAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<CreatePaymentIntentRequestDto>(),
            It.IsAny<bool>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ManualWalletRecoveryPolicy_MustBeResumeOnlyAndFailClosed()
    {
        var recovery = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs");
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");
        var paymentServiceContract = ReadRepoFile("PointOfSale.API/Services/Payments/IPaymentService.cs");

        Assert.Contains("ResumeExistingPaymentIntentAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("IManualPaymentDurableEvidenceAuthority", recovery, StringComparison.Ordinal);
        Assert.Contains("ValidateReceiptEvidence", recovery, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentDurableEvidencePurpose.Recovery", recovery, StringComparison.Ordinal);
        Assert.Contains("MANUAL_WALLET_RECOVERY_FACTS_MISSING", recovery, StringComparison.Ordinal);
        Assert.Contains("MANUAL_WALLET_RECOVERY_AMBIGUOUS", recovery, StringComparison.Ordinal);
        Assert.Contains("MANUAL_WALLET_RECOVERY_CONFLICTING_STATE", recovery, StringComparison.Ordinal);
        Assert.Contains("return intent.Status == PaymentIntentStatus.Pending", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("new CreatePaymentIntentRequestDto { Provider = PaymentProvider.ManualWallet.ToString() }", recovery, StringComparison.Ordinal);

        Assert.Contains("Task<PaymentIntentResponseDto> ResumeExistingPaymentIntentAsync", paymentServiceContract, StringComparison.Ordinal);
        Assert.Contains("EnsurePersistedPaymentIntentReadyAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("InitiateManualPaymentPersistedPaymentIntentAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentRuntimeApplicationService.InitializeReferencePaymentAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentEvidenceApplicationService.AdmitEvidenceAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("_paymentLifecycleService.AdmitPaymentReviewAsync", paymentService, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveRuntimeBridgeAdaptersAndControllerBoundaries()
    {
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var bridge = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs");
        var providerAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentProviderAdapter.cs");
        var evidenceAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentEvidenceAdapter.cs");
        var controllers = string.Concat(
            ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs"),
            ReadRepoFile("PointOfSale.API/Controllers/PaymentsController.cs"),
            ReadRepoFile("PointOfSale.API/Controllers/PaymentLifecycleController.cs"));

        Assert.DoesNotContain("ResumeExistingPaymentIntentAsync", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ResumeExistingPaymentIntentAsync", providerAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ResumeExistingPaymentIntentAsync", evidenceAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ResumeExistingPaymentIntentAsync", controllers, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("ResumeExistingPaymentIntentAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("MANUAL_WALLET_RECOVERY", source, StringComparison.Ordinal);
        }
    }

    private static CheckoutPaymentIntentRecoveryService CreateRecoveryService(
        ApplicationDbContext dbContext,
        IPaymentService paymentService,
        IManualPaymentDurableEvidenceAuthority? durableEvidenceAuthority = null,
        ILogger<CheckoutPaymentIntentRecoveryService>? logger = null) =>
        new(
            dbContext,
            paymentService,
            Mock.Of<IPaymentGateway>(),
            Mock.Of<ICheckoutSessionTokenService>(),
            Mock.Of<IProductVariantStockRepository>(),
            Mock.Of<ICouponValidationService>(),
            Mock.Of<IEventPublisher>(),
            durableEvidenceAuthority ?? Mock.Of<IManualPaymentDurableEvidenceAuthority>(),
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CheckoutPaymentIntentRecoveryService>.Instance);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static Order CreateManualWalletOrder(CheckoutState checkoutState)
    {
        var order = new Order
        {
            Id = OrderId,
            TenantId = TenantId,
            OrderNumber = "ORD-MANUAL-RECOVERY",
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            PaymentMethod = PaymentMethod.BankTransfer,
            PaymentMethodCode = "MANUALWALLET",
            Total = 100m,
            Currency = "EGP",
            CreatedAt = CreatedAtUtc
        };
        order.AcceptPaymentStatusChange();

        if (checkoutState == CheckoutState.PaymentPending)
        {
            order.TransitionCheckoutState(CheckoutState.PaymentPending);
        }

        return order;
    }

    private static PaymentIntent CreateManualWalletIntent(PaymentIntentStatus status) =>
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
            PaymentReference = "PAY-MANUAL-RECOVERY",
            IdempotencyKey = "storefront:checkout:manual-recovery",
            CreatedAt = CreatedAtUtc,
            WalletType = "BANK_TRANSFER",
            WalletPhoneNumber = "manual-reference",
            ProofImageUrl = $"tenants/{TenantId:D}/manual-wallet-proofs/proof.png",
            ProviderMetadata = "{}"
        };

    private static ManualPaymentDurableEvidenceFacts CreateDurableEvidenceFacts() =>
        new(
            "manual-durable-evidence-v1",
            "MANUAL_PAYMENT_RECEIPT_ADMITTED_TO_REVIEW",
            TenantId,
            OrderId,
            PaymentIntentId,
            "PAY-MANUAL-RECOVERY",
            100m,
            "EGP",
            "bank_transfer",
            "Receipt",
            "PendingReview",
            Guid.NewGuid().ToString("N"),
            new string('a', 64),
            $"manual_payment:evidence:tenants/{TenantId:D}/manual-wallet-proofs/proof.png",
            $"manual_payment:proof:tenants/{TenantId:D}/manual-wallet-proofs/proof.png",
            CreatedAtUtc.AddMinutes(1),
            "manual-config-test",
            "manual-proof-metadata-v1",
            $"tenants/{TenantId:D}/manual-wallet-proofs/proof.png",
            "checkout_manual_wallet",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["proof_identifier"] = $"tenants/{TenantId:D}/manual-wallet-proofs/proof.png",
                ["proof_source"] = "checkout_manual_wallet"
            },
            null,
            null);

    private static Task<int> CountActiveManualWalletIntentsAsync(ApplicationDbContext dbContext) =>
        dbContext.PaymentIntents
            .IgnoreQueryFilters()
            .CountAsync(intent =>
                intent.TenantId == TenantId &&
                intent.OrderId == OrderId &&
                !intent.IsDeleted &&
                intent.Provider == PaymentProvider.ManualWallet &&
                intent.Status == PaymentIntentStatus.Pending);

    private static async Task<bool> InvokeEnsureOrderHasPaymentIntentAsync(
        CheckoutPaymentIntentRecoveryService service,
        Order order)
    {
        var method = typeof(CheckoutPaymentIntentRecoveryService).GetMethod(
            "EnsureOrderHasPaymentIntentAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task<bool>)method.Invoke(
            service,
            [order, DateTime.UtcNow, CancellationToken.None])!;

        return await task;
    }

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

    private sealed class RethrowLoggedErrorLogger : ILogger<CheckoutPaymentIntentRecoveryService>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error && exception is not null)
            {
                throw exception;
            }
        }
    }
}
