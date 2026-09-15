using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Payments;

public sealed class PaymentServiceCreatePaymentIntentGuardTests
{
    [Fact]
    public async Task CreatePaymentIntentAsync_WhenOrderStatusIsNotPending_RejectsFinalizedPayment()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId, status: OrderStatus.Confirmed, paymentStatus: PaymentStatus.Pending);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<PaymentOrderInvalidStateException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = "Stripe" },
                promotionCalculationFailed: false,
                requestId: Guid.NewGuid().ToString("N"),
                correlationId: "test-correlation",
                cancellationToken: CancellationToken.None));

        Assert.Equal("PAYMENT_ALREADY_FINALIZED", exception.ErrorCode);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_WhenPaymentStatusIsNotPending_RejectsFinalizedPayment()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId, status: OrderStatus.Pending, paymentStatus: PaymentStatus.Collected);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<PaymentOrderInvalidStateException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = "Stripe" },
                promotionCalculationFailed: false,
                requestId: Guid.NewGuid().ToString("N"),
                correlationId: "test-correlation",
                cancellationToken: CancellationToken.None));

        Assert.Equal("PAYMENT_ALREADY_FINALIZED", exception.ErrorCode);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_ReplayAfterSuccess_RejectsInsteadOfReturningCachedIntent()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId, status: OrderStatus.Pending, paymentStatus: PaymentStatus.Pending);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        const string requestId = "req-replay-after-success";

        var initialResponse = await service.CreatePaymentIntentAsync(
            tenantId,
            order.Id,
            new CreatePaymentIntentRequestDto { Provider = "Stripe" },
            promotionCalculationFailed: false,
            requestId,
            correlationId: "initial-attempt",
            cancellationToken: CancellationToken.None);

        var persistedOrder = await dbContext.Orders.SingleAsync(candidate => candidate.Id == order.Id);
        persistedOrder.PaymentStatus = PaymentStatus.Collected;
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<PaymentOrderInvalidStateException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = "Stripe" },
                promotionCalculationFailed: false,
                requestId,
                correlationId: "replay-attempt",
                cancellationToken: CancellationToken.None));

        Assert.Equal("PAYMENT_ALREADY_FINALIZED", exception.ErrorCode);
        Assert.Equal(1, await dbContext.PaymentIntents.CountAsync());
        Assert.NotEqual(Guid.Empty, initialResponse.PaymentIntentId);
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_WhenProviderIsFawry_RejectsWithoutFinancialAuthority()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId, status: OrderStatus.Pending, paymentStatus: PaymentStatus.Pending);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<PaymentFawryFinancialAuthorityException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = "Fawry" },
                promotionCalculationFailed: false,
                requestId: Guid.NewGuid().ToString("N"),
                correlationId: "fawry-authority",
                cancellationToken: CancellationToken.None));

        Assert.Equal("FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE", exception.ErrorCode);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_WhenFawryOrderRequestsStripe_RejectsWithoutMutation()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Card,
            paymentMethodCode: "FAWRY");
        order.PaymentGateway = "Fawry";
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<PaymentFawryFinancialAuthorityException>(() =>
            CreateService(dbContext).CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = "Stripe" },
                promotionCalculationFailed: false,
                requestId: Guid.NewGuid().ToString("N"),
                correlationId: "fawry-disguised-as-stripe",
                cancellationToken: CancellationToken.None));

        Assert.Equal("FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE", exception.ErrorCode);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_WhenManualWalletConfigMissing_ThrowsManualWalletNotConfigured()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Card,
            paymentMethodCode: "MANUALWALLET");
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<PaymentManualWalletNotConfiguredException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto
                {
                    Provider = "ManualWallet",
                    WalletType = "VodafoneCash",
                    WalletPhoneNumber = "01011112222",
                    ProofFileKey = $"tenants/{tenantId:D}/manual-wallet-proofs/proof.png"
                },
                promotionCalculationFailed: false,
                requestId: Guid.NewGuid().ToString("N"),
                correlationId: "manual-wallet-missing-config",
                cancellationToken: CancellationToken.None));

        Assert.Equal("MANUAL_WALLET_NOT_CONFIGURED", exception.ErrorCode);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_WhenManualWalletConfigExists_Proceeds()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var dataProtector = dataProtectionProvider.CreateProtector("PaymentMethodSecrets");
        var order = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Card,
            paymentMethodCode: "MANUALWALLET");
        dbContext.Orders.Add(order);
        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "MANUALWALLET",
            DisplayName = "Manual Wallet",
            IsEnabled = true,
            SupportsOnline = true,
            SupportsOffline = false,
            ConfigEncrypted = dataProtector.Protect("""{"walletNumber":"01099998888"}""")
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, dataProtector);

        var response = await service.CreatePaymentIntentAsync(
            tenantId,
            order.Id,
            new CreatePaymentIntentRequestDto
            {
                Provider = "ManualWallet",
                WalletType = "VodafoneCash",
                WalletPhoneNumber = "01011112222",
                ProofFileKey = $"tenants/{tenantId:D}/manual-wallet-proofs/proof.png"
            },
            promotionCalculationFailed: false,
            requestId: Guid.NewGuid().ToString("N"),
            correlationId: "manual-wallet-configured",
            cancellationToken: CancellationToken.None);

        Assert.Equal(order.Id, response.OrderId);
        var paymentIntent = await dbContext.PaymentIntents.SingleAsync();
        Assert.Equal(PaymentProvider.ManualWallet, paymentIntent.Provider);
        Assert.Equal(
            $"tenants/{tenantId:D}/manual-wallet-proofs/proof.png",
            paymentIntent.ProofImageUrl);
        Assert.Contains("01099998888", paymentIntent.ProviderMetadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_WhenManualWalletProofIsExternalUrl_RejectsProofReference()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var dataProtector = dataProtectionProvider.CreateProtector("PaymentMethodSecrets");
        var order = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Card,
            paymentMethodCode: "MANUALWALLET");
        dbContext.Orders.Add(order);
        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "MANUALWALLET",
            DisplayName = "Manual Wallet",
            IsEnabled = true,
            SupportsOnline = true,
            SupportsOffline = false,
            ConfigEncrypted = dataProtector.Protect("""{"walletNumber":"01099998888"}""")
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, dataProtector);

        var exception = await Assert.ThrowsAsync<PaymentManualWalletProofException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto
                {
                    Provider = "ManualWallet",
                    WalletType = "VodafoneCash",
                    WalletPhoneNumber = "01011112222",
                    ProofFileKey = $"https://cdn.example.com/tenants/{tenantId:D}/manual-wallet-proofs/proof.png"
                },
                promotionCalculationFailed: false,
                requestId: Guid.NewGuid().ToString("N"),
                correlationId: "manual-wallet-invalid-proof",
                cancellationToken: CancellationToken.None));

        Assert.Equal("INVALID_MANUAL_WALLET_PROOF", exception.ErrorCode);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_WhenOrderCurrencyIsMissing_RejectsRequest()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId, status: OrderStatus.Pending, paymentStatus: PaymentStatus.Pending);
        order.Currency = string.Empty;
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<PaymentException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = "Stripe" },
                promotionCalculationFailed: false,
                requestId: Guid.NewGuid().ToString("N"),
                correlationId: "currency-missing",
                cancellationToken: CancellationToken.None));

        Assert.Equal("ORDER_CURRENCY_NOT_SET", exception.ErrorCode);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_WhenProviderIsFawry_RejectsWithStableAuthorityDespiteCurrencyMismatch()
    {
        // T-09: the Fawry tenant-currency provider check no longer exists because
        // Fawry initiation is fail-closed. The stable authority rejection wins
        // and no intent is created.
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId, status: OrderStatus.Pending, paymentStatus: PaymentStatus.Pending);
        order.Currency = "USD";
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<PaymentFawryFinancialAuthorityException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = "Fawry" },
                promotionCalculationFailed: false,
                requestId: Guid.NewGuid().ToString("N"),
                correlationId: "currency-mismatch",
                cancellationToken: CancellationToken.None));

        Assert.Equal("FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE", exception.ErrorCode);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task ConfirmCodOrderAsync_PersistsDbBackedIdempotency_AndReplaysStoredResponse()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Cash,
            paymentMethodCode: "CASH");
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        const string requestId = "cod-confirm-replay";

        var initialResponse = await service.ConfirmCodOrderAsync(
            tenantId,
            order.Id,
            requestId,
            correlationId: "first-confirm",
            cancellationToken: CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        var replayResponse = await service.ConfirmCodOrderAsync(
            tenantId,
            order.Id,
            requestId,
            correlationId: "replay-confirm",
            cancellationToken: CancellationToken.None);

        Assert.Equal(initialResponse.OrderId, replayResponse.OrderId);
        Assert.Equal(initialResponse.OrderNumber, replayResponse.OrderNumber);
        Assert.Equal(initialResponse.ConfirmedAt, replayResponse.ConfirmedAt);

        var idempotencyRecord = await dbContext.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .SingleAsync();

        Assert.Equal(order.Id, idempotencyRecord.OrderId);
        Assert.Equal(StatusCodes.Status200OK, idempotencyRecord.ResponseStatusCode);
        Assert.False(string.IsNullOrWhiteSpace(idempotencyRecord.ResponsePayload));
        Assert.NotNull(idempotencyRecord.CompletedAtUtc);
    }

    [Fact]
    public async Task ConfirmCodOrderAsync_WhenIdempotencyKeyIsReusedForDifferentOrder_RejectsPayloadConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var firstOrder = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Cash,
            paymentMethodCode: "CASH");
        var secondOrder = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Cash,
            paymentMethodCode: "CASH");
        dbContext.Orders.AddRange(firstOrder, secondOrder);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        const string requestId = "cod-confirm-conflict";

        await service.ConfirmCodOrderAsync(
            tenantId,
            firstOrder.Id,
            requestId,
            correlationId: "first-confirm",
            cancellationToken: CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<PaymentOrderInvalidStateException>(() =>
            service.ConfirmCodOrderAsync(
                tenantId,
                secondOrder.Id,
                requestId,
                correlationId: "second-confirm",
                cancellationToken: CancellationToken.None));

        Assert.Equal("IDEMPOTENCY_KEY_REUSED", exception.ErrorCode);
    }

    [Fact]
    public async Task ConfirmCodOrderAsync_WhenMatchingClaimIsAlreadyInProgress_RejectsConcurrentReplay()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Cash,
            paymentMethodCode: "CASH");
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        const string requestId = "cod-confirm-in-progress";
        var storageKey = $"PAYMENT:COD_CONFIRM:{tenantId:N}:{requestId}";
        var requestHash = DeterministicRequestHasher.ComputeHash(new
        {
            Operation = "COD_CONFIRM",
            OrderId = order.Id
        });

        dbContext.OrderRequestIdempotencyRecords.Add(new OrderRequestIdempotency
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Key = storageKey,
            RequestHash = requestHash,
            OrderId = order.Id,
            ClaimToken = Guid.NewGuid().ToString("N"),
            ClaimTimestampUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<PaymentOrderInvalidStateException>(() =>
            service.ConfirmCodOrderAsync(
                tenantId,
                order.Id,
                requestId,
                correlationId: "concurrent-confirm",
                cancellationToken: CancellationToken.None));

        Assert.Equal("COD_CONFIRMATION_IN_PROGRESS", exception.ErrorCode);
    }

    private static PaymentService CreateService(
        ApplicationDbContext dbContext,
        IDataProtector? dataProtectorOverride = null)
    {
        var dataProtector = dataProtectorOverride ?? Mock.Of<IDataProtector>();
        var dataProtectionProvider = new Mock<IDataProtectionProvider>();
        dataProtectionProvider
            .Setup(provider => provider.CreateProtector(It.IsAny<string>()))
            .Returns(dataProtector);

        var runtimeApplicationService = new Mock<IManualPaymentRuntimeApplicationService>(MockBehavior.Strict);
        runtimeApplicationService
            .Setup(service => service.InitializeReferencePaymentAsync(
                It.IsAny<ManualPaymentRuntimeApplicationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManualPaymentRuntimeApplicationRequest request, CancellationToken _) =>
                new ManualPaymentRuntimeApplicationResult(
                    "manual-wallet-test",
                    request.Channel,
                    request.Channel.ToString().ToLowerInvariant(),
                    request.ExecutionTimestampUtc.AddHours(2),
                    ReferenceNumberRequired: true,
                    ProofRequired: true,
                    null!,
                    null!,
                    null!));

        var evidenceApplicationService = new Mock<IManualPaymentEvidenceApplicationService>(MockBehavior.Strict);
        evidenceApplicationService
            .Setup(service => service.AdmitEvidenceAsync(
                It.IsAny<ManualPaymentEvidenceApplicationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManualPaymentEvidenceApplicationResult(null!, null!, null!));

        var durableEvidenceAuthority = new Mock<IManualPaymentDurableEvidenceAuthority>(MockBehavior.Strict);
        durableEvidenceAuthority
            .Setup(authority => authority.WriteReceiptEvidence(
                It.IsAny<PaymentIntent>(),
                It.IsAny<string?>(),
                It.IsAny<ManualPaymentRuntimeApplicationResult>(),
                It.IsAny<ManualPaymentEvidenceApplicationResult>(),
                It.IsAny<ManualPaymentProofMetadata>()))
            .Returns((PaymentIntent _, string? metadata, ManualPaymentRuntimeApplicationResult _, ManualPaymentEvidenceApplicationResult _, ManualPaymentProofMetadata _) => metadata ?? "{}");
        durableEvidenceAuthority
            .Setup(authority => authority.ValidateReceiptEvidence(
                It.IsAny<PaymentIntent>(),
                It.IsAny<ManualPaymentDurableEvidencePurpose>()))
            .Returns((ManualPaymentDurableEvidenceFacts)null!);

        var lifecycleService = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        lifecycleService
            .Setup(service => service.CreateReviewablePaymentIntentAsync(
                It.IsAny<ReviewablePaymentIntentCreateRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReviewablePaymentIntentCreateRequest request, CancellationToken _) =>
            {
                var intent = new PaymentIntent
                {
                    Id = request.Id,
                    TenantId = request.TenantId,
                    OrderId = request.OrderId,
                    Amount = request.Amount,
                    Currency = request.Currency,
                    PaymentMethod = request.PaymentMethod,
                    Provider = PaymentProvider.ManualWallet,
                    PaymentReference = request.PaymentReference,
                    IdempotencyKey = request.IdempotencyKey,
                    CreatedAt = request.CreatedAtUtc,
                    Status = PaymentIntentStatus.Pending,
                    WalletType = request.WalletType,
                    WalletPhoneNumber = request.WalletPhoneNumber,
                    ProofImageUrl = request.ProofImageUrl,
                    ProofImageExpiresAtUtc = request.ProofImageRetentionExpiresAtUtc,
                    ProviderMetadata = request.ProviderMetadata
                };
                dbContext.PaymentIntents.Add(intent);
                return intent;
            });
        lifecycleService
            .Setup(service => service.AdmitPaymentReviewAsync(
                It.IsAny<PaymentReviewAdmissionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentTransitionResult.Succeeded(PaymentStatus.AwaitingVerification));

        return new PaymentService(
            dbContext,
            new RedisConnectionFactory(
                Options.Create(new RedisSettings { Enabled = false, InstanceName = "test" }),
                NullLogger<RedisConnectionFactory>.Instance),
            Options.Create(new RedisSettings { Enabled = false, InstanceName = "test" }),
            Mock.Of<IEventPublisher>(),
            Array.Empty<IPaymentAdapter>(),
            lifecycleService.Object,
            dataProtectionProvider.Object,
            Options.Create(new R2Options()),
            new CommerceReliabilityTelemetry(),
            NullLogger<PaymentService>.Instance,
            manualPaymentRuntimeApplicationService: runtimeApplicationService.Object,
            manualPaymentEvidenceApplicationService: evidenceApplicationService.Object,
            manualPaymentRetentionAuthority: new ManualPaymentRetentionAuthority(
                Options.Create(CreateManualPaymentOptions())),
            manualPaymentDurableEvidenceAuthority: durableEvidenceAuthority.Object);
    }

    private static ManualPaymentOptions CreateManualPaymentOptions() => new()
    {
        Enabled = true,
        Environment = ManualPaymentEnvironment.Sandbox,
        SandboxMode = true,
        FeatureFlags = new ManualPaymentFeatureFlags
        {
            Enabled = true,
            SandboxEnabled = true,
            ProofUploadEnabled = true
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
            ProofAccessTtl = TimeSpan.FromDays(7)
        },
        Channels =
        [
            new ManualPaymentChannelConfiguration
            {
                Channel = ManualPaymentChannel.VodafoneCash,
                Enabled = true,
                DisplayName = "Vodafone Cash",
                Instructions = "Transfer manually."
            }
        ]
    };

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = $"tenant-{tenantId:N}",
            Slug = $"tenant-{tenantId:N}",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        context.SaveChanges();
        return context;
    }

    private static Order SeedOrder(
        Guid tenantId,
        OrderStatus status,
        PaymentStatus paymentStatus,
        PaymentMethod paymentMethod = PaymentMethod.Card,
        string? paymentMethodCode = null)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            Status = status,
            PaymentStatus = paymentStatus,
            PaymentMethod = paymentMethod,
            PaymentMethodCode = paymentMethodCode ?? paymentMethod.ToString().ToUpperInvariant(),
            Total = 150m,
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow
        };
    }
}
